using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Alignment.BundleAdjusterStructures;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using OPS.Util;
using System.IO;
using System.Runtime.InteropServices;
using OPS.Plumbing;

namespace OPS.Alignment
{
    public class BundleAdjuster : PipelineRoutine
    {
        public BundleAdjuster(PipelineCore pipeline) : base(pipeline)
        {
        }


        struct FeatureIndex
        {
            public ImageRef Image;
            public int Index;

            public FeatureIndex(ImageRef image, int index)
            {
                Image = image;
                Index = index;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is FeatureIndex))
                {
                    return false;
                }

                var index = (FeatureIndex)obj;
                return EqualityComparer<ImageRef>.Default.Equals(Image, index.Image) &&
                       Index == index.Index;
            }

            public override int GetHashCode()
            {
                var hashCode = 227977205;
                hashCode = hashCode * -1521134295 + Image.GetHashCode();
                hashCode = hashCode * -1521134295 + Index.GetHashCode();
                return hashCode;
            }
        }
        class Track
        {
            public HashSet<FeatureIndex> projections;
            public int pointIdx;

            public Track()
            {
                projections = new HashSet<FeatureIndex>();
                pointIdx = -1;
            }
        }

        public void Adjust(AlignmentScene scene)
        {
            // scene.Root is the world coordinate system
            // AdjustedNodes are present on all frames to adjust
            // Mean transform will be assumed for all frames above adjusted nodes


            BundleAdjusterProblem problem = new BundleAdjusterProblem();
            List<AdjustedNode> toAdjust = scene.Root.GetComponentsInTree<AdjustedNode>().ToList();

            Matrix worldToRoot = scene.Root.Transform.WorldToLocal;
            Memoizer<CAHV, int> cameraModels = new Memoizer<CAHV, int>(problem.AddCameraModel);
            Dictionary<ImageRef, int> imageToCamera = new Dictionary<ImageRef, int>();
            Dictionary<int, SceneNode> transformToNode = new Dictionary<int, SceneNode>();
            Memoizer<SceneNode, int> nodeToTransform = new Memoizer<SceneNode, int>(node =>
            {
                bool _fixed = !node.HasComponent<AdjustedNode>();
                int idx = problem.AddTransform(node.Transform.Matrix, _fixed);
                if (!_fixed && node.HasComponent<NodeUncertainTransform>())
                {
                    var nut = node.GetComponent<NodeUncertainTransform>();
                    problem.AddPrior(idx, nut.UncertainTransform);
                }
                transformToNode[idx] = node;
                return idx;
            });
            Dictionary<ImageRef, List<int>> imageTransformLists = new Dictionary<ImageRef, List<int>>();
            
            // collect images
            foreach (var imgRefC in scene.Root.GetComponentsInTree<NodeImageReference>())
            {
                var cmod = GetImage(imgRefC.Reference).CameraModel;
                int cameraIdx = cameraModels[(CAHV)cmod];
                imageToCamera[imgRefC.Reference] = cameraIdx;

                // build list of transforms to apply to get to root
                List<int> transforms = new List<int>();
                SceneNode curr = imgRefC.Node;
                while (curr != null && curr != scene.Root)
                {
                    transforms.Add(nodeToTransform[curr]);
                    curr = curr.Parent;
                }

                imageTransformLists[imgRefC.Reference] = transforms;
            }

            // collect tracks
            Dictionary<FeatureIndex, Guid> featureToTrack = new Dictionary<FeatureIndex, Guid>();
            Dictionary<Guid, Track> tracks = new Dictionary<Guid, Track>();
            Action<Guid, Guid> mergeTracks = (one, two) =>
            {
                if (one == two) return;
                List<FeatureIndex> keys = featureToTrack.Keys.ToList();
                foreach (var feat in keys)
                {
                    if (featureToTrack[feat] == two)
                    {
                        featureToTrack[feat] = one;
                    }
                }
                tracks.Remove(two);
            };
            foreach (var corr in scene.Context.Correspondences)
            {
                var model = corr.Value.ModelImage;
                var data = corr.Value.DataImage;

                foreach (var pair in corr.Value.DataToModel)
                {
                    FeatureIndex dataFeat = new FeatureIndex(data, pair.Key);
                    FeatureIndex modelFeat = new FeatureIndex(model, pair.Value);

                    if (!featureToTrack.ContainsKey(dataFeat))
                    {
                        if (!featureToTrack.ContainsKey(modelFeat))
                        {
                            var trackId = Guid.NewGuid();
                            featureToTrack[dataFeat] = trackId;
                            featureToTrack[modelFeat] = trackId;
                            tracks[trackId] = new Track();
                        }
                        else
                        {
                            featureToTrack[dataFeat] = featureToTrack[modelFeat];
                        }
                    }
                    else
                    {
                        if (!featureToTrack.ContainsKey(modelFeat))
                        {
                            featureToTrack[modelFeat] = featureToTrack[dataFeat];
                        }
                        else
                        {
                            mergeTracks(featureToTrack[modelFeat], featureToTrack[dataFeat]);
                        }
                    }
                }
            }

            // assign projections to tracks
            foreach (var pair in featureToTrack)
            {
                var track = tracks[pair.Value];
                track.projections.Add(pair.Key);
            }
            foreach (var track in tracks.Values)
            {
                Vector3 initialPose;

                // triangulate initial guess
                {
                    List<Ray> rays = new List<Ray>(track.projections.Count);

                    Matrix M = Matrix.Identity * 0;
                    M[3, 3] = 1;
                    Vector4 b = new Vector4(0, 0, 0, 1);

                    foreach (var projection in track.projections)
                    {
                        var feat = scene.Context.DetectedFeatures[projection.Image][projection.Index];

                        var cameraSpace = GetImage(projection.Image).CameraModel.ProjectRay(feat.Location);
                        var cameraToWorld = scene.ImageToNode[projection.Image].Transform.LocalToWorld * worldToRoot;
                        var r = new Ray(Vector3.Transform(cameraSpace.Position, cameraToWorld), Vector3.TransformNormal(cameraSpace.Direction, cameraToWorld));

                        Matrix nnt = Matrix.Identity * 0;
                        for (int i = 0; i < 3; i++)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                nnt[i, j] = r.Direction[i] * r.Direction[j];
                                if (i == j) nnt[i, j] -= 1;
                            }
                        }

                        M += nnt;
                        b += Vector4.Transform(new Vector4(r.Position, 0), nnt);
                    }


                    Matrix inv = Matrix.Invert(M);
                    Vector4 x = Vector4.Transform(b, inv);
                    if (!double.IsNaN(x.X))
                    {
                        initialPose = new Vector3(x.X, x.Y, x.Z);
                    }
                    else
                    {
                        initialPose = Vector3.Zero;
                    }
                }

                track.pointIdx = problem.AddPoint(initialPose);

                // add projections to problem
                foreach (var projection in track.projections)
                {
                    var img = projection.Image;
                    var feat = scene.Context.DetectedFeatures[img][projection.Index];

                    Vector3 pointPos = problem.Points[track.pointIdx].Position;
                    problem.AddProjection(imageToCamera[img], imageTransformLists[img].ToArray(), track.pointIdx, feat.Location);
                }
            }

            Console.WriteLine("here");

            BundleAdjusterProblem result = null;
            TemporaryFile.GetAndDelete(".bin", inputFile =>
            {
                using (FileStream fs = new FileStream(inputFile, FileMode.Create))
                {
                    using (BinaryWriter bw = new BinaryWriter(fs))
                    {
                        problem.Write(bw);
                    }
                }
                TemporaryFile.GetAndDelete(".bin", (outputFile) =>
                {
                    ProgramRunner pr = new ProgramRunner("CeresBundler.exe", "\"" + inputFile + "\" \"" + outputFile + "\"", createNoWindow: false, useShellExecute: false);
                    pr.Run();

                    using (FileStream fs = new FileStream(outputFile, FileMode.Open))
                    {
                        using (BinaryReader br = new BinaryReader(fs))
                        {
                            result = BundleAdjusterProblem.Read(br);
                        }
                    }
                });
            });

            Console.WriteLine("Got result");
            for (int i = 0; i < result.Transforms.Count; i++)
            {
                var transform = result.Transforms[i];
                if (transform.Fixed) continue;

                var node = transformToNode[i];
                node.Transform.Matrix = transform.Matrix;
            }

            Mesh pc = new Mesh(capacity: result.Points.Count);
            foreach (var pt in result.Points)
            {
                if (Math.Abs(pt.W) > 1e-8)
                {
                    pc.Vertices.Add(new Vertex(pt.X / pt.W, pt.Y / pt.W, pt.Z / pt.W));
                }
            }
            pc.Save("d:\\bundle.ply");
        }
    }
}

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


        class FeatureIndex
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
            public HashSet<FeatureIndex> features;
            public HashSet<int> projections;
            public Vector3 position;
            public double error;
            public int pointIdx;

            public Track()
            {
                features = new HashSet<FeatureIndex>();
                projections = new HashSet<int>();
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
            Memoizer<Imaging.CameraModel, int> cameraModels = new Memoizer<Imaging.CameraModel, int>(problem.AddCameraModel);
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
                int cameraIdx = cameraModels[(Imaging.CameraModel)cmod];
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
            Dictionary<FeatureIndex, int> featureIndexToProjection = new Dictionary<FeatureIndex, int>();
            Dictionary<Guid, Track> tracks = new Dictionary<Guid, Track>();

            Func<Track, IEnumerable<FeatureIndex>, bool, double> computeMinErr = (track, extraProjs, set) =>
            {
                List<Ray> rays = new List<Ray>(track.features.Count);

                Matrix M = Matrix.Identity * 0;
                M[3, 3] = 1;
                Vector4 b = new Vector4(0, 0, 0, 1);

                IEnumerable<FeatureIndex> allProjections =
                (extraProjs != null) ? track.features.Concat(extraProjs) : track.features;

                foreach (var projection in allProjections)
                {
                    var feat = scene.DetectedFeatures[projection.Image][projection.Index];

                    var cameraSpace = GetImage(projection.Image).CameraModel.Unproject(feat.Location);
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
                    Vector3 pose = new Vector3(x.X, x.Y, x.Z);
                    double error = 0;
                    foreach (var proj in track.features)
                    {
                        var feat = scene.DetectedFeatures[proj.Image][proj.Index];
                        var cmod = problem.CameraModels[imageToCamera[proj.Image]];

                        var worldToCamera = Matrix.Invert(scene.ImageToNode[proj.Image].Transform.LocalToWorld * worldToRoot);
                        var cameraPt = Vector3.Transform(pose, worldToCamera);
                        try
                        {
                            var projected = cmod.Model.Project(cameraPt, out double range);
                            error += (projected - feat.Location).LengthSquared();
                        }
                        catch (DivideByZeroException)
                        {
                            if (set)
                            {
                                track.error = double.PositiveInfinity;
                            }
                            return double.PositiveInfinity;
                        }
                    }

                    if (set)
                    {
                        track.position = pose;
                        track.error = error;
                    }
                    return error;
                }
                else
                {
                    if (set)
                    {
                        track.error = double.PositiveInfinity;
                    }
                    return double.PositiveInfinity;
                }
            };
            Action<Guid, Guid> mergeTracks = (one, two) =>
            {
                if (one == two) return;
                foreach (var proj in tracks[two].features)
                {
                    featureToTrack[proj] = one;
                    tracks[one].features.Add(proj);
                }
                tracks.Remove(two);
            };

            foreach (var corr in scene.Correspondences)
            {
                var model = corr.Value.ModelImage;
                var data = corr.Value.DataImage;
                var modelModel = problem.CameraModels[imageToCamera[model]];
                var dataModel = problem.CameraModels[imageToCamera[data]];
                var modelToWorld = scene.ImageToNode[model].Transform.LocalToWorld * worldToRoot;
                var dataToWorld = scene.ImageToNode[data].Transform.LocalToWorld * worldToRoot;

                foreach (var pair in corr.Value.DataToModel)
                {
                    FeatureIndex dataFeat = new FeatureIndex(data, pair.Key);
                    FeatureIndex modelFeat = new FeatureIndex(model, pair.Value);

                    // Make sure both features have a (potentially single-projection) track
                    if (!featureToTrack.ContainsKey(dataFeat))
                    {
                        var trackId = Guid.NewGuid();
                        featureToTrack[dataFeat] = trackId;

                        var track = tracks[trackId] = new Track();
                        var feat = scene.DetectedFeatures[data][dataFeat.Index];
                        track.position = Vector3.Transform(dataModel.Model.Unproject(feat.Location, 100), dataToWorld);
                        track.error = 0;
                        track.features.Add(dataFeat);
                    }
                    if (!featureToTrack.ContainsKey(modelFeat))
                    {
                        var trackId = Guid.NewGuid();
                        featureToTrack[modelFeat] = trackId;

                        var track = tracks[trackId] = new Track();
                        var feat = scene.DetectedFeatures[model][modelFeat.Index];
                        track.position = Vector3.Transform(modelModel.Model.Unproject(feat.Location, 100), modelToWorld);
                        track.error = 0;
                        track.features.Add(modelFeat);
                    }

                    // Try merging the tracks
                    double oldErr = tracks[featureToTrack[modelFeat]].error + tracks[featureToTrack[dataFeat]].error;
                    Vector3 oldPos = tracks[featureToTrack[modelFeat]].position;
                    if (oldErr < 20) oldErr = 20;
                    double newErr = computeMinErr(tracks[featureToTrack[modelFeat]], tracks[featureToTrack[dataFeat]].features, true);

                    if (true || newErr < oldErr * 1.5)
                    {
                        mergeTracks(featureToTrack[modelFeat], featureToTrack[dataFeat]);
                    }
                    else
                    {
                        tracks[featureToTrack[modelFeat]].position = oldPos;
                    }
                }
            }

            // assign projections to tracks
            foreach (var track in tracks.Values)
            {
                if (track.features.Count < 2) continue;

                //track.position = Vector3.Zero;
                computeMinErr(track, null, true);

                if (track.pointIdx < 0)
                {
                    track.pointIdx = problem.AddPoint(track.position);
                }

                // add projections to problem
                foreach (var projection in track.features)
                {
                    var img = projection.Image;
                    var feat = scene.DetectedFeatures[img][projection.Index];
                    track.projections.Add(problem.AddProjection(imageToCamera[img], imageTransformLists[img].ToArray(), track.pointIdx, feat.Location));
                }
            }

            HashSet<int> badPoints = new HashSet<int>();

            for (int iter = 0; iter < 2; iter++)
            {
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
                    problem.Transforms[i] = transform;

                    var node = transformToNode[i];
                    node.Transform.Matrix = transform.Matrix;
                }
                problem.Points = result.Points;

                {
                    Mesh m = new Mesh(capacity: result.Points.Count);
                    for (int i = 0; i < result.Points.Count; i++)
                    {
                        m.Vertices.Add(new Vertex(result.Points[i].Position));
                    }
                    m.Save("D:\\bundlecloud.ply");
                }

                // Trim bad points
                List<double> trackErrors = new List<double>(tracks.Count);
                foreach (var track in tracks.Values)
                {
                    track.error = 0;
                    foreach (var projIdx in track.projections)
                    {
                        track.error += problem.EvaluateError(problem.Projections[projIdx]);
                    }
                    trackErrors.Add(track.error);
                }
                double errAvg = trackErrors.Sum() / trackErrors.Count;
                double errStd = Math.Sqrt(trackErrors.Sum(err => (err - errAvg) * (err - errAvg)) / trackErrors.Count);
                List<Guid> badTracks = new List<Guid>();
                SortedSet<int> badProjections = new SortedSet<int>();
                foreach (var trackId in tracks.Keys)
                {
                    var track = tracks[trackId];
                    if (track.error > errStd * 2)
                    {
                        badTracks.Add(trackId);
                        badPoints.Add(track.pointIdx);
                        foreach (var projIdx in track.projections)
                        {
                            badProjections.Add(projIdx);
                        }
                    }
                }
                Dictionary<int, int> projOldToNew = new Dictionary<int, int>();
                int newNumProjections = 0;
                for (int i = 0; i < problem.Projections.Count; i++)
                {
                    if (badProjections.Contains(i)) continue;
                    projOldToNew[i] = newNumProjections;
                    newNumProjections++;
                }
                foreach (var projIdx in badProjections.Reverse())
                {
                    problem.Projections.RemoveAt(projIdx);
                }
                foreach (var track in tracks.Values)
                {
                    HashSet<int> newProjs = new HashSet<int>();
                    foreach (var projIdx in track.projections)
                    {
                        if (!projOldToNew.ContainsKey(projIdx))
                        {
                            continue;
                        }
                        newProjs.Add(projOldToNew[projIdx]);
                    }
                    track.projections = newProjs;
                }
            }

        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment.BundleAdjusterStructures;

namespace OPS.Alignment
{
    public class BundleAdjuster
    {
        private ILog logger;

        public BundleAdjuster(ILog logger)
        {
            this.logger = logger;
        }

        class FeatureIndex
        {
            public string ImageUrl;
            public int Index;

            public FeatureIndex(string imageUrl, int index)
            {
                ImageUrl = imageUrl;
                Index = index;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is FeatureIndex))
                {
                    return false;
                }

                var index = (FeatureIndex)obj;
                return ImageUrl == index.ImageUrl && Index == index.Index;
            }

            public override int GetHashCode()
            {
                var hashCode = 227977205;
                hashCode = hashCode * -1521134295 + ImageUrl.GetHashCode();
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

        public void Adjust(AlignmentScene scene, int rounds = 2, string debugOutputDirectory = null)
        {
            // scene.Root is the world coordinate system
            // AdjustedNodes are present on all frames to adjust
            // Mean transform will be assumed for all frames above adjusted nodes

            BundleAdjusterProblem problem = new BundleAdjusterProblem();
            List<AdjustedNode> toAdjust = scene.Root.GetComponentsInTree<AdjustedNode>().ToList();

            if (logger != null)
            {
                logger.InfoFormat("Setting up bundle adjust of {0} images", toAdjust.Count());
            }

            Matrix worldToRoot = scene.Root.Transform.WorldToLocal;

            Dictionary<string, Imaging.CameraModel> cameraModels = new Dictionary<string, Imaging.CameraModel>();
            Memoizer<Imaging.CameraModel, int> cameraToIndex =
                new Memoizer<Imaging.CameraModel, int>(problem.AddCameraModel);
            Dictionary<string, int> imageToCamera = new Dictionary<string, int>();

            Dictionary<int, SceneNode> transformToNode = new Dictionary<int, SceneNode>();
            Memoizer<SceneNode, int> nodeToTransform = new Memoizer<SceneNode, int>(node =>
            {
                bool adjust = node.HasComponent<AdjustedNode>();
                int idx = problem.AddTransform(node.Transform.Matrix, !adjust);
                if (adjust && node.HasComponent<NodeUncertainTransform>())
                {
                    var nut = node.GetComponent<NodeUncertainTransform>();
                    problem.AddPrior(idx, nut.UncertainTransform);
                }
                transformToNode[idx] = node;
                return idx;
            });
            Dictionary<string, List<int>> imageTransformLists = new Dictionary<string, List<int>>();
            
            // collect images
            foreach (var img in scene.Root.GetComponentsInTree<NodeImage>())
            {
                cameraModels[img.Url] = img.CameraModel;
                imageToCamera[img.Url] = cameraToIndex[img.CameraModel];

                // build list of transforms to apply to get to root
                List<int> transforms = new List<int>();
                SceneNode curr = img.Node;
                while (curr != null && curr != scene.Root)
                {
                    transforms.Add(nodeToTransform[curr]);
                    curr = curr.Parent;
                }

                imageTransformLists[img.Url] = transforms;
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
                    var feat = scene.DetectedFeatures[projection.ImageUrl][projection.Index];

                    var cameraModel = cameraModels[projection.ImageUrl];
                    var cameraSpace = cameraModel.Unproject(feat.Location);
                    var cameraToWorld =
                        scene.ObservationUrlToNode[projection.ImageUrl].Transform.LocalToWorld * worldToRoot;
                    var r = new Ray(Vector3.Transform(cameraSpace.Position, cameraToWorld),
                                    Vector3.TransformNormal(cameraSpace.Direction, cameraToWorld));

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
                        var feat = scene.DetectedFeatures[proj.ImageUrl][proj.Index];
                        var cmod = cameraModels[proj.ImageUrl];

                        var obsToWorld = scene.ObservationUrlToNode[proj.ImageUrl].Transform.LocalToWorld * worldToRoot;
                        var worldToCamera = Matrix.Invert(obsToWorld);
                        var cameraPt = Vector3.Transform(pose, worldToCamera);
                        try
                        {
                            var projected = cmod.Project(cameraPt, out double range);
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

            int idxCurCorrespondence = 0;
            int numCorrespondences = scene.Correspondences.Count();
            foreach (var corr in scene.Correspondences)
            {
                if (logger != null)
                {
                    logger.DebugFormat("Processing correspondence {0} of {1}",
                                       idxCurCorrespondence++, numCorrespondences);
                }

                var modelUrl = corr.Value.ModelImageUrl;
                var dataUrl = corr.Value.DataImageUrl;
                var modelCam = cameraModels[modelUrl];
                var dataCam = cameraModels[dataUrl];
                var modelToWorld = scene.ObservationUrlToNode[modelUrl].Transform.LocalToWorld * worldToRoot;
                var dataToWorld = scene.ObservationUrlToNode[dataUrl].Transform.LocalToWorld * worldToRoot;

                foreach (var pair in corr.Value.DataToModel)
                {
                    FeatureIndex dataFeat = new FeatureIndex(dataUrl, pair.Key);
                    FeatureIndex modelFeat = new FeatureIndex(modelUrl, pair.Value);

                    // Make sure both features have a (potentially single-projection) track
                    if (!featureToTrack.ContainsKey(dataFeat))
                    {
                        var trackId = Guid.NewGuid();
                        featureToTrack[dataFeat] = trackId;

                        var track = tracks[trackId] = new Track();
                        var feat = scene.DetectedFeatures[dataUrl][dataFeat.Index];
                        track.position = Vector3.Transform(dataCam.Unproject(feat.Location, 100), dataToWorld);
                        track.error = 0;
                        track.features.Add(dataFeat);
                    }
                    if (!featureToTrack.ContainsKey(modelFeat))
                    {
                        var trackId = Guid.NewGuid();
                        featureToTrack[modelFeat] = trackId;

                        var track = tracks[trackId] = new Track();
                        var feat = scene.DetectedFeatures[modelUrl][modelFeat.Index];
                        track.position = Vector3.Transform(modelCam.Unproject(feat.Location, 100), modelToWorld);
                        track.error = 0;
                        track.features.Add(modelFeat);
                    }

                    // Try merging the tracks
                    double oldErr = tracks[featureToTrack[modelFeat]].error + tracks[featureToTrack[dataFeat]].error;
                    Vector3 oldPos = tracks[featureToTrack[modelFeat]].position;
                    if (oldErr < 20) oldErr = 20;
                    double newErr = computeMinErr(tracks[featureToTrack[modelFeat]], tracks[featureToTrack[dataFeat]].features, true);

                    if (newErr < oldErr * 1.5)
                    {
                        mergeTracks(featureToTrack[modelFeat], featureToTrack[dataFeat]);
                    }
                    else
                    {
                        tracks[featureToTrack[modelFeat]].position = oldPos;
                    }
                }
            }

            if (logger != null)
            {
                logger.InfoFormat("processed {0} correspondences", numCorrespondences);
            }

            // assign projections to tracks
            int numTracks = tracks.Values.Count;
            int idxTrack = 0;
            foreach (var track in tracks.Values)
            {
                if (logger != null)
                {
                    logger.DebugFormat("Processing track {0} of {1}", idxTrack++, numTracks);
                }

                if (track.features.Count < 2) continue;
                
                computeMinErr(track, null, true);

                if (track.pointIdx < 0)
                {
                    track.pointIdx = problem.AddPoint(track.position);
                }

                // add projections to problem
                foreach (var projection in track.features)
                {
                    var img = projection.ImageUrl;
                    var feat = scene.DetectedFeatures[img][projection.Index];
                    track.projections.Add(problem.AddProjection(imageToCamera[img], imageTransformLists[img].ToArray(),
                                                                track.pointIdx, feat.Location));
                }
            }
            if (logger != null)
            {
                logger.InfoFormat("processed {0} tracks", numTracks);
            }

            HashSet<int> badPoints = new HashSet<int>();

            for (int round = 0; round < rounds; round++)
            {
                if (logger != null)
                {
                    logger.InfoFormat("running Ceres round {0}", round);
                }

                double startTime = UTCTime.Now();
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
                        string exePath = Path.Combine("ExternalApps", "CeresBundler.exe");
                        ProgramRunner pr = new ProgramRunner(exePath, "\"" + inputFile + "\" \"" + outputFile + "\"",
                                                             createNoWindow: false, useShellExecute: false);
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

                if (logger != null)
                {
                    logger.InfoFormat("got ceres result after {0}s", UTCTime.Now() - startTime);
                }

                for (int i = 0; i < result.Transforms.Count; i++)
                {
                    var transform = result.Transforms[i];
                    if (transform.Fixed) continue;
                    problem.Transforms[i] = transform;

                    var node = transformToNode[i];
                    node.Transform.Matrix = transform.Matrix;
                }
                problem.Points = result.Points;

                if(debugOutputDirectory != null)
                {
                    logger.Info("Writing bundler debug data");
                    Mesh m = new Mesh(capacity: result.Points.Count);
                    for (int i = 0; i < result.Points.Count; i++)
                    {
                        m.Vertices.Add(new Vertex(result.Points[i].Position));
                    }
                    PathHelper.EnsureExists(debugOutputDirectory);
                    m.Save(Path.Combine(debugOutputDirectory, "bundlecloud.ply"));
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

                logger.InfoFormat("Identified {0} bad points on {1} tracks", badProjections.Count, badTracks.Count());

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
                    foreach (var oldProjIdx in track.projections)
                    {
                        if (!projOldToNew.ContainsKey(oldProjIdx))
                        {
                            continue;
                        }
                        newProjs.Add(projOldToNew[oldProjIdx]);
                    }
                    track.projections = newProjs;
                }

                logger.Info("Completed trimming bad points from tracks");
            }
        }
    }
}

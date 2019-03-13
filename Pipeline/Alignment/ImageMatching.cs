using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using log4net;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class ImageMatching
    {
        public enum MatcherType
        {
            EmguSIFT,
            KnownGeometry,
            BruteForce,
            CascadeHashing
        }
        public const MatcherType DEF_MATCHER_TYPE = MatcherType.CascadeHashing;
        public const int DEF_MIN_MATCHES = 20;
        public const double DEF_MAX_DESCRIPTOR_DISTANCE_RATIO = 0.9;
        public const double DEF_MAX_DESCRIPTOR_DISTANCE = 0;
        public const bool DEF_USE_KNOWN_GEOMETRY_FILTER = true;
        public const bool DEF_USE_MOISAN_STIVAL_FILTER = true;
        public const bool DEF_USE_GTM_FILTER = false;

        public class Options
        {
            public MatcherType MatcherType = DEF_MATCHER_TYPE;
            public int MinMatches = DEF_MIN_MATCHES;
            public double MaxDescriptorDistanceRatio = DEF_MAX_DESCRIPTOR_DISTANCE_RATIO;
            public double MaxDescriptorDistance = DEF_MAX_DESCRIPTOR_DISTANCE;
            public bool UseKnownGeometryFilter = DEF_USE_KNOWN_GEOMETRY_FILTER;
            public bool UseMoisanStivalFilter = DEF_USE_MOISAN_STIVAL_FILTER;
            public bool UseGTMFilter = DEF_USE_GTM_FILTER;
            public double KGFMahalanobisThreshold = KnownGeometryFilter.DEF_MAHALANOBIS_THRESHOLD;
            public double KGFMajorAxisThreshold = KnownGeometryFilter.DEF_MAJOR_AXIS_THRESHOLD;
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, string projectName,
                                                                   string modelUrl, string dataUrl,
                                                                   string modelFrameName, string dataFrameName,
                                                                   Options options = null)
        {
            //build a minimal scene graph containing these two frames and their ancestors
            var bsoOpts = new BuildSceneGraph.Options() {
                PreloadCaches = false, //in this situation it will in general be a loss to preload caches
                UseTransformPriors = true, //we build the scene graph bottom-up so the frame cache doesn't scan
                LoadFeatures = true //observation cache doesn't scan
            };
            BuildSceneGraph builder = new BuildSceneGraph(pipeline, projectName, bsoOpts);
            AlignmentScene scene = builder.BuildBottomUp(new[] { modelFrameName, dataFrameName });
            (new FrustumOverlapDetector(pipeline, pipeline)).MakeHulls(scene);
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl, options);
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl,
                                                                   Options options = null)
        {
            string rejectionReason;
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl, out rejectionReason, options);
        }


        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl,
                                                                   out string rejectionReason, Options options = null)
        {
            if (options == null)
            {
                options = new Options();
            }

            rejectionReason = null;

            var modelNode = scene.ObservationUrlToNode[modelUrl];
            var dataNode = scene.ObservationUrlToNode[dataUrl];

            var modelObs = modelNode.GetComponent<NodeObservation>().Observation;
            var dataObs = dataNode.GetComponent<NodeObservation>().Observation;

            string pairName = (new URLPair(modelUrl, dataUrl)).ToStringShort();
            pipeline.LogVerbose("{0} ({1} features, {2} features): (re)computing feature matches",
                                pairName,
                                scene.DetectedFeatures[modelUrl].Length,
                                scene.DetectedFeatures[dataUrl].Length);

            if (modelObs is RoverObservation && dataObs is RoverObservation)
            {
                var mro = modelObs as RoverObservation;
                var dro = dataObs as RoverObservation;
                pipeline.LogVerbose("{0} SiteDrives: {1}, {2}",
                                    pairName,
                                    (new SiteDrive(mro.Site, mro.Drive)).ToString(),
                                    (new SiteDrive(dro.Site, dro.Drive)).ToString());
            }

            IFeatureMatcher matcher = null;
            switch (options.MatcherType)
            {
                case MatcherType.EmguSIFT:
                {
                    matcher = new EmguSIFTMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
                case MatcherType.KnownGeometry:
                {
                    matcher = new KnownGeometryMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
                case MatcherType.BruteForce:
                {
                    matcher = new BruteForceMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
                case MatcherType.CascadeHashing:
                {
                    matcher = new CascadeHashingMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
            }

            pipeline.LogVerbose("{0} {1}: maxDescriptorDistanceRatio = {2}, minMatches = {3}" +
                                "useKnownGeometryFilter={4}, useMoisanStivalFilter={5}, useGTMFilter={6}",
                                pairName, matcher.GetType().Name, options.MaxDescriptorDistanceRatio,
                                options.MinMatches, options.UseKnownGeometryFilter, options.UseMoisanStivalFilter,
                                options.UseGTMFilter);

            var matches = matcher.Match(scene, modelUrl, dataUrl);

            if (matches.Count < options.MinMatches)
            {
                pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, matcher.GetType().Name,
                                    matches.Count, options.MinMatches);
                rejectionReason = string.Format("(step 0) {0} returned too few matches", matcher.GetType().Name);
                return null;
            }
            pipeline.LogVerbose("{0} {1}: {2} matches", pairName, matcher.GetType().Name, matches.Count);

            List<IMatchFilter> filters = new List<IMatchFilter>();
            if (options.MaxDescriptorDistance > 0)
            {
                filters.Add(new DescriptorDistanceFilter(options.MaxDescriptorDistance));
            }
            if (options.UseKnownGeometryFilter)
            {
                filters.Add(new KnownGeometryFilter(pipeline)
                            {
                                MahalanobisThreshold = options.KGFMahalanobisThreshold,
                                MajorAxisThreshold = options.KGFMajorAxisThreshold
                            });
            }
            if (options.UseMoisanStivalFilter)
            {
                filters.Add(new MoisanStivalFilter(pipeline));
            }
            if (options.UseGTMFilter)
            { 
                filters.Add(new GTMFilter());
            }
            int step = 1;
            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(scene, matches);
                if (matches.Count < options.MinMatches)
                {
                    pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, filter.GetType().Name,
                                        matches.Count, options.MinMatches);
                    rejectionReason = string.Format("(step {0}) {1} returned too few matches",
                                                    step, filter.GetType().Name);
                    return null;
                }
                pipeline.LogVerbose("{0} {1}: {2} -> {3}", pairName, filter.GetType().Name, oldCount, matches.Count);
                step++;
            }

            pipeline.LogVerbose("{0} {1}: {2} -> {3} feature matches, keeping", pairName, matcher.GetType().Name,
                                string.Join(", ", filters.Select(f => f.GetType().Name)), matches.Count);

            return new ComputedCorrespondence()
            {
                ModelFeaturesGuid = modelObs.FeaturesGuid,
                DataFeaturesGuid = dataObs.FeaturesGuid,
                Correspondence = matches
            };
        }

        public static bool SaveOverlap(PipelineCore pipeline, string projectName, Guid matchGuid,
                                       string modelObsName, string dataObsName)
        {
            var dbOverlap = Overlap.Create(pipeline, projectName, modelObsName, dataObsName);
            if (dbOverlap == null)
            {
                return false;
            }
            dbOverlap.MatchGuid = matchGuid;
            dbOverlap.Status = matchGuid != Guid.Empty ? Overlap.StatusType.Matched : Overlap.StatusType.Rejected;
            return dbOverlap.TrySave(pipeline);
        }

        public static AlignmentScene BuildSceneAndDetectOverlaps(PipelineCore pipeline, Project project,
                                                                 bool loadFeatures = true, bool redoOverlaps = false,
                                                                 bool onlyCrossSite = true,
                                                                 Func<Observation, bool> obsFilter = null,
                                                                 Func<string, string, bool> overlapFilter = null)
        {
            pipeline.LogInfo("building scene graph for {0}image matching",
                             onlyCrossSite ? "cross-site " : "");
            var sb = new BuildSceneGraph(pipeline, project.Name, new BuildSceneGraph.Options()
                                         {
                                             UseTransformPriors = true,
                                             LoadFeatures = loadFeatures,
                                             LoadOverlaps = !redoOverlaps,
                                             OnlyKeepImagesWithFeatures = true,
                                             OnlyKeepBestImages = true,
                                             OnlyCrossSiteDriveOverlaps = onlyCrossSite,
                                             IncludeObservation = obs => obsFilter == null || obsFilter(obs),
                                             IncludeOverlap = (n1, n2) => overlapFilter == null || overlapFilter(n1, n2)
                                         });
            var scene = sb.BuildTopDown(project.RootFrame);

            var fod = new FrustumOverlapDetector(pipeline, pipeline);

            //scene should have no overlaps if redoOverlaps is set because they shouldn't have been loaded
            //but it's more clear and doesn't hurt to also or with redoOverlaps here
            if (redoOverlaps || scene.Overlaps.Count == 0)
            {
                fod.Detect(scene, onlyCrossSite);
            }
            else
            {
                fod.MakeHulls(scene);
            }
            return scene;
        }
        
        public static Image DrawMatches(Image modelImg, Image dataImg, ImageFeature[] modelFeatures,
                                        ImageFeature[] dataFeatures, KeyValuePair<int, int>[] dataToModel,
                                        string modelName = null, string dataName = null, bool stretch = true)
        {
            var modelFeaturesForDataFeature = new Dictionary<int, HashSet<int>>();
            foreach (var pair in dataToModel)
            {
                int dataFeatureIndex = pair.Key;
                int modelFeatureIndex = pair.Value;
                if (!modelFeaturesForDataFeature.ContainsKey(dataFeatureIndex))
                {
                    modelFeaturesForDataFeature[dataFeatureIndex] = new HashSet<int>();
                }
                modelFeaturesForDataFeature[dataFeatureIndex].Add(modelFeatureIndex);
            }
            var matches = new VectorOfVectorOfDMatch();
            foreach (var pair in modelFeaturesForDataFeature)
            {
                int dataFeatureIndex = pair.Key;
                var matchesForDataFeature = new List<MDMatch>();
                foreach (int modelFeatureIndex in pair.Value)
                {
                    matchesForDataFeature.Add(new MDMatch() {
                            TrainIdx = modelFeatureIndex,
                            QueryIdx = dataFeatureIndex
                        });
                }
                matches.Push(new VectorOfDMatch(matchesForDataFeature.ToArray()));
            }
            var modelKeypoints = new VectorOfKeyPoint(modelFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());
            var dataKeypoints = new VectorOfKeyPoint(dataFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());
            var lineColor = new MCvScalar(0, 0, 255); //RGB
            var pointColor = new MCvScalar(255, 255, 0); //RGB
            var ret = new Image<Bgr, byte>(modelImg.Width + dataImg.Width, Math.Max(modelImg.Height, dataImg.Height));
            var modelImgEmgu =
                stretch ? (new Image(modelImg)).ApplyStdDevStretch().ToEmguGrayscale() : modelImg.ToEmguGrayscale();
            var dataImgEmgu =
                stretch ? (new Image(dataImg)).ApplyStdDevStretch().ToEmguGrayscale() : dataImg.ToEmguGrayscale();
            //opencv sometimes throws exceptions here, so roll our own replacement
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/439
            //Features2DToolbox.DrawMatches(modelImgEmgu, modelKeypoints, dataImgEmgu, dataKeypoints, matches, ret,
            //                              lineColor, pointColor, null,
            //                              Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            DrawMatches(modelImgEmgu, modelKeypoints, dataImgEmgu, dataKeypoints, matches, ret, lineColor, pointColor,
                        Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            if (dataName != null)
            {
                ret.Draw("data: " + dataName, new System.Drawing.Point(5, 30),
                         FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
                if (modelImg.Metadata is PDSMetadata)
                {
                    int sol = (new PDSParser((PDSMetadata)modelImg.Metadata)).PlanetDayNumber;
                    ret.Draw("sol" + sol, new System.Drawing.Point(5, 60),
                             FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
                }
            }
            if (modelName != null)
            {
                ret.Draw("model: " + modelName, new System.Drawing.Point(dataImg.Width + 5, 30),
                         FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
                if (modelImg.Metadata is PDSMetadata)
                {
                    int sol = (new PDSParser((PDSMetadata)modelImg.Metadata)).PlanetDayNumber;
                    ret.Draw("sol" + sol, new System.Drawing.Point(dataImg.Width + 5, 60),
                             FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
                }
            }
            return ret.ToOPSImage();
        }

        //basic replacement for Features2DToolbox.DrawMatches() which sometimes barfs
        //NOTE these functions draw the model image on the right and the data image on the left
        //our legacy code in MatchImage.cs is similar (and maybe should go away) but does the opposite...
        public static void DrawMatches(Image<Gray, byte> modelImage, VectorOfKeyPoint modelKeypoints,
                                       Image<Gray, byte> dataImage, VectorOfKeyPoint dataKeypoints,
                                       VectorOfVectorOfDMatch matches, Image<Bgr, byte> ret,
                                       MCvScalar lineColor, MCvScalar pointColor,
                                       Features2DToolbox.KeypointDrawType flags)
        {
            var pc = new Bgr(pointColor.V0, pointColor.V1, pointColor.V2);
            var lc = new Bgr(lineColor.V0, lineColor.V1, lineColor.V2);

            //don't import System.Drawing because it creates a conflict with "Image"
            ret.ROI = new System.Drawing.Rectangle(0, 0, dataImage.Width, dataImage.Height);
            dataImage.Convert<Bgr, byte>().CopyTo(ret);
            Features2DToolbox.DrawKeypoints(ret, dataKeypoints, ret, pc, flags);

            ret.ROI = new System.Drawing.Rectangle(dataImage.Width, 0, modelImage.Width, modelImage.Height);
            modelImage.Convert<Bgr, byte>().CopyTo(ret);
            Features2DToolbox.DrawKeypoints(ret, modelKeypoints, ret, pc, flags);

            //ret.ROI = new System.Drawing.Rectangle(0, 0, ret.Width, ret.Height); //tried this, doesn't work
            ret.ROI = new System.Drawing.Rectangle(0, 0, modelImage.Width + dataImage.Width,
                                                   Math.Max(modelImage.Height, dataImage.Height));
            var offset = new System.Drawing.SizeF(dataImage.Width, 0);

            for (int i = 0; i < matches.Size; i++)
            {
                for (int j = 0; j < matches[i].Size; j++)
                {
                    var mp = modelKeypoints[matches[i][j].TrainIdx];
                    var dp = dataKeypoints[matches[i][j].QueryIdx];
                    ret.Draw(new CircleF(mp.Point + offset, mp.Size), lc, 2);
                    ret.Draw(new CircleF(dp.Point, dp.Size), lc, 2);
                    ret.Draw(new LineSegment2DF(mp.Point + offset, dp.Point), lc, 1);
                }
            }
        }

        public static Mesh MakeMatchMesh(CameraModel modelCam, CameraModel dataCam,
                                         ImageFeature[] modelFeat, ImageFeature[] dataFeat,
                                         Matrix modelToRoot, Matrix dataToRoot,
                                         KeyValuePair<int, int>[] dataToModel)
        {
            var ret = new Mesh(hasNormals: true, hasColors: true);
            var lineColor = new Vector4(0, 0, 1, 0);
            var pointColor = new Vector4(0, 1, 0, 0);
            double pointSize = 0.05; //meters
            double lineSize = 0.02; //meters
            var pointMesh = BoundingBoxExtensions.MakeCube(pointSize).ToMesh(pointColor);
            var lineMesh = BoundingBoxExtensions.MakeCube(lineSize).ToMesh(lineColor);
            foreach (var match in dataToModel)
            {
                var df = dataFeat[match.Key];
                var mf = modelFeat[match.Value];
                if (df.Range > 0 && mf.Range > 0)
                {
                    var mp = Vector3.Transform(modelCam.Unproject(mf.Location, mf.Range), modelToRoot);
                    var dp = Vector3.Transform(dataCam.Unproject(df.Location, df.Range), dataToRoot);
                    ret.MergeWith(Mesh.Transformed(pointMesh, Matrix.CreateTranslation(mp)));
                    ret.MergeWith(Mesh.Transformed(pointMesh, Matrix.CreateTranslation(dp)));
                    var lineMat = BoundingBoxExtensions.StretchCubeAlongLineSegment(mp, dp, lineSize);
                    ret.MergeWith(Mesh.Transformed(lineMesh, lineMat));
                }
            }
            return ret;
        }
    }
}

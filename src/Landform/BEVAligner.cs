using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Xna.Framework;
using CommandLine;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;
using JPLOPS.ImageFeatures;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.AlignmentServer;

/// <summary>
/// Align sitedrives to each other using features detected in birds-eye-view renders.
///
/// Typically performed before heightmap-align in a contextual mesh workflow, but can be omitted or run after
/// heightmap-align.
///
/// Does nothing if there is only one sitedrive.
///
/// Typically reconstructs the sitedrive meshes using organized meshing from the observation point clouds, which results
/// in more detailed meshes than are typically loaded from some types of mission mesh RDRs.
///
/// The sitedrive meshes are rendered in a birds-eye orthographic view (BEV).  BEV rasterization is performed at a fixed
/// meters per pixel resolution, conveying scale invariance to the BEV images.  They are typically colored by the tilt
/// angle of the vertex normals relative to vertical, conveying lighting invariance.
///
/// Sparse external areas of the BEV images are clipped using a block-based heruistic algorithm.  Features are detected
/// typically with a FAST feature detector.  Features are matched across sitedrives by a combination of spatial
/// filtering, image descriptor distance, and RANSAC.  These 2D feature pairs are transformed in to 3D pairs
/// by combining each BEV with a DEM that is rendered from the same viewpoint, but coloring the mesh by vertex elevation
/// instead of tilt angle.  Outlier pairs are rejected with a mean absolute deviation filter.
///
/// A graph of sitedrives is formed with edges connecting sitedrives with sufficient numbers of feature matches.  Pose
/// optimization is performed on the graph.  The current implementation computes a spanning tree (or forest) and aligns
/// adjacent sitedrives pairwise with orgthogonal Procrustes.  Simultaneous alignment of the full graph using Levenberg
/// Marquardt is TODO.
///
/// Debug meshes can be optionally saved with the aligned and unaligned sitedrive meshes and the sample point pair
/// matches.
///
/// Example:
///
/// Landform.exe bev-align windjana --fixsitedrives 0311472
///
/// </summary>
namespace JPLOPS.Landform
{
    public enum AlignmentMode { Pairwise, Simultaneous, None };

    public enum CalfMode { None, Centroid, Temporal };

    [Verb("bev-align", HelpText = "birds eye view alignment")]
    public class BEVAlignerOptions : BEVCommandOptions
    {
        [Option(HelpText = "Don't adjust specified site drives, comma separated, or Newest, Oldest, Biggest, Smallest, ProjectThenBiggest", Default = "ProjectThenBiggest")]
        public string FixSiteDrives { get; set; }

        [Option(HelpText = "Alignment algorithm: Pairwise, Simultaneous, None (match only)", Default = AlignmentMode.Pairwise)]
        public AlignmentMode AlignmentMode { get; set; }

        [Option(HelpText = "Algorithm to bring un-aligned \"calf\" site drives along for the ride: None, Centroid (match to aligned site drive with closest horizontal centroid), Temporal (match to closest aligned site drive by acquisition time)", Default = CalfMode.Centroid)]
        public CalfMode CalfMode { get; set; }

        [Option(HelpText = "In pairwise alignment modes lower priority site drives will be aligned to higher priority ones (Newest, Oldest, Biggest, Smallest, ProjectThenBiggest)", Default = SiteDrivePriority.ProjectThenBiggest)]
        public SiteDrivePriority SiteDrivePriority { get; set; }

        [Option(HelpText = "Stop after rendering BEVs (and DEMs)", Default = false)]
        public bool OnlyRenderBEVs { get; set; }

        [Option(HelpText = "Stop after detecting features", Default = false)]
        public bool OnlyDetectFeatures { get; set; }

        [Option(HelpText = "Detector type", Default = FeatureDetector.DetectorType.FAST)]
        public FeatureDetector.DetectorType DetectorType { get; set; }

        [Option(HelpText = "Maximum number of features per image", Default = 50000)]
        public int MaxFeaturesPerImage { get; set; }

        [Option(HelpText = "Extra radius to cull features near invalid regions", Default = 4)]
        public int FeatureExtraInvalidRadius { get; set; }

        [Option(HelpText = "FAST detector threshold", Default = 5)]
        public int FASTThreshold { get; set; }

        [Option(HelpText = "Minimum feature response", Default = 10)]
        public double MinFeatureResponse { get; set; }

        [Option(HelpText = "Recompute existing features", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Recompute existing feature matches", Default = false)]
        public bool RedoMatches { get; set; }

        [Option(HelpText = "Search radius for feature matching in meters", Default = 1)]
        public double MatchRadius { get; set; }

        [Option(HelpText = "Max descriptor distance ratio", Default = 1)]
        public double MaxDescriptorDistanceRatio { get; set; }

        [Option(HelpText = "Max descriptor distance", Default = 500)]
        public double MaxDescriptorDistance { get; set; }

        [Option(HelpText = "Disable bidirectional feature matching", Default = false)]
        public bool NoBidirectionalMatching { get; set; }

        [Option(HelpText = "Max RANSAC tests", Default = 5000000)]
        public int MaxRansacTests { get; set; }

        [Option(HelpText = "Max RANSAC residual in meters", Default = 0.02)]
        public double MaxRansacResidual { get; set; }

        [Option(HelpText = "Max RANSAC feature match radius meters", Default = 0.05)]
        public double RansacMatchRadius { get; set; }

        [Option(HelpText = "Min RANSAC feature separation meters", Default = 0.05)]
        public double MinRansacSeparation { get; set; }

        [Option(HelpText = "Min RANSAC good matches", Default = 25)]
        public int MinRansacMatches { get; set; }

        [Option(HelpText = "Max RANSAC good matches", Default = 500)]
        public int MaxRansacMatches { get; set; }

        [Option(HelpText = "Spatial outlier number of mean absolute deviations", Default = 5)]
        public double SpatialOutlierMADs { get; set; }

        [Option(HelpText = "Write RANSAC debug images", Default = false)]
        public bool WriteRansacDebug { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = true)]
        public override bool NoOrbital { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSurface { get; set; }
    }

    public class BEVAligner : BEVCommand
    {
        private const string OUT_DIR = "alignment/BEVProducts";

        private BEVAlignerOptions options;

        //sitedrive => features sorted by increasing distance to origin of sitedrive
        private ConcurrentDictionary<SiteDrive, ImageFeature[]> features =
            new ConcurrentDictionary<SiteDrive, ImageFeature[]>();

        //modelSiteDrive-dataSiteDrive => feature matches
        private ConcurrentDictionary<string, FeatureMatch[]> matches =
            new ConcurrentDictionary<string, FeatureMatch[]>();

        //modelSiteDrive-dataSiteDrive => feature matches
        private ConcurrentDictionary<string, FeatureMatch[]> ransacMatches =
            new ConcurrentDictionary<string, FeatureMatch[]>();

        //modelSiteDrive-dataSiteDrive => (modelPoint, dataPoint), (modelPoint, dataPoint), ... in project root frame
        private ConcurrentDictionary<string, SpatialMatch[]> spatialMatches =
            new ConcurrentDictionary<string, SpatialMatch[]>();

        //(modelSiteDrive, dataSiteDrive), (modelSiteDrive, dataSiteDrive), ...
        List<Tuple<SiteDrive, SiteDrive>> siteDrivePairs = new List<Tuple<SiteDrive, SiteDrive>>();
        
        private HashSet<SiteDrive> fixedSiteDrives = new HashSet<SiteDrive>();

        public BEVAligner(BEVAlignerOptions options) : base(options)
        {
            this.options = options;

            if (options.Redo)
            {
                options.RedoBEVs = true;
                options.RedoFeatures = true;
                options.RedoMatches = true;
                options.UsePriors = true;
            }
            else if (options.RedoBEVs)
            {
                options.RedoFeatures = true;
                options.RedoMatches = true;
                options.UsePriors = true;
            }
            else if (options.RedoFeatures)
            {
                options.RedoMatches = true;
                options.UsePriors = true;
            }
        }

        protected override HashSet<TransformSource> GetDefaultExcludedAdjustedTransformSources()
        {
            return new HashSet<TransformSource>()
            { TransformSource.LandformBEV, TransformSource.LandformBEVRoot, TransformSource.LandformBEVCalf };
        }

        public int Run()
        {
            try
            {
                if (options.NoSurface)
                {
                    throw new Exception("--nosurface not supported for this command");
                }

                if (!ParseArgumentsAndLoadCaches(OUT_DIR))
                {
                    return 0; //help
                }

                if (siteDrives.Length < 2 && !(options.OnlyRenderBEVs || options.OnlyDetectFeatures))
                {
                    pipeline.LogWarn("at least two site drives required");
                    StopStopwatch();
                    return 0;
                }

                pipeline.LogInfo("computing birds eye view alignment for {0} site drives", siteDrives.Length);

                RunPhase("load or render birds eye views", LoadOrRenderBEVs); //observations -> bevs, dems

                if (options.OnlyRenderBEVs)
                {
                    pipeline.LogInfo("rendered birds eye views for {0} site drives ({1:F3}s)",
                                     bevs.Count, 0.001 * stopwatch.ElapsedMilliseconds);
                    StopStopwatch();
                    return 0;
                }

                RunPhase("load or detect features", LoadOrDetectFeatures); //bevs -> features

                if (options.OnlyDetectFeatures)
                {
                    pipeline.LogInfo("rendered birds eye views for {0} site drives and detected features ({1:F3}s)",
                                     bevs.Count, 0.001 * stopwatch.ElapsedMilliseconds);
                    StopStopwatch();
                    return 0;
                }

                //some BEVs may have failed to render
                if (siteDrives.Length < 2)
                {
                    pipeline.LogWarn("at least two site drives required");
                    StopStopwatch();
                    return 0;
                }

                RunPhase("compute site drive pairs", ComputePairs); //siteDrives -> siteDrivePairs

                //at least one member of each pair is not in fixedSiteDrives
                if (siteDrivePairs.Count == 0)
                {
                    pipeline.LogWarn("at least one movable site drive required");
                    StopStopwatch();
                    return 0;
                }

                int nm = 0, na = 0;

                //siteDrivePairs, features -> spatialMatches
                RunPhase("load or match feature pairs", () => { nm = LoadOrMatchPairs(); });

                //spatialMatches -> LandformBEV aligned FrameTransforms
                RunPhase("compute alignment", () => { na = Align(); });
                
                bool matchOnly = options.AlignmentMode == AlignmentMode.None;
                pipeline.LogInfo("matched {0}{1} site drives from {2} birds eye views",
                                 matchOnly ? "" : "and aligned ", matchOnly ? nm : na, bevs.Count);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        /// <summary>
        /// populate features from database or bevs  
        /// </summary>
        private void LoadOrDetectFeatures()
        {
            if (options.RedoFeatures || !LoadFeatures())
            {
                DetectFeatures();
                if (!options.NoSave)
                {
                    SaveFeatures();
                }
            }

            if (options.WriteDebug)
            {
                double crossRadius = 0.05 * BEVPixelsPerMeter, circleRadius = 0.5 * BEVPixelsPerMeter;
                void drawOrigin(Image<Bgr, byte> img, Vector2 pixel, Vector3 color)
                {
                    System.Drawing.PointF toPointF(Vector2 v)
                    {
                        return new System.Drawing.PointF((float)v.X, (float)v.Y);
                    }
                    LineSegment2DF toLineSegment2DF(Vector2 a, Vector2 b)
                    {
                        return new LineSegment2DF(toPointF(a), toPointF(b));
                    }
                    var bgr = new Bgr((float)color.X * 255, (float)color.Y * 255, (float)color.Z * 255); //actually RGB
                    if (crossRadius > 0)
                    {
                        var cr = crossRadius;
                        img.Draw(toLineSegment2DF(pixel + new Vector2(-cr, 0), pixel + new Vector2(cr, 0)), bgr, 2);
                        img.Draw(toLineSegment2DF(pixel + new Vector2(0, -cr), pixel + new Vector2(0, cr)), bgr, 2);
                    }
                    if (circleRadius > 0)
                    {
                        var cr = circleRadius;
                        img.Draw(new CircleF(toPointF(pixel), (float)cr), bgr, 2);
                    }
                }

                double startSec = UTCTime.Now();
                int np = 0, nc = 0;
                CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                        Interlocked.Increment(ref np);
                        if (!options.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} birds eye view feature images in parallel, completed {1}/{2}",
                                             np, nc, siteDrives.Length);
                        }
                        var bev = bevs[siteDrive];
                        var mask = bev.MaskToImage(valid: 1, invalid: 0);
                        var feat = features[siteDrive];
                        var img = ImageFeature.DrawFeaturesEmgu(bev, mask, feat, siteDrive.ToString(), stretch: false);
                        foreach (var otherSiteDrive in siteDrives)
                        {
                            var pixel = BEVPointToPixel(BestTransform, Vector3.Zero, otherSiteDrive, siteDrive);
                            var color = new Vector3(otherSiteDrive != siteDrive ? 0 : 1,
                                                    otherSiteDrive != siteDrive ? 1 : 0,
                                                    0);
                            drawOrigin(img, pixel, color);
                        }
                        SaveImage(img.ToOPSImage(), siteDrive + "_BEV_Features");
                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    });
                pipeline.LogInfo("saved {0} birds eye view feature images ({1:F3}s)", siteDrives.Length,
                                 UTCTime.Now() - startSec);
            }
        }

        /// <summary>
        /// detect features that were not loaded from database
        /// </summary>
        private void DetectFeatures()
        {
            double startSec = UTCTime.Now();
            var featuresNeeded = siteDrives.Where(sd => !features.ContainsKey(sd));
            pipeline.LogInfo("detecting {0} features in {1} birds eye views...", options.DetectorType,
                             featuresNeeded.Count());

            var detectorOpts = new FeatureDetector.Options()
            {
                DetectorType = options.DetectorType,
                MinResponse = options.MinFeatureResponse,
                MaxFeatures = options.MaxFeaturesPerImage,
                ExtraInvalidRadius = options.FeatureExtraInvalidRadius,
                FASTThreshold = options.FASTThreshold,
                FeaturesPerImageBucketSize = 1000,
                FeaturesPerSizeBucketSize = 5,
                FeaturesPerResponseBucketSize = 10,
            };
            FeatureDetector detector = new FeatureDetector(detectorOpts, pipeline);

            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(featuresNeeded, siteDrive => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("detecting features for {0} site drives in parallel, completed {1}/{2}",
                                         np, nc, siteDrives.Length);
                    }

                    var origin = sdOriginPixel[siteDrive];

                    FeatureDetector.FeatureSortKey sortByDistance =
                    (SIFTFeature f) => Vector2.DistanceSquared(f.Location, origin);

                    var bev = bevs[siteDrive];
                    var mask = bev.MaskToImage(valid: 1, invalid: 0);

                    var feat = features[siteDrive] = detector.Detect(bev, mask, sortByDistance);

                    pipeline.LogVerbose("detected {0} {1} features in {2}x{3} birds eye view for {4}, " +
                                        "max features {5}, extra invalid radius {6}, FAST threshold {7}",
                                        feat.Length, options.DetectorType, bev.Width, bev.Height, siteDrive,
                                        options.MaxFeaturesPerImage, options.FeatureExtraInvalidRadius,
                                        options.FASTThreshold);

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            if (options.Verbose)
            {
                detector.DumpHistograms(pipeline);
            }

            pipeline.LogInfo("detected features for {0} birds eye views ({1:F3}s)",
                             features.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate features from database
        /// returns true iff all were loaded successfully
        /// </summary>
        private bool LoadFeatures()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                    var rec = BirdsEyeViewFeatures.Find(pipeline, project.Name, siteDrive.ToString());
                    if (rec != null &&
                        rec.DetectorType == options.DetectorType &&
                        rec.MinFeatureResponse == options.MinFeatureResponse &&
                        rec.MaxFeatures == options.MaxFeaturesPerImage &&
                        rec.ExtraInvalidRadius == options.FeatureExtraInvalidRadius &&
                        rec.FASTThreshold == options.FASTThreshold)
                    {
                        features[siteDrive] = pipeline
                            .GetDataProduct<FeaturesDataProduct>(project, rec.FeaturesGuid, noCache: true)
                            .Features;
                    }
                });
            pipeline.LogInfo("loaded {0} birds eye view features ({1:F3}s)", features.Count, UTCTime.Now() - startSec);
            return features.Count == siteDrives.Length;
        }

        /// <summary>
        /// save features and associated metadata to database
        /// </summary>
        private void SaveFeatures()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(features, pair => {
                    var siteDrive = pair.Key;
                    var features = pair.Value;
                    BirdsEyeViewFeatures.Create(pipeline, project, siteDrive.ToString(), features, options.DetectorType,
                                                options.MinFeatureResponse, options.MaxFeaturesPerImage,
                                                options.FeatureExtraInvalidRadius, options.FASTThreshold);
                });
            pipeline.LogInfo("saved {0} birds eye view features ({1:F3}s)", features.Count, UTCTime.Now() - startSec);
        }
        
        /// <summary>
        /// populates matches[modelSiteDrive-dataSiteDrive] from features
        /// assumes features[siteDrive] are sorted by increasing distance to origin of siteDrive
        /// </summary>
        private int MatchFeatures(SiteDrive modelSiteDrive, SiteDrive dataSiteDrive)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("matching features in birds eye views for site drives {0} (model) and  {1} (data)...",
                             modelSiteDrive, dataSiteDrive);

            //return the index of the first entry in distances that is >= distance
            //yes there is a built-in Array.BinarySearch()
            //but here we can control behavior when distance is not actually present in distances
            int binarySearch(double[] distances, double distance)
            {
                int l = 0, u = distances.Length - 1;
                while (u - l > 1)
                {
                    var m = (u + l) / 2;
                    if (distance <= distances[m])
                    {
                        u = m;
                    }
                    else
                    {
                        l = m;
                    }
                }
                return u;
            }
            
            IEnumerable<FeatureMatch> matchPair(SiteDrive model, SiteDrive data)
            {
                var modelFeatures = features[model];
                var dataFeatures = features[data];

                //pixel corresponding to origin of model sitedrive in model BEV
                var modelOrigin = sdOriginPixel[model];

                //pixel corresponding to origin of data sitedrive in data BEV
                var dataOrigin = sdOriginPixel[data];

                //pixel corresponding to origin of data sitedrive in model BEV
                var dataOriginInModel = BEVPointToPixel(BestTransform, Vector3.Zero, data, model);
                
                //distance in pixels of model feature to origin of model sitedrive in model BEV
                var modelDistances = modelFeatures.Select(f => Vector2.Distance(f.Location, modelOrigin)).ToArray();

                //NOTE: features for a site drive are already sorted by distance to origin of that site drive
                
                double radius = options.MatchRadius * BEVPixelsPerMeter;
                
                for (int i = 0; i < dataFeatures.Length; i++)
                {
                    var df = dataFeatures[i];
                    var dfInModel = dataOriginInModel + (df.Location - dataOrigin);
                    var r = Vector2.Distance(dfInModel, modelOrigin);
                    int minSearchIndex = binarySearch(modelDistances, r - radius);
                    int maxSearchIndex = binarySearch(modelDistances, r + radius) - 1;
                    if (maxSearchIndex >= minSearchIndex)
                    {
                        var match =
                            BruteForceMatcher.FindBestModelFeatureForDataFeature
                            (modelFeatures, dataFeatures, i,
                             options.MaxDescriptorDistanceRatio,
                             mf => Vector2.Distance(mf.Location, dfInModel) <= radius,
                             minSearchIndex, maxSearchIndex);
                        if (match != null && match.DescriptorDistance <= options.MaxDescriptorDistance)
                        {
                            yield return match;
                        }
                    }
                }
            }

            var best = new Dictionary<FeatureMatch, double>();
            int d2m = 0, m2d = 0;

            foreach (var match in matchPair(modelSiteDrive, dataSiteDrive))
            {
                d2m++;
                best[match] = match.DescriptorDistance;
            }

            if (!options.NoBidirectionalMatching)
            {
                foreach (var match in matchPair(dataSiteDrive, modelSiteDrive))
                {
                    var tmp = match.ModelIndex;
                    match.ModelIndex = match.DataIndex;
                    match.DataIndex = tmp;
                    if (!best.ContainsKey(match))
                    {
                        best[match] = match.DescriptorDistance;
                        m2d++;
                    }
                    else if (best[match] > match.DescriptorDistance)
                    {
                        best[match] = match.DescriptorDistance;
                        d2m--;
                        m2d++;
                    }
                }
            }
                
            var pair = modelSiteDrive + "-" + dataSiteDrive;

            var matchArray = matches[pair] = best.Keys.OrderBy(m => m.DescriptorDistance).ToArray();

            if (options.Verbose)
            {
                var histogram = new Histogram(50, pair + " matches", "distance");
                foreach (var match in matchArray)
                {
                    histogram.Add(match.DescriptorDistance);
                }
                histogram.Dump(pipeline);
            }

            int nm = matchArray.Length;
            pipeline.LogInfo("{0} feature matches for site drives {1} (model) and {2} (data) ({3} d2m, {4} m2d) " +
                             "({5:F3}s)", nm, modelSiteDrive, dataSiteDrive, d2m, m2d, UTCTime.Now() - startSec);
            return nm;
        }

        /// <summary>
        /// populates ransacMatches[modelSiteDrive-dataSiteDrive] from corresponding matches and features
        /// </summary>
        private int RansacMatches(SiteDrive modelSiteDrive, SiteDrive dataSiteDrive)
        {
            var pair = modelSiteDrive + "-" + dataSiteDrive;
            var matchArray = matches[pair];
            var nm = matchArray.Length;

            double startSec = UTCTime.Now();
            pipeline.LogInfo("RANSACing {0} feature matches for site drives {1} (model) and  {2} (data)...",
                             nm, modelSiteDrive, dataSiteDrive);

            var modelFeatures = features[modelSiteDrive];
            var dataFeatures = features[dataSiteDrive];

            //pixel corresponding to origin of model sitedrive in model BEV
            var modelOrigin = sdOriginPixel[modelSiteDrive];

            //pixel corresponding to origin of data sitedrive in data BEV
            var dataOrigin = sdOriginPixel[dataSiteDrive];

            //pixel corresponding to origin of data sitedrive in model BEV
            var dataOriginInModel = BEVPointToPixel(BestTransform, Vector3.Zero, dataSiteDrive, modelSiteDrive);

            //pixel offsets corresponding to model features relative to data sitedrive origin in model BEV
            var modelPts = matchArray
                .Select(m => modelFeatures[m.ModelIndex].Location - dataOriginInModel)
                .ToArray();

            //pixel offsets corresponding to data features relative to data sitedrive origin in model BEV
            var dataPtsInModel = matchArray
                .Select(m => dataFeatures[m.DataIndex].Location - dataOrigin)
                .ToArray();

            var bestTransform = new RigidTransform2D();
            var bestMatches = new List<int>(nm);
            var tmpMatches = new List<int>(nm);
            double bestResidual = double.PositiveInfinity;

            double radius = options.RansacMatchRadius * BEVPixelsPerMeter;
            double radiusSquared = radius * radius;

            double minSep = options.MinRansacSeparation * BEVPixelsPerMeter;
            double minSepSquared = minSep * minSep;

            var maxResidual = options.MaxRansacResidual * BEVPixelsPerMeter;

            var random = NumberHelper.MakeRandomGenerator();
            int[,] shuffle = null;
            HashSet<Tuple<int, int>> alreadyTried = null;
            int maxTests = 0;
            long totalCombinations = ((long)nm) * (((long)nm) - 1) / 2; //nm choose 2
            if (totalCombinations < 2 * (long)(options.MaxRansacTests))
            {
                pipeline.LogVerbose("generating random shuffle of {0} feature pairs for {1}", totalCombinations, pair);

                //the total number of combinations is tractable
                //so enumerate all combinations, randomly shuffle, take at most MaxRansacTests of them
                shuffle = new int[(int)totalCombinations, 2]; 
                int n = 0;
                for (int i = 0; i < nm; i++)
                {
                    for (int j = i + 1; j < nm; j++)
                    {
                        shuffle[n, 0] = i;
                        shuffle[n, 1] = j;
                        n++;
                    }
                }

                //Fisher-Yates shuffle
                void swap(int i, int j, int k)
                {
                    var t = shuffle[i, k];
                    shuffle[i, k] = shuffle[j, k];
                    shuffle[j, k] = t;
                }
                for (int i = 0; i < (int)totalCombinations - 1; i++)
                {
                    int j = random.Next(i, (int)totalCombinations);
                    swap(i, j, 0);
                    swap(i, j, 1);
                }

                maxTests = (int)Math.Min(totalCombinations, options.MaxRansacTests);
            }
            else
            {
                pipeline.LogVerbose("random shuffle of {0} feature pairs for {1} too big, using probabilistic sampling",
                                    totalCombinations, pair);
                //if the total number of combinations is more than twice MaxRansacTests then
                //avoid allocating shuffle which could be gigantic
                //in this case we instead throw dice to generate combinations
                //but keep track of the ones we've already tried and re-throw if we get a dupe
                //since we'll be trying at most half of the total possible combinations
                //we should't spend too much time re-throwing
                alreadyTried = new HashSet<Tuple<int, int>>();
                maxTests = options.MaxRansacTests;
            }

            pipeline.LogInfo("RANSACing {0} match pairs for {1}", maxTests, pair);
            int nt;
            int maxMatches = 0;
            for (nt = 0; nt < maxTests; nt++)
            {
                Tuple<int, int> seeds = null;
                if (shuffle != null)
                {
                    seeds = new Tuple<int, int>(shuffle[nt, 0], shuffle[nt, 1]);
                }
                else
                {
                    do
                    {
                        int j = random.Next(0, nm);
                        int k = random.Next(0, nm);
                        seeds = new Tuple<int, int>(Math.Min(j, k), Math.Max(j, k)); //canonical order Item1 < item2
                    }
                    while (seeds.Item1 == seeds.Item2 || alreadyTried.Contains(seeds));
                    alreadyTried.Add(seeds);
                }

                if (minSepSquared > 0 &&
                    (Vector2.DistanceSquared(dataPtsInModel[seeds.Item1], dataPtsInModel[seeds.Item2]) < minSepSquared
                     || Vector2.DistanceSquared(modelPts[seeds.Item1], modelPts[seeds.Item2]) < minSepSquared))
                {
                    continue;
                }

                var xform =
                    RigidTransform2D.Estimate(new [] { dataPtsInModel[seeds.Item1], dataPtsInModel[seeds.Item2] },
                                              new [] { modelPts[seeds.Item1], modelPts[seeds.Item2] },
                                              out double residual);

                if (residual > bestResidual)
                {
                    continue;
                }

                tmpMatches.Clear();
                for (int j = 0; j < nm; j++)
                {
                    var d = Vector2.DistanceSquared(xform.Transform(dataPtsInModel[j]), modelPts[j]);
                    if (d < radiusSquared)
                    {
                        bool ok = true;
                        if (minSepSquared > 0)
                        {
                            foreach (var k in tmpMatches)
                            {
                                if (Vector2.DistanceSquared(dataPtsInModel[j], dataPtsInModel[k]) < minSepSquared ||
                                    Vector2.DistanceSquared(modelPts[j], modelPts[k]) < minSepSquared)
                                {
                                    ok = false;
                                    break;
                                }
                            }
                        }
                        if (ok)
                        {
                            tmpMatches.Add(j);
                        }
                    }
                    if (tmpMatches.Count >= options.MaxRansacMatches)
                    {
                        break;
                    }
                }

                maxMatches = Math.Max(maxMatches, tmpMatches.Count);

                if (tmpMatches.Count < options.MinRansacMatches)
                {
                    continue;
                }

                xform = RigidTransform2D.Estimate(tmpMatches.Select(j => dataPtsInModel[j]).ToArray(),
                                                  tmpMatches.Select(j => modelPts[j]).ToArray(),
                                                  out residual);

                //if (residual < bestResidual)
                if (tmpMatches.Count() > bestMatches.Count())
                {
                    bestResidual = residual;
                    bestTransform = xform;
                    bestMatches.Clear();
                    bestMatches.AddRange(tmpMatches);
                }

                if (bestResidual < maxResidual)
                {
                    break;
                }
            }

            if (options.WriteRansacDebug)
            {
                var mf = bestMatches
                    .Select(m => modelFeatures[matchArray[m].ModelIndex])
                    .Cast<SIFTFeature>()
                    .CastToMKeyPoint()
                    .ToArray();
                
                void writeImage(string suffix, Func<Vector2, Vector2> dataPointTransform)
                {
                    var df = bestMatches
                        .Select(m =>
                                {
                                    var f = new SIFTFeature((SIFTFeature)(dataFeatures[matchArray[m].DataIndex]));
                                    f.Location = dataPointTransform(dataPtsInModel[m]) + dataOriginInModel;
                                    return f;
                                })
                        .CastToMKeyPoint()
                        .ToArray();
                    
                    var img = bevs[modelSiteDrive].ToEmgu<Bgr>();
                    
                    Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(mf), img, new Bgr(255, 0, 0), //RGB
                                                    Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
                    
                    Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(df), img, new Bgr(0, 255, 0), //RGB
                                                    Features2DToolbox.KeypointDrawType.DrawRichKeypoints);

                    SaveImage(img.ToOPSImage(), pair + "_BEV_RANSAC" + suffix);
                }
                
                writeImage("_0_Priors", pt => pt);
                writeImage("_1_Rotation", pt => bestTransform.Rotate(pt));
                writeImage("_2_Solved", pt => bestTransform.Transform(pt));
            }
                        
            ransacMatches[pair] = bestMatches.Select(m => matchArray[m]).ToArray();

            nm = bestMatches.Count;
            var msg =
                nm > 0 ? string.Format(", best transform ({0:F3}m, {1:F3}m, {2:F3}deg), residual {3:F3}m, {4} matches",
                                       bestTransform.Translation.X * BEVMetersPerPixel,
                                       bestTransform.Translation.Y * BEVMetersPerPixel,
                                       MathHelper.ToDegrees(bestTransform.Rotation),
                                       bestResidual * BEVMetersPerPixel, nm)
                : "";
            pipeline.LogInfo("performed {0}/{1} ransac tests for {2} ({3} combinations), max matches {4}{5} ({6:F3}s)",
                             nt, maxTests, pair, totalCombinations, maxMatches, msg, UTCTime.Now() - startSec);
            return nm;
        }

        /// <summary>
        /// compute spatialMatches from ransacMatches, features, and dems
        /// </summary>
        private int SpatializeMatches(SiteDrive modelSiteDrive, SiteDrive dataSiteDrive)
        {
            var modelFeatures = features[modelSiteDrive];
            var dataFeatures = features[dataSiteDrive];

            //pixel corresponding to world origin in model BEV
            var modelOrigin = rootOriginPixel[modelSiteDrive];

            //pixel corresponding to world origin in data BEV
            var dataOrigin = rootOriginPixel[dataSiteDrive];

            var modelDEM = dems[modelSiteDrive];
            var dataDEM = dems[dataSiteDrive];

            mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevation, out Vector3 right, out Vector3 down);

            //DEM elevations are relative to site drive origin
            double modelSiteDriveOriginElevation =
                Vector3.Dot(Vector3.Transform(Vector3.Zero, PriorTransform(modelSiteDrive)), elevation);
            double dataSiteDriveOriginElevation =
                Vector3.Dot(Vector3.Transform(Vector3.Zero, PriorTransform(dataSiteDrive)), elevation);

            var pair = modelSiteDrive + "-" + dataSiteDrive;

            var pairs = new List<SpatialMatch>();
            var lengths = new List<double>();
            foreach (var match in ransacMatches[pair])
            {
                var mf = modelFeatures[match.ModelIndex];
                var df = dataFeatures[match.DataIndex];

                var mxy = mf.Location - modelOrigin;
                var dxy = df.Location - dataOrigin;

                var me = modelDEM[0, (int)mf.Location.Y, (int)mf.Location.X] + modelSiteDriveOriginElevation;
                var de = dataDEM[0, (int)df.Location.Y, (int)df.Location.X] + dataSiteDriveOriginElevation;

                var mp = right * mxy.X * BEVMetersPerPixel + down * mxy.Y * BEVMetersPerPixel + elevation * me;
                var dp = right * dxy.X * BEVMetersPerPixel + down * dxy.Y * BEVMetersPerPixel + elevation * de;

                lengths.Add(Vector3.Distance(mp, dp));
                pairs.Add(new SpatialMatch(mp, dp));
            }

            //the XY components of the matches should already be pretty robust due to the ransac
            //but now that they have Z components those can be dirty
            int n = lengths.Count();
            if (n > 1)
            {
                lengths.Sort();
                double median = lengths[n/2];
                for (int i = 0; i < n; i++)
                {
                    lengths[i] = Math.Abs(lengths[i] - median);
                }
                lengths.Sort();
                var mad = lengths[n/2]; //median absolute deviation
                
                double threshold = options.SpatialOutlierMADs * mad;
                pairs = pairs
                    .Where(pr => Math.Abs(Vector3.Distance(pr.ModelPoint, pr.DataPoint) - median) < threshold)
                    .ToList();
                int nn = pairs.Count();
                if (nn < n)
                {
                    pipeline.LogInfo("{0} outlier spatial matches for {1}, median {2:F3}, threshold {3:F3} ({4} MAD)",
                                     n - nn, pair, median, threshold, options.SpatialOutlierMADs);
                }
                n = nn;
            }
                
            spatialMatches[pair] = pairs.ToArray();

            return n;
        }

        /// <summary>
        /// compute siteDrivePairs = (modelSiteDrive, dataSiteDrive), (modelSiteDrive, dataSiteDrive), ...
        /// </summary>
        private void ComputePairs()
        {
            var fx = StringHelper.ParseList(options.FixSiteDrives);

            var specials = new Dictionary<string, string>();
            foreach (var priority in (SiteDrivePriority[])Enum.GetValues(typeof(SiteDrivePriority)))
            {
                specials[priority.ToString()] = SortSiteDrives(siteDrives, priority).First().ToString();
            }

            for (int i = 0; i < fx.Length; i++)
            {
                if (specials.ContainsKey(fx[i]))
                {
                    fx[i] = specials[fx[i]];
                }
            }

            if (fx.Any(sd => !SiteDrive.IsSiteDriveString(sd)))
            {
                throw new ArgumentException("error parsing --fixedsitedrives: " + string.Join(",", fx));
            }

            fixedSiteDrives.UnionWith(fx.Select(sd => new SiteDrive(sd)));

            pipeline.LogInfo("fixed site drives: {0}", string.Join(", ", fixedSiteDrives.Select(sd => sd.ToString())));

            siteDrives = SortSiteDrives(siteDrives, options.SiteDrivePriority).ToArray();

            pipeline.LogInfo("site drives ordered by {0}: {1}",
                             options.SiteDrivePriority, string.Join(", ", siteDrives));

            for (int i = 0; i < siteDrives.Length; i++)
            {
                for (int j = i + 1; j < siteDrives.Length; j++)
                {
                    var model = siteDrives[i];
                    var data = siteDrives[j];
                    if (fixedSiteDrives.Contains(data))
                    {
                        if (fixedSiteDrives.Contains(model))
                        {
                            continue;
                        }
                        var tmp = model;
                        model = data;
                        data = tmp;
                    }
                    siteDrivePairs.Add(new Tuple<SiteDrive, SiteDrive>(model, data));
                }
            }

            pipeline.LogInfo("{0} site drive pairs", siteDrivePairs.Count);
        }

        /// <summary>
        /// populates matches, ransacMatches, and spatialMatches from database or siteDrivePairs and features
        /// </summary>
        private int LoadOrMatchPairs()
        {
            if (options.RedoMatches || !LoadMatches())
            {
                MatchPairs();
                if (!options.NoSave)
                {
                    SaveMatches();
                }
            }

            int ng = 0;
            foreach (var entry in spatialMatches)
            {
                var name = entry.Key;
                var num = entry.Value.Length;
                if (num > 0)
                {
                    pipeline.LogInfo("{0}: {1} matches", name, num);
                }
                if (num >= options.MinRansacMatches)
                {
                    ng++;
                }
            }
            pipeline.LogInfo("{0} site drive pairs with at least {1} matches", ng, options.MinRansacMatches);

            if (options.WriteDebug)
            {
                double startSec = UTCTime.Now();
                int np = 0, nc = 0;
                CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                        
                        Interlocked.Increment(ref np);
                        if (!options.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} birds eye match images/meshes in parallel, completed {1}/{2}",
                                             np, nc, siteDrivePairs.Count);
                        }

                        var model = pair.Item1;
                        var data = pair.Item2;
                        var pairName = model + "-" + data;

                        if (options.WriteRansacDebug && matches[pairName].Length > 0)
                        {
                            SaveImage(FeatureMatch
                                      .DrawMatches(bevs[model], bevs[data], features[model], features[data],
                                                   matches[pairName]
                                                   .Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex))
                                                   .ToArray(),
                                                   model.ToString(), data.ToString(), stretch: false),
                                      pairName + "_BEV_Pre_RANSAC_Matches");
                        }

                        if (ransacMatches[pairName].Length > 0)
                        {
                            SaveImage(FeatureMatch
                                      .DrawMatches(bevs[model], bevs[data], features[model], features[data],
                                                   ransacMatches[pairName]
                                                   .Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex))
                                                   .ToArray(),
                                                   model.ToString(), data.ToString(), stretch: false),
                                      pairName + "_BEV_Matches");
                        }

                        if (spatialMatches[pairName].Length > 0)
                        {
                            SaveMesh(FeatureMatch
                                     .MakeMatchMesh(spatialMatches[pairName].Select(p => p.ModelPoint).ToArray(),
                                                    spatialMatches[pairName].Select(p => p.DataPoint).ToArray()),
                                     pairName + "_BEV_Matches");
                        }

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    });
                pipeline.LogInfo("saved {0} birds eye view match image/meshes ({1:F3}s)", siteDrivePairs.Count,
                                 UTCTime.Now() - startSec);
            }

            var good = new HashSet<SiteDrive>();
            foreach (var pair in siteDrivePairs)
            {
                var model = pair.Item1;
                var data = pair.Item2;
                var pairName = model + "-" + data;
                if (spatialMatches.ContainsKey(pairName) && spatialMatches[pairName].Length >= options.MinRansacMatches)
                {
                    good.Add(model);
                    good.Add(data);
                }
            }

            return good.Count;
        }

        /// <summary>
        /// compute matches, ransacMatches, and spatialMatches from siteDrivePairs and features  
        /// </summary>
        private void MatchPairs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("matching features in birds eye views for {0} site drive pairs...", siteDrivePairs.Count);

            var histogram = new Histogram(10, "pairs", "matches");
            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                    
                    Interlocked.Increment(ref np);
                    
                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("matching {0} sitedrive pairs in parallel, completed {1}/{2}",
                                         np, nc, siteDrivePairs.Count);
                    }

                    var model = pair.Item1;
                    var data = pair.Item2;
                    var pairName = model + "-" + data;

                    //features -> matches
                    int nm = matches.ContainsKey(pairName) ? matches[pairName].Length : MatchFeatures(model, data);

                    if (nm > options.MinRansacMatches)
                    {
                        //matches -> ransacMatches
                        nm = ransacMatches.ContainsKey(pairName) ?
                            ransacMatches[pairName].Length : RansacMatches(model, data);

                        if (nm > 0)
                        {
                            //ransacMatches -> spatialMatches
                            nm = spatialMatches.ContainsKey(pairName) ?
                                spatialMatches[pairName].Length : SpatializeMatches(model, data);
                        }
                        else
                        {
                            spatialMatches[pairName] = new SpatialMatch[] {};
                        }
                    }
                    else
                    {
                        ransacMatches[pairName] = new FeatureMatch[] {};
                        spatialMatches[pairName] = new SpatialMatch[] {};
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            if (options.Verbose)
            {
                histogram.Dump(pipeline);
            }

            pipeline.LogInfo("matched features in birds eye views for {0} site drive pairs ({1:F3}s)",
                             siteDrivePairs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate matches, ransacMatches, and spatialMatches from database
        /// returns true iff all were loaded successfully
        /// </summary>
        private bool LoadMatches()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                    var model = pair.Item1;
                    var data = pair.Item2;
                    var pairName = model + "-" + data;
                    var fm = FeatureMatches.Find(pipeline, project.Name, pairName);
                    if (fm != null)
                    {
                        matches[pairName] = pipeline
                            .GetDataProduct<FeatureMatchesDataProduct>(project, fm.MatchesGuid, noCache: true)
                            .Matches;
                        var rm = FeatureMatches.Find(pipeline, project.Name, pairName + "_RANSAC");
                        if (rm != null)
                        {
                            ransacMatches[pairName] = pipeline
                                .GetDataProduct<FeatureMatchesDataProduct>(project, rm.MatchesGuid, noCache: true)
                                .Matches;
                            var sm = SpatialMatches.Find(pipeline, project.Name, pairName);
                            if (sm != null)
                            {
                                spatialMatches[pairName] = pipeline
                                    .GetDataProduct<SpatialMatchesDataProduct>(project, sm.MatchesGuid, noCache: true)
                                    .Matches;
                            }
                        }
                    } 
                });
            pipeline.LogInfo("loaded {0} site drive feature matches ({1:F3}s)", spatialMatches.Count,
                             UTCTime.Now() - startSec);
            return spatialMatches.Count == siteDrivePairs.Count;
        }

        /// <summary>
        /// save matches, ransacMatches, and spatialMatches to database
        /// </summary>
        private void SaveMatches()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                    var model = pair.Item1.ToString();
                    var data = pair.Item2.ToString();
                    var pairName = model + "-" + data;
                    FeatureMatches.Create(pipeline, project, pairName, model, data, matches[pairName]);
                    FeatureMatches.Create(pipeline, project, pairName + "_RANSAC", model, data, ransacMatches[pairName]);
                    SpatialMatches.Create(pipeline, project, pairName, model, data, spatialMatches[pairName]);
                });
            pipeline.LogInfo("saved {0} site drive feature matches ({1:F3}s)", matches.Count, UTCTime.Now() - startSec);
        }

        private class Node
        {
            public SiteDrive siteDrive;
            public Node treeParent;
            public HashSet<Node> parents = new HashSet<Node>();
            public HashSet<Node> children = new HashSet<Node>();
            public bool visited;
            public Matrix transform; //to parent
            public Matrix? worldTransform; //to world

            public Node(SiteDrive siteDrive)
            {
                this.siteDrive = siteDrive;
            }
        }
        private List<Node> nodes = new List<Node>();
        private Dictionary<SiteDrive, Node> siteDriveToNode = new Dictionary<SiteDrive, Node>();

        /// <summary>
        /// build graph of sitedrive nodes  
        /// for each pair of sitedrives for which we have a sufficient spatial match
        /// the "data" sitedrive is a child of the "model" sitedrive
        /// at this stage the graph is a DAG because a node can be a child of more than one parent
        /// the graph is also possibly disconnected (i.e. there can be more than one node with no parent)
        /// </summary>
        private void MakeGraph()
        {
            foreach (var sd in siteDrives)
            {
                var node = new Node(sd);
                nodes.Add(node);
                siteDriveToNode[sd] = node;
            }

            foreach (var pair in siteDrivePairs)
            {
                var model =  pair.Item1;
                var data =  pair.Item2;
                var key = model + "-" + data;
                if (spatialMatches.ContainsKey(key) && spatialMatches[key].Length >= options.MinRansacMatches)
                {
                    var parent = siteDriveToNode[model];
                    var child = siteDriveToNode[data];
                    parent.children.Add(child);
                    child.parents.Add(parent);
                }
            }
        }

        /// <summary>
        /// write out sitedrive -> root adjusted transforms
        /// </summary>
        private void SaveTransforms(IEnumerable<Node> aligned, TransformSource transformSource)
        {
            var sds = aligned.Select(node => node.siteDrive).ToArray();
            var xforms = aligned.Select(node => node.worldTransform.Value).ToArray();

            SaveTransforms(sds, xforms, transformSource);

            if (options.WriteDebug)
            {
                string sfx = transformSource == TransformSource.LandformBEV ? "_Adj"
                    : transformSource == TransformSource.LandformBEVCalf ? "_Calf" : null;
                if (sfx != null)
                {
                    var meshToRoot =
                        dbgMeshTransform.HasValue ? Matrix.Invert(dbgMeshTransform.Value) : Matrix.Identity;
                    CoreLimitedParallel.For(0, sds.Length, i =>
                    {
                        string bn = sds[i] + DEBUG_BEV_MESH_SUFFIX;
                        string imgFile = bn + imageExt;
                        if (!File.Exists(Path.Combine(localOutputPath, imgFile)))
                        {
                            imgFile = null;
                        }
                        string meshPath = Path.Combine(localOutputPath, bn + meshExt);
                        if (File.Exists(meshPath))
                        {
                            var mesh = Mesh.Load(meshPath);
                            string meshFile = bn + sfx;
                            var rootToSD = Matrix.Invert(PriorTransform(sds[i]));
                            var sdToRoot = xforms[i];
                            SaveMesh(mesh.Transformed(meshToRoot * rootToSD * sdToRoot), meshFile, imgFile);
                        }
                    });
                }
            }
        }

        private void SaveCalves(IEnumerable<Node> aligned)
        {
            if (options.CalfMode == CalfMode.None)
            {
                return;
            }

            var calfSDs = new HashSet<SiteDrive>(siteDrives);
            foreach (var node in aligned)
            {
                calfSDs.Remove(node.siteDrive);
                calfSDs.Remove(node.treeParent.siteDrive);
            }

            foreach (var sd in fixedSiteDrives)
            {
                calfSDs.Remove(sd);
            }

            if (calfSDs.Count == 0)
            {
                return;
            }

            var calves = calfSDs.Select(name => siteDriveToNode[name]);

            switch (options.CalfMode)
            {
                case CalfMode.Centroid:
                    {
                        var centroid = new Dictionary<SiteDrive, Vector2>();
                        foreach (var sd in siteDrives)
                        {
                            var c = new Vector2(bevs[sd].Width, bevs[sd].Height) * 0.5;
                            centroid[sd] = c - rootOriginPixel[sd];
                        }
                        foreach (var calf in calves)
                        {
                            double closestDistSq = double.PositiveInfinity;
                            Node closestParent = null;
                            foreach (var node in aligned)
                            {
                                var d2 = Vector2.DistanceSquared(centroid[calf.siteDrive], centroid[node.siteDrive]);
                                if (d2 < closestDistSq)
                                {
                                    closestDistSq = d2;
                                    closestParent = node;
                                }
                            }
                            calf.treeParent = closestParent;
                        }
                        break;
                    }

                case CalfMode.Temporal:
                    {
                        foreach (var calf in calves)
                        {
                            int closestDist = int.MaxValue;
                            Node closestParent = null;
                            foreach (var node in aligned)
                            {
                                var d = Math.Abs((int)(calf.siteDrive) - (int)(node.siteDrive));
                                if (d < closestDist)
                                {
                                    closestDist = d;
                                    closestParent = node;
                                }
                            }
                            calf.treeParent = closestParent;
                        }
                        break;
                    }
            }

            foreach (var calf in calves)
            {
                if (calf.treeParent != null)
                {
                    var calfToWorld = BestTransform(calf.siteDrive);
                    var parentToWorld = BestTransform(calf.treeParent.siteDrive);
                    //row matrix transforms compose left to right
                    var calfToParent = calfToWorld * Matrix.Invert(parentToWorld);
                    calf.worldTransform = calfToParent * calf.treeParent.worldTransform.Value;
                }
                else
                {
                    calf.worldTransform = null;
                }
            }

            pipeline.LogInfo("calf mode: {0}", options.CalfMode);
            var calvesFor = new Dictionary<SiteDrive, List<SiteDrive>>();
            foreach (var calf in calves)
            {
                if (calf.treeParent != null)
                {
                    if (!calvesFor.ContainsKey(calf.treeParent.siteDrive))
                    {
                        calvesFor[calf.treeParent.siteDrive] = new List<SiteDrive>();
                    }
                    calvesFor[calf.treeParent.siteDrive].Add(calf.siteDrive);
                }
            }

            pipeline.LogInfo("{0} calves: {1}", calves.Count(), String.Join(", ", calves.Select(n => n.siteDrive)));

            foreach (var parent in calvesFor.Keys)
            {
                pipeline.LogInfo("{0} calves for site drive {1}: {2}",
                                 calvesFor[parent].Count, parent, String.Join(", ", calvesFor[parent]));
            }

            if (!options.NoSave)
            {
                SaveTransforms(calves.Where(calf => calf.worldTransform.HasValue), TransformSource.LandformBEVCalf);
            }
        }
                
        /// <summary>
        /// spatialMatches -> LandformBEV aligned FrameTransforms
        /// </summary>
        private int Align()
        {
            switch (options.AlignmentMode)
            {
                case AlignmentMode.Simultaneous: return SimultaneousAlign();
                case AlignmentMode.Pairwise: return PairwiseAlign();
                default: return 0;
            }
        }

        /// <summary>
        /// simultaneous align all sitedrives that have a sufficent number of spatialized ransac feature matches
        /// then compute the adjusted sitedrive -> root transforms and write them back to the database
        /// using TransformSource = LandformBEV
        /// </summary>
        private int SimultaneousAlign()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("simultaneous aligning...");

            MakeGraph();

            foreach (var node in nodes)
            {
                node.worldTransform = BestTransform(node.siteDrive);
            }

            var nodesToAlign = new List<Node>();
            foreach (var node in nodes)
            {
                if ((node.parents.Count > 0 || node.children.Count > 0) && !fixedSiteDrives.Contains(node.siteDrive))
                {
                    nodesToAlign.Add(node);
                }
            }

            //TODO fix at least one node in each connected component
            
            //TODO
            throw new NotImplementedException("simultaneous align not implemented yet");

            //if (!options.NoSave)
            //{
            //    SaveTransforms(nodesToAlign, TransformSource.LandformBEV);
            //    SaveTransforms(TODO, TransformSource.LandformBEVRoot);
            //}

            //SaveCalves(nodesToAlign);

            //pipeline.LogInfo("simultaneous aligned {0} nodes ({1:F3}s)", nodesToAlign.Count, UTCTime.Now() - startSec);

            //return nodesToAlign.Length;
        }

        /// <summary>
        /// pairwise align all sitedrives that have a sufficent number of spatialized ransac feature matches
        /// then compute the adjusted sitedrive -> root transforms and write them back to the database
        /// using TransformSource = LandformBEV
        /// </summary>
        private int PairwiseAlign()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("pairwise aligning...");

            MakeGraph();

            //treat the graph as undirectred and BFS it to make spanning trees
            foreach (var node in nodes)
            {
                node.visited = false;
            }
            void bfs(Node node)
            {
                var queue = new Queue<Node>();
                queue.Enqueue(node);
                while (queue.Count > 0)
                {
                    node = queue.Dequeue();
                    node.visited = true;
                    foreach (var nbr in node.children.Concat(node.parents))
                    {
                        if (nbr != null && !nbr.visited && !fixedSiteDrives.Contains(nbr.siteDrive))
                        {
                            nbr.treeParent = node;
                            queue.Enqueue(nbr);
                        }
                    }
                }
            }
            foreach (var node in nodes.Where(n => fixedSiteDrives.Contains(n.siteDrive)))
            {
                bfs(node);
            }
            foreach (var node in nodes.Where(n => n.parents.Count == 0 && !n.visited))
            {
                bfs(node);
            }

            var nodesToAlign = nodes.Where(n => n.treeParent != null).ToList();
            pipeline.LogInfo("pairwise aligning {0} site drives", nodesToAlign.Count);
            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(nodesToAlign, node => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("pairwise aligning {0} site drives in parallel, completed {1}/{2}",
                                         np, nc, nodesToAlign.Count);
                    }

                    var parent = node.treeParent.siteDrive;
                    var child = node.siteDrive;
                    
                    var parentToRoot = BestTransform(parent);
                    var childToRoot = BestTransform(child);
                    var rootToParent = Matrix.Invert(parentToRoot);
                    
                    //the spatial matches are in root frame, transform them to parent prior frame
                    Vector3[] parentPts = null, childPts = null;
                    SpatialMatch[] sm = null;
                    var pair = parent + "-" + child;
                    if (spatialMatches.ContainsKey(pair))
                    {
                        sm = spatialMatches[pair];
                        parentPts = sm.Select(m => Vector3.Transform(m.ModelPoint, rootToParent)).ToArray();
                        childPts = sm.Select(m => Vector3.Transform(m.DataPoint, rootToParent)).ToArray();
                    }
                    else
                    {
                        pair = child + "-" + parent;
                        sm = spatialMatches[pair];
                        parentPts = sm.Select(m => Vector3.Transform(m.DataPoint, rootToParent)).ToArray();
                        childPts = sm.Select(m => Vector3.Transform(m.ModelPoint, rootToParent)).ToArray();
                    }
                    
                    double priorResidual = 0;
                    for (int i = 0; i < parentPts.Length; i++)
                    {
                        priorResidual += Vector3.DistanceSquared(parentPts[i], childPts[i]);
                    }
                    priorResidual = Math.Sqrt(priorResidual / parentPts.Length);
                    
                    //compute transform adj that best aligns child points to parent points
                    var residual = Procrustes.CalculateRigid(childPts, parentPts, out Matrix adj);
                    
                    pipeline.LogInfo("aligned {0} to {1} ({2} matches), residual {3}->{4}m",
                                     child, parent, sm.Length, priorResidual, residual);

                    //row matrix transforms compose left to right
                    var childToParent = childToRoot * rootToParent;
                    
                    //adjusted transform taking points in child frame to points in parent frame
                    node.transform = childToParent * adj;

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            //compute a world transform for each node (i.e. sitedrive to root transform)
            foreach (var node in nodes.Where(n => n.treeParent == null))
            {
                node.worldTransform = BestTransform(node.siteDrive);
            }
            foreach (var node in nodesToAlign)
            {
                var stack = new Stack<Node>();
                for (var n = node; n.worldTransform == null; n = n.treeParent)
                {
                    stack.Push(n);
                }
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    //row matrix transforms compose left to right
                    n.worldTransform = n.transform * n.treeParent.worldTransform.Value;
                }
            }

            if (!options.NoSave)
            {
                SaveTransforms(nodesToAlign, TransformSource.LandformBEV);
            }

            var roots = nodesToAlign.Select(n => n.treeParent).Where(n => n.treeParent == null).Distinct();
            pipeline.LogInfo("{0} roots: {1}", roots.Count(), String.Join(", ", roots.Select(node => node.siteDrive)));

            if (!options.NoSave)
            {
                SaveTransforms(roots, TransformSource.LandformBEVRoot);
            }

            SaveCalves(nodesToAlign);

            pipeline.LogInfo("pairwise aligned {0} nodes ({1:F3}s)", nodesToAlign.Count, UTCTime.Now() - startSec);

            return nodesToAlign.Count;
        }
    }
}


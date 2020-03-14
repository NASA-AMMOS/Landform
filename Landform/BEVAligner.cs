using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using OPS.Util;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public enum SiteDrivePriority { NewestFirst, OldestFirst, BiggestFirst, SmallestFirst };
    
    public enum AlignmentMode { PairwiseMinimal, PairwiseMaximal, Simultaneous, None };

    public enum CalfMode { None, Centroid, Temporal };

    [Verb("bev-align", HelpText = "birds eye view alignment")]
    public class BEVAlignerOptions : BEVCommandOptions
    {
        [Option(HelpText = "Option disabled for this command - always loads priors", Default = true)]
        public override bool UsePriors { get; set; }

        [Option(HelpText = "Option disabled for this command - always loads priors", Default = false)]
        public override bool OnlyAligned { get; set; }

        [Option(HelpText = "Option disabled for this command - always loads priors", Default = null)]
        public override string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Don't adjust specified site drives (or \"newest\", \"oldest\", \"largest\", \"smallest\"), comma separated", Default = null)]
        public string FixSiteDrives { get; set; }

        [Option(HelpText = "Alignment algorithm: PairwiseMinimal, PairwiseMaximal, Simultaneous, None (match only)", Default = AlignmentMode.PairwiseMinimal)]
        public AlignmentMode AlignmentMode { get; set; }

        [Option(HelpText = "Algorithm to bring un-aligned \"calf\" site drives along for the ride: None, Centroid (match to aligned site drive with closest horizontal centroid), Temporal (match to closest aligned site drive by acquisition time)", Default = CalfMode.Centroid)]
        public CalfMode CalfMode { get; set; }

        [Option(HelpText = "In pairwise alignment modes lower priority site drives will be aligned to higher priority ones (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst)", Default = SiteDrivePriority.OldestFirst)]
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
    }

    public class BEVAligner : BEVCommand
    {
        private const string OUT_DIR = "alignment/AdjustProducts";

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

        //modelSiteDrive-dataSiteDrive => (modelPoint, dataPoint), (modelPoint, dataPoint), ...
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
            }
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                if (siteDrives.Length < 2 && !(options.OnlyRenderBEVs || options.OnlyDetectFeatures))
                {
                    pipeline.LogWarn("at least two site drives required");
                    return 0;
                }

                pipeline.LogInfo("computing birds eye view alignment for {0} site drives", siteDrives.Length);

                RunPhase("load or render birds eye views", LoadOrRenderBEVs); //observations -> bevs, dems

                if (options.OnlyRenderBEVs)
                {
                    pipeline.LogInfo("rendered birds eye views for {0} site drives ({1:F3}s)",
                                     bevs.Count, 0.001 * stopwatch.ElapsedMilliseconds);
                    return 0;
                }

                RunPhase("load or detect features", LoadOrDetectFeatures); //bevs -> features

                if (options.OnlyDetectFeatures)
                {
                    pipeline.LogInfo("rendered birds eye views for {0} site drives and detected features ({1:F3}s)",
                                     bevs.Count, 0.001 * stopwatch.ElapsedMilliseconds);
                    return 0;
                }

                //some BEVs may have failed to render
                if (siteDrives.Length < 2)
                {
                    pipeline.LogWarn("at least two site drives required");
                    return 0;
                }

                RunPhase("compute site drive pairs", ComputePairs); //siteDrives -> siteDrivePairs

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

        private bool ParseArgumentsAndLoadCaches()
        {
            if (!options.UsePriors)
            {
                throw new Exception("--usepriors=false not supported for this command");
            } 

            if (options.OnlyAligned)
            {
                throw new Exception("--onlyaligned not supported for this command");
            } 

            if (!string.IsNullOrEmpty(options.AdjustedTransformSources))
            {
                throw new Exception("--adjustedtransformsources not supported for this command");
            } 

            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            return true;
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
                double crossRadius = 0.05 * PixelsPerMeter, circleRadius = 0.5 * PixelsPerMeter;
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
                        var img = FeatureDetecting.DrawFeaturesEmgu(bev, mask, feat, siteDrive.ToString(),
                                                                    stretch: false);
                        foreach (var otherSiteDrive in siteDrives)
                        {
                            var pixel = PointToPixel(Vector3.Zero, otherSiteDrive, siteDrive);
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
            FeatureDetector detector = new FeatureDetector(pipeline, masker, detectorOpts);

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
                        features[siteDrive] =
                            pipeline.GetDataProduct<FeaturesDataProduct>(project, rec.FeaturesGuid).Features;
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
                var dataOriginInModel = PointToPixel(Vector3.Zero, data, model);
                
                //distance in pixels of model feature to origin of model sitedrive in model BEV
                var modelDistances = modelFeatures.Select(f => Vector2.Distance(f.Location, modelOrigin)).ToArray();

                //NOTE: features for a site drive are already sorted by distance to origin of that site drive
                
                double radius = options.MatchRadius * PixelsPerMeter;
                
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
            var dataOriginInModel = PointToPixel(Vector3.Zero, dataSiteDrive, modelSiteDrive);

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

            double radius = options.RansacMatchRadius * PixelsPerMeter;
            double radiusSquared = radius * radius;

            double minSep = options.MinRansacSeparation * PixelsPerMeter;
            double minSepSquared = minSep * minSep;

            var maxResidual = options.MaxRansacResidual * PixelsPerMeter;

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

            if (options.WriteDebug)
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
                
                writeImage("_0_priors", pt => pt);
                writeImage("_1_rotation", pt => bestTransform.Rotate(pt));
                writeImage("_2_solved", pt => bestTransform.Transform(pt));
            }
                        
            ransacMatches[pair] = bestMatches.Select(m => matchArray[m]).ToArray();

            nm = bestMatches.Count;
            var msg =
                nm > 0 ? string.Format(", best transform ({0:F3}m, {1:F3}m, {2:F3}deg), residual {3:F3}m, {4} matches",
                                       bestTransform.Translation.X * MetersPerPixel,
                                       bestTransform.Translation.Y * MetersPerPixel,
                                       MathHelper.ToDegrees(bestTransform.Rotation),
                                       bestResidual * MetersPerPixel, nm)
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

            var pair = modelSiteDrive + "-" + dataSiteDrive;

            var pairs = new List<SpatialMatch>();
            var lengths = new List<double>();
            foreach (var match in ransacMatches[pair])
            {
                var mf = modelFeatures[match.ModelIndex];
                var df = dataFeatures[match.DataIndex];

                var mxy = (mf.Location - modelOrigin) * MetersPerPixel;
                var mz = modelDEM[0, (int)mf.Location.Y, (int)mf.Location.X];

                var dxy = (df.Location - dataOrigin) * MetersPerPixel;
                var dz = dataDEM[0, (int)df.Location.Y, (int)df.Location.X];

                var mp = new Vector3(mxy.X, mxy.Y, mz);
                var dp = new Vector3(dxy.X, dxy.Y, dz);
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

            var specials = new Dictionary<string, SiteDrive>();
            specials["newest"] = siteDrives.OrderByDescending(sd => sd).FirstOrDefault();
            specials["oldest"] = siteDrives.OrderBy(sd => sd).FirstOrDefault();
            specials["largest"] = siteDrives.OrderByDescending(sd => bevs[sd].Area).FirstOrDefault();
            specials["smallest"] = siteDrives.OrderBy(sd => bevs[sd].Area).FirstOrDefault();

            for (int i = 0; i < fx.Length; i++)
            {
                var sd = fx[i];
                if (specials.ContainsKey(sd))
                {
                    fx[i] = specials[sd].ToString();
                }
            }

            fixedSiteDrives.UnionWith(fx.Select(sd => new SiteDrive(sd)));

            switch (options.SiteDrivePriority)
            {
                case SiteDrivePriority.NewestFirst:
                {
                    siteDrives = siteDrives.OrderByDescending(sd => sd).ToArray();
                    break;
                }
                case SiteDrivePriority.OldestFirst:
                {
                    siteDrives = siteDrives.OrderBy(sd => sd).ToArray();
                    break;
                }
                case SiteDrivePriority.BiggestFirst:
                {
                    siteDrives = siteDrives.OrderByDescending(sd => bevs[sd].Area).ToArray();
                    break;
                }
                case SiteDrivePriority.SmallestFirst:
                {
                    siteDrives = siteDrives.OrderBy(sd => bevs[sd].Area).ToArray();
                    break;
                }
            }

            pipeline.LogInfo("site drives ordered by {0}: {1}",
                             options.SiteDrivePriority, string.Join(", ", siteDrives));

            for (int i = 0; i < siteDrives.Length; i++)
            {
                for (int j = i + 1; j < siteDrives.Length; j++)
                {
                    var model = siteDrives[i];
                    var data = siteDrives[j];
                    if (fixedSiteDrives.Contains(data) && !fixedSiteDrives.Contains(model))
                    {
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

                        if (matches[pairName].Length > 0)
                        {
                            SaveImage(ImageMatching
                                      .DrawMatches(bevs[model], bevs[data], features[model], features[data],
                                                   matches[pairName]
                                                   .Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex))
                                                   .ToArray(),
                                                   model.ToString(), data.ToString(), stretch: false),
                                      pairName + "_BEV_Matches");
                        }

                        if (ransacMatches[pairName].Length > 0)
                        {
                            SaveImage(ImageMatching
                                      .DrawMatches(bevs[model], bevs[data], features[model], features[data],
                                                   ransacMatches[pairName]
                                                   .Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex))
                                                   .ToArray(),
                                                   model.ToString(), data.ToString(), stretch: false),
                                      pairName + "_BEV_RANSAC_Matches");
                        }

                        if (spatialMatches[pairName].Length > 0)
                        {
                            SaveMesh(ImageMatching
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
                        matches[pairName] =
                            pipeline.GetDataProduct<FeatureMatchesDataProduct>(project, fm.MatchesGuid).Matches;
                        var rm = FeatureMatches.Find(pipeline, project.Name, pairName + "_RANSAC");
                        if (rm != null)
                        {
                            ransacMatches[pairName] =
                                pipeline.GetDataProduct<FeatureMatchesDataProduct>(project, rm.MatchesGuid).Matches;
                            var sm = SpatialMatches.Find(pipeline, project.Name, pairName);
                            if (sm != null)
                            {
                                spatialMatches[pairName] =
                                    pipeline.GetDataProduct<SpatialMatchesDataProduct>(project, sm.MatchesGuid).Matches;
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
            public Node parent;
            public List<Node> children = new List<Node>();
            public int depth; //length of path along ancestor chain to world
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
                    child.parent = parent; //for now any parent will do
                }
            }
        }

        /// <summary>
        /// write out sitedrive -> root adjusted transforms
        /// </summary>
        private void SaveTransforms(IEnumerable<Node> aligned, TransformSource transformSource)
        {
            var unaligned = new HashSet<SiteDrive>(siteDrives);
            foreach (var node in aligned)
            {
                unaligned.Remove(node.siteDrive);
                var ut = new UncertainRigidTransform(node.worldTransform.Value);
                var frame = frameCache.GetFrame(node.siteDrive.ToString());
                var ft = FrameTransform.FindOrCreate(pipeline, frame, transformSource, ut);
                ft.Transform = ut;
                ft.Save(pipeline);
                bool added = false;
                lock (frame.Transforms)
                {
                    added = frame.Transforms.Add(ft.Source);
                }
                if (added)
                {
                    frame.Save(pipeline);
                }
                pipeline.LogInfo("saved {0} adjusted transform for site drive {1}", transformSource, node.siteDrive);
            }
            foreach (var sd in unaligned)
            {
                var frame = frameCache.GetFrame(sd.ToString());
                bool removed = false;
                lock (frame.Transforms)
                {
                    removed = frame.Transforms.Remove(transformSource);
                }
                if (removed)
                {
                    frame.Save(pipeline);
                }
                //can't use frameCache here because it was loaded with only priors
                //but that's OK because FrameTransform.Find() doesn't scan
                var ft = FrameTransform.Find(pipeline, frame, transformSource);
                if (ft != null)
                {
                    ft.Delete(pipeline);
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
                calfSDs.Remove(node.parent.siteDrive);
            }

            foreach (var sd in fixedSiteDrives)
            {
                calfSDs.Remove(sd);
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
                            calf.parent = closestParent;
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
                            calf.parent = closestParent;
                        }
                        break;
                    }
            }

            foreach (var calf in calves)
            {
                if (calf.parent != null)
                {
                    var calfToWorldPrior = SiteDrivePrior(calf.siteDrive);
                    var parentToWorldPrior = SiteDrivePrior(calf.parent.siteDrive);
                    //row matrix transforms compose left to right
                    var calfToParent = calfToWorldPrior * Matrix.Invert(parentToWorldPrior);
                    calf.worldTransform = calfToParent * calf.parent.worldTransform.Value;
                }
                else
                {
                    calf.worldTransform = null;
                }
            }

            pipeline.LogInfo("birds eye view calf mode: {0}", options.CalfMode);
            var calvesFor = new Dictionary<SiteDrive, List<SiteDrive>>();
            foreach (var calf in calves)
            {
                if (calf.parent != null)
                {
                    if (!calvesFor.ContainsKey(calf.parent.siteDrive))
                    {
                        calvesFor[calf.parent.siteDrive] = new List<SiteDrive>();
                    }
                    calvesFor[calf.parent.siteDrive].Add(calf.siteDrive);
                }
            }

            pipeline.LogInfo("{0} birds eye view calves: {1}",
                             calves.Count(), String.Join(", ", calves.Select(n => n.siteDrive)));

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
                case AlignmentMode.PairwiseMaximal: return PairwiseAlign(maximal: true);
                case AlignmentMode.PairwiseMinimal: return PairwiseAlign(maximal: false);
            }
            return 0;
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
                node.worldTransform = SiteDrivePrior(node.siteDrive);
            }

            var nodesToAlign = new List<Node>();
            foreach (var node in nodes)
            {
                if ((node.parent != null || node.children.Count > 0) && !fixedSiteDrives.Contains(node.siteDrive))
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
        private int PairwiseAlign(bool maximal)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("pairwise aligning...");

            MakeGraph();

            //BFS the graph to set the best parent for each node
            //the best parent is the one to follow to get to a root along a best path
            foreach (var node in nodes)
            {
                node.depth = maximal ? int.MinValue : int.MaxValue;
            }
            foreach (var node in nodes.Where(n => n.parent == null))
            {
                node.depth = 0;
                var queue = new Queue<Node>();
                queue.Enqueue(node);
                while (queue.Count > 0)
                {
                    var parent = queue.Dequeue();
                    var depth = parent.depth + 1;
                    foreach (var child in parent.children)
                    {
                        if ((maximal && child.depth < depth) || (!maximal && child.depth > depth))
                        {
                            child.parent = parent;
                            child.depth = depth;
                            queue.Enqueue(child);
                        }
                    }
                }
            }

            var closures = new HashSet<string>();
            foreach (var node in nodes)
            {
                foreach (var child in node.children)
                {
                    if (child.parent != node)
                    {
                        closures.Add(node.siteDrive + "-" + child.siteDrive);
                    }
                }
            }
            pipeline.LogInfo("{0} birds eye view loop closures: {1}", closures.Count, String.Join(", ", closures));

            //align every node to its a parent
            //a node has a parent iff we found enough ransac matches from that node to a higher-priority sitedrive
            var nodesToAlign = nodes
                .Where(n => n.parent != null)
                .Where(n => !fixedSiteDrives.Contains(n.siteDrive))
                .ToList();
            pipeline.LogInfo("pairwise aligning {0} site drives", nodesToAlign.Count);
            int nc = 0, np = 0;
            var aligned = new HashSet<string>();
            CoreLimitedParallel.ForEach(nodesToAlign, node => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("pairwise aligning {0} site drives in parallel, completed {1}/{2}",
                                         np, nc, nodesToAlign.Count);
                    }

                    var model = node.parent.siteDrive;
                    var data = node.siteDrive;
                    
                    var modelToRootPrior = SiteDrivePrior(model);
                    var dataToRootPrior = SiteDrivePrior(data);
                    var rootToModelPrior = Matrix.Invert(modelToRootPrior);
                    
                    //the spatial matches are in root frame, transform them to model prior frame
                    var pair = model + "-" + data;
                    var sm = spatialMatches[pair];
                    var modelPts = sm.Select(m => Vector3.Transform(m.ModelPoint, rootToModelPrior)).ToArray();
                    var dataPts = sm.Select(m => Vector3.Transform(m.DataPoint, rootToModelPrior)).ToArray();
                    
                    double priorResidual = 0;
                    for (int i = 0; i < modelPts.Length; i++)
                    {
                        priorResidual += Vector3.DistanceSquared(modelPts[i], dataPts[i]);
                    }
                    priorResidual = Math.Sqrt(priorResidual / modelPts.Length);
                    
                    //compute transform adj that best aligns data points to model points
                    var residual = Procrustes.CalculateRigid(dataPts, modelPts, out Matrix adj);
                    
                    pipeline.LogInfo("aligned {0} ({1} matches), residual {2}->{3}m",
                                     pair, sm.Length, priorResidual, residual);

                    aligned.Add(pair);
                    
                    //row matrix transforms compose left to right
                    var dataToModelPrior = dataToRootPrior * rootToModelPrior;
                    
                    //adjusted transform taking points in data frame to points in model frame
                    node.transform = dataToModelPrior * adj;

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            //compute a world transform for each node (i.e. sitedrive to root transform)
            //for a node with no parent this is just the prior
            //otherwise it's the concatenation of adjusted transforms along ancestor chain from node to world
            foreach (var node in nodes.Where(n => n.parent == null))
            {
                node.worldTransform = SiteDrivePrior(node.siteDrive);
            }
            foreach (var node in nodesToAlign)
            {
                var stack = new Stack<Node>();
                for (var n = node; n.worldTransform == null; n = n.parent)
                {
                    stack.Push(node);
                }
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    //row matrix transforms compose left to right
                    n.worldTransform = n.transform * n.parent.worldTransform.Value;
                }
            }

            if (!options.NoSave)
            {
                SaveTransforms(nodesToAlign, TransformSource.LandformBEV);
            }

            var roots = nodesToAlign.Select(n => n.parent).Where(n => n.parent == null).Distinct();
            pipeline.LogInfo("{0} birds eye view roots: {1}",
                             roots.Count(), String.Join(", ", roots.Select(node => node.siteDrive)));

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


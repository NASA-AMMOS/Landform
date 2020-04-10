using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    [Verb("detect-features", HelpText = "create image features")]
    public class DetectFeaturesOptions : LandformCommandOptions
    {
        [Option(HelpText = "Detector type", Default = FeatureDetector.DEF_DETECTOR_TYPE)]
        public FeatureDetector.DetectorType DetectorType { get; set; }

        [Option(HelpText = "Minimum feature size to keep", Default = FeatureDetector.DEF_MIN_FEATURE_SIZE)]
        public double MinFeatureSize { get; set; }

        [Option(HelpText = "Minimum feature response keep, -1 for all", Default = FeatureDetector.DEF_MIN_RESPONSE)]
        public double MinResponse { get; set; }

        [Option(HelpText = "Maximum feature response keep, -1 for all", Default = FeatureDetector.DEF_MAX_RESPONSE)]
        public double MaxResponse { get; set; }

        [Option(HelpText = "Maximum number of features per image", Default = FeatureDetector.DEF_MAX_FEATURES)]
        public int MaxFeaturesPerImage { get; set; }

        [Option(HelpText = "Image decimation", Default = FeatureDetector.DEF_DECIMATION)]
        public int Decimation { get; set; }

        [Option(HelpText = "SIFT octave layers", Default = FeatureDetector.DEF_SIFT_OCTAVES)]
        public int SIFTOctaves { get; set; }

        [Option(HelpText = "Minimum (finest) SIFT octave layer of features to keep, -1 for all", Default = FeatureDetector.DEF_MIN_SIFT_OCTAVE)]
        public int MinSIFTOctave { get; set; }

        [Option(HelpText = "Maximum (coarsest) SIFT octave layer of features to keep, -1 for all", Default = FeatureDetector.DEF_MAX_SIFT_OCTAVE)]
        public int MaxSIFTOctave { get; set; }

        [Option(HelpText = "Recreate features that already exist", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Don't try to add range data to features", Default = false)]
        public bool NoRange { get; set; }

        [Option(HelpText = "Only keep features with range", Default = false)]
        public bool RequireRange { get; set; }

        [Option(HelpText = "Write feature images for debugging", Default = false)]
        public bool WriteFeatureImages { get; set; }

        [Option(HelpText = "Include existing products in histograms", Default = false)]
        public bool TallyExisting { get; set; }

        [Option(HelpText = "Comma separated list of observations to process, omit for all", Default = null)]
        public string OnlyForObservations { get; set; }
    }

    public class DetectFeatures : LandformCommand
    {
        private DetectFeaturesOptions options;

        public DetectFeatures(DetectFeaturesOptions options) : base(options)
        {
            this.options = options;

            if (options.Redo)
            {
                options.RedoFeatures = true;
            }
        }

        public int Run()
        {
            var project = Project.Find(pipeline, options.ProjectName);

            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            localOutputPath = pipeline.GetLocalFolder(options.OutputFolder, "alignment/FeatureProducts", project.Name);

            if (options.WriteFeatureImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} feature images to {1}", imageExt, localOutputPath);
            }

            var allowed = (options.OnlyForObservations ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.Preload(loadTransforms: false);

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload(obs => obs.UseForAlignment &&
                                     (!obs.IsOrbital && allowed.Length == 0 || allowed.Any(name => name == obs.Name)));

            var obsForFrame = new Dictionary<string, List<RoverObservation>>();
            foreach (var obs in observationCache.GetAllObservations())
            {
                if (obs is RoverObservation)
                {
                    if (!obsForFrame.ContainsKey(obs.FrameName))
                    {
                        obsForFrame[obs.FrameName] = new List<RoverObservation>();
                    }
                    obsForFrame[obs.FrameName].Add(obs as RoverObservation);
                }
            }

            int no = obsForFrame.Values.Count(grp => grp.Any(obs => obs.ObservationType == RoverProductType.Image));
            pipeline.LogInfo("computing {0} features for {1} reconstruction images", options.DetectorType, no);

            var detectorOpts = new FeatureDetector.Options()
                {
                    DetectorType = options.DetectorType,
                    MinFeatureSize = options.MinFeatureSize,
                    MinResponse = options.MinResponse,
                    MaxResponse = options.MaxResponse,
                    MaxFeatures = options.MaxFeaturesPerImage,
                    Decimation = options.Decimation,
                    SIFTOctaves = options.SIFTOctaves,
                    MinSIFTOctave = options.MinSIFTOctave,
                    MaxSIFTOctave = options.MaxSIFTOctave,
                    FeaturesPerImageBucketSize = 1000,
                    FeaturesPerSizeBucketSize = 5,
                    FeaturesPerResponseBucketSize = 0.002,
                    FeaturesPerOctaveBucketSize = 1,
                    FeaturesPerLayerBucketSize = 1,
                    FeaturesPerScaleBucketSize = 0.1
                };
            FeatureDetector detector = new FeatureDetector(pipeline, masker, detectorOpts);

            var comparator = mission.GetRoverObservationComparator();

            double startSec = UTCTime.Now();
            int nc = 0, ne = 0, nf = 0, np = 0, wr = 0, tf = 0, trf = 0;
            CoreLimitedParallel.ForEach(obsForFrame.Values, obsGroup => { 

                    var observations = comparator.SortRoverObservations(obsGroup).ToList();
                    
                    var imageObs = observations.Find(obs => obs.ObservationType == RoverProductType.Image);
                    if (imageObs != null)
                    {
                        var maskObs = observations.Find(obs => obs.ObservationType == RoverProductType.RoverMask &&
                                                        obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                        var maskUrl = maskObs != null ? maskObs.Url : null;

                        if (imageObs.FeaturesGuid != null && imageObs.FeaturesGuid != Guid.Empty)
                        {
                            Interlocked.Increment(ref ne);
                            if (!options.RedoFeatures)
                            {
                                pipeline.LogVerbose("not recomputing features for observation {0}", imageObs.Name);
                                if (options.WriteFeatureImages || options.TallyExisting)
                                {
                                    var product =
                                        pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath,
                                                                                  imageObs.FeaturesGuid, project.Name);
                                    if (options.WriteFeatureImages)
                                    {
                                        WriteFeatureImage(product, maskUrl, imageObs);
                                    }
                                    if (options.TallyExisting)
                                    {
                                        detector.Tally(product.Features);
                                    }
                                }
                                return;
                            }
                            else
                            {
                                pipeline.LogVerbose("recomputing features for observation {0}", imageObs.Name);
                            }
                        }
                        else
                        {
                            pipeline.LogVerbose("computing features for observation {0}", imageObs.Name);
                        }
                        
                        Interlocked.Increment(ref nf);
                        Interlocked.Increment(ref np);
                        
                        if (!options.NoProgress)
                        {
                            pipeline.LogInfo("computing features for {0} images in parallel, completed {1}/{2}",
                                             np, nc, no);
                        }

                        var result = detector.Detect(imageObs.Url, maskUrl, project);
                        if (result != null)
                        {
                            Interlocked.Add(ref tf, result.Features.Length);

                            if (!options.NoRange)
                            {
                                var xyzOrRng =
                                    observations.Find(obs => obs.ObservationType == RoverProductType.Points &&
                                                      obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                                if (xyzOrRng == null)
                                {
                                    xyzOrRng =
                                        observations.Find(obs => obs.ObservationType == RoverProductType.Range &&
                                                          obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                                }
                                if (xyzOrRng != null)
                                {
                                    Interlocked.Increment(ref wr);
                                    int rf = FeatureDetecting.AddRange(result.Features, pipeline.LoadImage(xyzOrRng.Url));
                                    if (options.RequireRange)
                                    {
                                        result.Features = result.Features.Where(f => f.Range > 0).ToArray();
                                    }
                                    Interlocked.Add(ref trf, rf);
                                }
                            }

                            if (!options.NoSave)
                            {
                                pipeline.SaveDataProduct(project.ProductPath, result, project.Name);
                                imageObs.FeaturesGuid = result.Guid;
                                imageObs.Save(pipeline);
                            }
                        }
                        
                        if (options.WriteFeatureImages && result != null)
                        {
                            WriteFeatureImage(result, maskUrl, imageObs);
                        }

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    }
                });
            double totalSec = UTCTime.Now() - startSec;

            detector.DumpHistograms(pipeline);

            pipeline.LogInfo("processed {0} reconstruction images ({1:F3}s), computed features for {2} images, " +
                             "{3} with range, {4} existing", nc, totalSec, nf, wr, ne);
            pipeline.LogInfo("total {0} features, {1} with range", tf, trf);
            
            return 0;
        }

        private void WriteFeatureImage(DetectedFeatures product, string maskUrl, Observation imageObs)
        {
            //avoid using product.ImageUrl here for legacy compat reasons
            //fortunately we can use imageObs.Url instead
            var img = new Image(pipeline.LoadImage(imageObs.Url)); //don't mutate original image
            var mask = ImageMasker.GetOrCreateMask(pipeline, project, imageObs, masker, maskUrl, img);
            img = FeatureDetecting.DrawFeatures(img, mask, product.Features,
                                                StringHelper.GetLastUrlPathSegment(imageObs.Url));
            PathHelper.EnsureExists(localOutputPath);
            img.Save<byte>(string.Format("{0}{1}_Features{2}", localOutputPath, imageObs.Name, imageExt));
        }
    }
}

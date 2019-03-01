using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-features", HelpText = "create image features locally")]
    public class LocalFeaturesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Detector type", Default = DetectorType.ASIFT)]
        public DetectorType DetectorType { get; set; }

        [Option(HelpText = "Maximum number of features per image", Default = 1000)]
        public int MaxFeaturesPerImage { get; set; }

        [Option(HelpText = "Minimum feature size to keep", Default = 0)]
        public double MinFeatureSize { get; set; }

        [Option(HelpText = "Recreate features that already exist", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Write feature images for debugging", Default = false)]
        public bool WriteFeatureImages { get; set; }

        [Option(HelpText = "Output directory for debug images, or omit to save to project storage", Default = null)]
        public string ImageOutputFolder { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }
    }

    public class LocalFeatures : LocalPipeline
    {
        private LocalFeaturesOptions options;

        public LocalFeatures(LocalFeaturesOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = Project.Find(this, options.ProjectName);

            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            string imagePath = options.ImageOutputFolder;
            if (!string.IsNullOrEmpty(imagePath))
            {
                imagePath = StringHelper.NormalizeUrl(imagePath, "file://");
            }
            else
            {
                imagePath = GetStorageUrl("alignment/FeatureProducts", project.Name);
            }
            imagePath += "/";

            string imageExt = null;
            if (options.WriteFeatureImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, this);
                if (imageExt == null)
                {
                    return 0;
                }
            }

            var frameCache = new FrameCache(this, options.ProjectName);
            frameCache.Preload(loadTransforms: false);

            var observationCache = new ObservationCache(this, options.ProjectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            var imageType = ObservationType.Image.ToString();
            var maskType = ObservationType.RoverMask.ToString();
            var obsForFrame = new Dictionary<string, List<Observation>>();
            foreach (var obs in observationCache.GetAllObservations())
            {
                if (!obsForFrame.ContainsKey(obs.FrameName))
                {
                    obsForFrame[obs.FrameName] = new List<Observation>();
                }
                obsForFrame[obs.FrameName].Add(obs);
            }

            int no = obsForFrame.Values.Count(obsGroup => obsGroup.Any(obs => obs.ObservationType == imageType));
            LogInfo("computing {0} features for {1} reconstruction images", options.DetectorType, no);

            FeatureDetector detector = new FeatureDetector(this, options.DetectorType, options.MaxFeaturesPerImage,
                                                           options.MinFeatureSize);

            double startSec = UTCTime.Now();
            int nc = 0, ne = 0, nf = 0, np = 0;
            CoreLimitedParallel.ForEach(obsForFrame.Values, obsGroup => { 

                    var observations = obsGroup
                    .Cast<RoverObservation>()
                    .ToList();
                    observations.Sort(MSLProject.RoverObservationComparison);
                    
                    var imageObs = observations.Find(obs => obs.ObservationType == imageType);
                    if (imageObs != null)
                    {
                        if (imageObs.FeaturesGuid != null && imageObs.FeaturesGuid != Guid.Empty)
                        {
                            Interlocked.Increment(ref ne);
                            if (!options.RedoFeatures)
                            {
                                LogVerbose("not recomputing features for observation {0}", imageObs.Name);
                                return;
                            }
                            else
                            {
                                LogVerbose("recomputing features for observation {0}", imageObs.Name);
                            }
                        }
                        else
                        {
                            LogVerbose("computing features for observation {0}", imageObs.Name);
                        }
                        
                        Interlocked.Increment(ref nf);
                        Interlocked.Increment(ref np);
                        
                        if (!options.NoProgress)
                        {
                            LogInfo("computing features for {0} images in parallel, completed {1}/{2}", np, nc, no);
                        }
                        
                        var maskObs =
                            observations.Find(obs => obs.ObservationType == maskType &&
                                              obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                        var maskUrl = maskObs != null ? maskObs.Url : null;
                        
                        var features = detector.Detect(imageObs.Url, maskUrl, project.Name, project.ProductPath);
                        if (features != null)
                        {
                            SaveDataProduct(project.ProductPath, features, project.Name);
                            imageObs.FeaturesGuid = features.Guid;
                            imageObs.Save(this);
                        }
                        
                        if (options.WriteFeatureImages)
                        {
                            var img = new Image(LoadImage(imageObs.Url));
                            var mask = FeatureDetecting.MakeMask(this, maskUrl, img, imageObs.Name);
                            img = FeatureDetecting.DrawFeatures(img, mask, features.Features);
                            TemporaryFile.GetAndDelete(imageExt, tmpImage => {
                                    img.Save<byte>(tmpImage);
                                    SaveFile(tmpImage, imagePath + imageObs.Name + "-Features" + imageExt);
                                });
                        }

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    }
                });
            double totalSec = UTCTime.Now() - startSec;
            
            LogInfo("processed {0} reconstruction images ({1:F3}s), computed features for {2} images ({3} existing)",
                    nc, totalSec, nf, ne);

            return 0;
        }
    }
}

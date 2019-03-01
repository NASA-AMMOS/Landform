using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-matching", HelpText = "match features in overlapping images")]
    public class LocalMatchingOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Recreate frustum overlaps that already exist", Default = false)]
        public bool RedoOverlaps { get; set; }

        [Option(HelpText = "Recreate matches that already exist", Default = false)]
        public bool RedoMatches { get; set; }

        [Option(HelpText = "Only keep image correspondences with at least this many matches", Default = 20)]
        public int MinMatchesPerPair { get; set; }

        [Option(HelpText = "Find feature matches for images within the same site drive", Default = false)]
        public bool MatchWithinSiteDrives { get; set; }

        [Option(HelpText = "Write match images for debugging", Default = false)]
        public bool WriteMatchImages { get; set; }

        [Option(HelpText = "Output directory for debug images, or omit to save to project storage", Default = null)]
        public string ImageOutputFolder { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }
    }

    public class LocalMatching : LocalPipeline
    {
        private LocalMatchingOptions options;

        public LocalMatching(LocalMatchingOptions options) : base(options)
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
                imagePath = GetStorageUrl("alignment/MatchingProducts", project.Name);
            }
            imagePath += "/";

            string imageExt = null;
            if (options.WriteMatchImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, this);
                if (imageExt == null)
                {
                    return 0;
                }
            }

            var scene = ImageMatching.BuildSceneAndDetectOverlaps(this, project, loadFeatures: true,
                                                                  redoOverlaps: options.RedoOverlaps,
                                                                  onlyCrossSite: !options.MatchWithinSiteDrives);
            int no = scene.Overlaps.Count;

            LogInfo("finding feature matches for {0} image pairs", no);

            double startSec = UTCTime.Now();
            int nc = 0, np = 0, ne = 0, nr = 0, ns = 0;
            CoreLimitedParallel.ForEach(scene.Overlaps, pair => {

                string pairName = pair.ToStringShort();
                var modelUrl = pair.One;
                var dataUrl = pair.Two;
                var modelNode = scene.ObservationUrlToNode[modelUrl];
                var dataNode = scene.ObservationUrlToNode[dataUrl];
                var modelObs = modelNode.GetComponent<NodeObservation>().Observation;
                var dataObs = dataNode.GetComponent<NodeObservation>().Observation;

                if (!options.RedoMatches)
                {
                    //only hit the database if we need to
                    var overlap = Overlap.Find(this, project.Name, modelObs.Name, dataObs.Name);
                    if (overlap != null)
                    {
                        Interlocked.Increment(ref ne);
                        LogVerbose("not recomputing feature matches for image pair {0}", pairName);
                        return;
                    }
                }

                Interlocked.Increment(ref np);
                if (!options.NoProgress)

                {
                    LogInfo("processing {0} image pairs in parallel, completed {1}/{2}", np, nc, no);
                }

                var result = ImageMatching.ComputeCorrespondence(this, scene, modelUrl, dataUrl,
                                                                 options.MinMatchesPerPair);

                var guid = Guid.Empty;
                if (result != null)
                {
                    Interlocked.Increment(ref nr);
                    SaveDataProduct(project.ProductPath, result, project.Name);
                    guid = result.Guid;
                }

                if (ImageMatching.SaveOverlap(this, project.Name, guid, modelObs.Name, dataObs.Name))
                {
                    Interlocked.Increment(ref ns);
                }

                if (options.WriteMatchImages && result != null)
                {
                    var modelFeatGuid = result.ModelFeaturesGuid;
                    var dataFeatGuid = result.DataFeaturesGuid;
                    var d2m = result.Correspondence.DataToModel;
                    var modelImg = LoadImage(modelUrl);
                    var dataImg = LoadImage(dataUrl);
                    var modelFeat = GetDataProduct<DetectedFeatures>(project.ProductPath, modelFeatGuid, project.Name);
                    var dataFeat = GetDataProduct<DetectedFeatures>(project.ProductPath, dataFeatGuid, project.Name);
                    var img = ImageMatching.DrawMatches(modelImg, dataImg, modelFeat.Features, dataFeat.Features, d2m);
                    TemporaryFile.GetAndDelete(imageExt, tmpImage => {
                            img.Save<byte>(tmpImage);
                            SaveFile(tmpImage, imagePath + modelObs.Name + "-" + dataObs.Name + "-Matches" + imageExt);
                        });
                }

                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            LogInfo("processed {0} image pairs ({1:F3}s), computed {2} correspondences ({3} existing), saved {4}",
                    nc, UTCTime.Now() - startSec, nr, ne, ns);

            return 0;
        }
    }
}

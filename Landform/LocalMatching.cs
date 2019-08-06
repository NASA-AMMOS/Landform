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
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
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

        [Option(HelpText = "Redo everything", Default = false)]
        public bool Redo { get; set; }

        [Option(HelpText = "Feature matching algorithm (EmguSIFT, KnownGeometry, BruteForce, CascadeHashing)", Default = ImageMatching.DEF_MATCHER_TYPE)]
        public ImageMatching.MatcherType MatcherType { get; set; }

        [Option(HelpText = "Only keep image correspondences with at least this many matches", Default = ImageMatching.DEF_MIN_MATCHES)]
        public int MinMatchesPerPair { get; set; }

        [Option(HelpText = "Max descriptor distance ratio", Default = ImageMatching.DEF_MAX_DESCRIPTOR_DISTANCE_RATIO)]
        public double MaxDescriptorDistanceRatio { get; set; }

        [Option(HelpText = "Max descriptor distance", Default = ImageMatching.DEF_MAX_DESCRIPTOR_DISTANCE)]
        public double MaxDescriptorDistance { get; set; }

        [Option(HelpText = "Disable known geometry filter", Default = !ImageMatching.DEF_USE_KNOWN_GEOMETRY_FILTER)]
        public bool NoKnownGeometryFilter { get; set; }

        [Option(HelpText = "Disable known geometry filter for cross-sitedrive matches", Default = false)]
        public bool DisableKnownGeometryFilterForCrossSiteDrive { get; set; }

        [Option(HelpText = "Known geometry filter Mahalanobis threshold", Default = KnownGeometryFilter.DEF_MAHALANOBIS_THRESHOLD)]
        public double KGFMahalanobisThreshold { get; set; }

        [Option(HelpText = "Known geometry filter major axis threshold", Default = KnownGeometryFilter.DEF_MAJOR_AXIS_THRESHOLD)]
        public double KGFMajorAxisThreshold { get; set; }

        [Option(HelpText = "Disable Moisan Stival filter", Default = !ImageMatching.DEF_USE_MOISAN_STIVAL_FILTER)]
        public bool NoMoisanStivalFilter { get; set; }

        [Option(HelpText = "Enable GTM filter", Default = ImageMatching.DEF_USE_GTM_FILTER)]
        public bool UseGTMFilter { get; set; }

        [Option(HelpText = "Find feature matches for images within the same site drive", Default = false)]
        public bool MatchWithinSiteDrives { get; set; }

        [Option(HelpText = "Write match images for debugging", Default = false)]
        public bool WriteMatchImages { get; set; }

        [Option(HelpText = "Write match meshes (in root frame using transform priors) for debugging", Default = false)]
        public bool WriteMatchMeshes { get; set; }

        [Option(HelpText = "Output directory for debug products, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Debug mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Include existing products in histograms", Default = false)]
        public bool TallyExisting { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }

        [Option(HelpText = "Disable saving results to database", Default = false)]
        public bool NoSave { get; set; }

        [Option(HelpText = "Comma separated list of observation pairs (\"Name1-Name2\", order agnostic) to process, omit for all", Default = null)]
        public string OnlyForOverlaps { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalMatching
    {
        private LocalMatchingOptions options;
        private PipelineCore pipeline;
        private string dbgDir;
        private string imageExt;
        private string meshExt;

        private Histogram matchesPerImage = new Histogram(5, "image pairs", "matches after filtering");
        private Histogram matchesPerDistance = new Histogram(50, "feature matches", "distance after filtering");

        public LocalMatching(LocalMatchingOptions options)
        {
            this.options = options;

            if (options.Redo)
            {
                options.RedoMatches = true;
                options.RedoOverlaps = true;
            }

            if (options.Cloud)
            {
                this.pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
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

            dbgDir = pipeline.GetLocalDebugFolder(options.OutputFolder, "alignment/MatchingProducts", project.Name);

            if (options.WriteMatchImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} match images to {1}", imageExt, dbgDir);
            }

            if (options.WriteMatchMeshes)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} match meshes to {1}", meshExt, dbgDir);
            }

            var allowed = (options.OnlyForOverlaps ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s.Split('-'))
                .ToArray();

            Func<Observation, bool> obsFilter =
                obs => allowed.Length == 0 || allowed.Any(pair => obs.Name == pair[0] || obs.Name == pair[1]);

            //note: (n1, n2) vs (n2, n1) is handled inside BuildSceneGraph
            Func<string, string, bool> overlapFilter =
                (n1, n2) => allowed.Length == 0 || allowed.Any(pair => n1 == pair[0] && n2 == pair[1]);

            var scene = ImageMatching.BuildSceneAndDetectOverlaps(pipeline, project, loadFeatures: true,
                                                                  redoOverlaps: options.RedoOverlaps,
                                                                  onlyCrossSite: !options.MatchWithinSiteDrives,
                                                                  obsFilter: obsFilter, overlapFilter: overlapFilter);
            int no = scene.Overlaps.Count;

            pipeline.LogInfo("finding feature matches for {0} image pairs", no);

            var rejectionTallies = new ConcurrentDictionary<string, int>();
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
                    var overlap = Overlap.Find(pipeline, project.Name, modelObs.Name, dataObs.Name);
                    if (overlap != null)
                    {
                        Interlocked.Increment(ref ne);
                        pipeline.LogVerbose("not recomputing feature matches for image pair {0}", pairName);
                        if ((options.WriteMatchImages || options.WriteMatchMeshes || options.TallyExisting) &&
                            overlap.MatchGuid != Guid.Empty)
                        {
                            var product =
                                pipeline.GetDataProduct<ComputedCorrespondence>(project.ProductPath, overlap.MatchGuid);

                            if (options.WriteMatchImages)
                            {
                                WriteMatchImage(product, scene, modelObs, dataObs);
                            }
                            if (options.WriteMatchMeshes)
                            {
                                WriteMatchMesh(product, scene, modelObs, dataObs);
                            }
                            if (options.TallyExisting)
                            {
                                Tally(product);
                            }
                        }
                        return;
                    }
                }

                Interlocked.Increment(ref np);

                if (!options.NoProgress)

                {
                    pipeline.LogInfo("processing {0} image pairs in parallel, completed {1}/{2}", np, nc, no);
                }

                var opts = new ImageMatching.Options()
                {
                    MatcherType = options.MatcherType,
                    MinMatches = options.MinMatchesPerPair,
                    MaxDescriptorDistanceRatio = options.MaxDescriptorDistanceRatio,
                    MaxDescriptorDistance = options.MaxDescriptorDistance,
                    UseKnownGeometryFilter = !options.NoKnownGeometryFilter,
                    UseMoisanStivalFilter = !options.NoMoisanStivalFilter,
                    UseGTMFilter = options.UseGTMFilter,
                    KGFMahalanobisThreshold = options.KGFMahalanobisThreshold,
                    KGFMajorAxisThreshold = options.KGFMajorAxisThreshold
                };
                if (options.DisableKnownGeometryFilterForCrossSiteDrive &&
                    modelObs is RoverObservation && dataObs is RoverObservation)
                {
                    var ro1 = modelObs as RoverObservation;
                    var ro2 = dataObs as RoverObservation;
                    var sd1 = new SiteDrive(ro1.Site, ro1.Drive);
                    var sd2 = new SiteDrive(ro2.Site, ro2.Drive);
                    if (sd1 != sd2)
                    {
                        opts.UseKnownGeometryFilter = false;
                    }
                }

                string rejectionReason;
                var result = ImageMatching.ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl,
                                                                 out rejectionReason, opts);

                var guid = Guid.Empty;
                if (result != null)
                {
                    Interlocked.Increment(ref nr);
                    if (!options.NoSave)
                    {
                        pipeline.SaveDataProduct(project.ProductPath, result, project.Name);
                        guid = result.Guid;
                    }
                    Tally(result);
                }
                else
                {
                    rejectionTallies.AddOrUpdate(rejectionReason, _ => 1, (_, count) => count + 1);
                }

                if (!options.NoSave && ImageMatching.SaveOverlap(pipeline, project.Name, guid, modelObs.Name, dataObs.Name))
                {
                    Interlocked.Increment(ref ns);
                }

                if (options.WriteMatchImages && result != null)
                {
                    WriteMatchImage(result, scene, modelObs, dataObs);
                }

                if (options.WriteMatchMeshes && result != null)
                {
                    WriteMatchMesh(result, scene, modelObs, dataObs);
                }

                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            matchesPerImage.Dump(pipeline);
            matchesPerDistance.Dump(pipeline);

            foreach (var reason in rejectionTallies.Keys.OrderBy(r => r))
            {
                pipeline.LogInfo("rejected {0} image pairs because {1}", rejectionTallies[reason], reason);
            }

            pipeline.LogInfo("processed {0} image pairs ({1:F3}s), computed {2} correspondences ({3} existing), " +
                             "saved {4}", nc, UTCTime.Now() - startSec, nr, ne, ns);

            return 0;
        }

        private void Tally(ComputedCorrespondence product)
        {
            matchesPerImage.Add(product.Correspondence.Count);
            foreach (var dist in product.Correspondence.DescriptorDistance)
            {
                matchesPerDistance.Add(dist);
            }
        }

        private void WriteMatchImage(ComputedCorrespondence product, AlignmentScene scene,
                                     Observation modelObs, Observation dataObs)
        {
            var modelImg = pipeline.LoadImage(modelObs.Url);
            var dataImg = pipeline.LoadImage(dataObs.Url);
            var modelFeat = scene.DetectedFeatures[modelObs.Url];
            var dataFeat = scene.DetectedFeatures[dataObs.Url];
            var d2m = product.Correspondence.DataToModel;
            var modelFile = StringHelper.GetLastUrlPathSegment(modelObs.Url);
            var dataFile = StringHelper.GetLastUrlPathSegment(dataObs.Url);
            var ret = ImageMatching.DrawMatches(modelImg, dataImg, modelFeat, dataFeat, d2m, modelFile, dataFile);
            PathHelper.EnsureExists(dbgDir);
            ret.Save<byte>(string.Format("{0}{1}_{2}_Matches{3}", dbgDir, modelObs.Name, dataObs.Name, imageExt));
        }

        private void WriteMatchMesh(ComputedCorrespondence product, AlignmentScene scene,
                                    Observation modelObs, Observation dataObs)
        {
            var modelNode = scene.ObservationUrlToNode[modelObs.Url];
            var dataNode = scene.ObservationUrlToNode[dataObs.Url];
            var modelCam = modelNode.GetComponent<NodeImage>().CameraModel;
            var dataCam = dataNode.GetComponent<NodeImage>().CameraModel;
            var modelFeat = scene.DetectedFeatures[modelObs.Url];
            var dataFeat = scene.DetectedFeatures[dataObs.Url];
            var d2m = product.Correspondence.DataToModel;
            var modelToRoot = modelNode.GetComponent<NodeUncertainTransform>().To(scene.Root).Mean;
            var dataToRoot = dataNode.GetComponent<NodeUncertainTransform>().To(scene.Root).Mean;
            var ret = ImageMatching.MakeMatchMesh(modelCam, dataCam, modelFeat, dataFeat, modelToRoot, dataToRoot, d2m);
            if (ret.HasFaces)
            {
                var dir = dbgDir + "meshes/";
                PathHelper.EnsureExists(dir);
                ret.Save(string.Format("{0}{1}_{2}_PriorMatches{3}", dir, modelObs.Name, dataObs.Name, meshExt));
            }
        }
    }
}

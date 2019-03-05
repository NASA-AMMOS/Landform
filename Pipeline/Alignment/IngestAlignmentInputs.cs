using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class IngestAlignmentInputs : PipelineRoutine
    {
        public class BaseUrl
        {
            public readonly string Url;
            public readonly bool Recursive;
            public BaseUrl(string url)
            {
                if (url.EndsWith("/**"))
                {
                    Url = url.Substring(0, url.Length - 2); //leave trailing slash
                    Recursive = true;
                }
                else
                {
                    Url = StringHelper.EnsureTrailingSlash(url);
                    Recursive = false;
                }
            }
        }
        public readonly List<BaseUrl> BaseUrls = new List<BaseUrl>();

        private Project project;
        private IngestPDSImage ingester;

        public IngestAlignmentInputs(PipelineCore pipeline, Project project, bool recreateObservations = false,
                                     bool resetTransforms = false, string onlyForSiteDrives = null)
            : base(pipeline)
        {
            if (string.IsNullOrEmpty(project.InputPath))
            {
                throw new ArgumentException("input path not set for project " + project.Name);
            }

            if (project.InputPath.ToLower().EndsWith(".txt"))
            {
                pipeline.GetFile(project.InputPath, file => {
                        foreach (var line in File.ReadAllLines(file))
                        {
                            var url = line.Trim();
                            if (url != "")
                            {
                                BaseUrls.Add(new BaseUrl(url));
                            }
                        }
                    });
            }
            else if (project.InputPath.EndsWith(".json"))
            {
                pipeline.GetFile(project.InputPath, file => {
                        foreach (var url in JsonHelper.FromJson<List<string>>(File.ReadAllText(file), autoTypes: false))
                        {
                            BaseUrls.Add(new BaseUrl(url));
                        }
                    });
            }
            else
            {
                BaseUrls.Add(new BaseUrl(project.InputPath));
            }

            this.project = project;

            SiteDrive[] siteDrives = (onlyForSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .Cast<SiteDrive>()
                .ToArray();

            IngestPDSImage.Filter filter = (imageUrl, pdsMetadata, pdsParser) =>
                siteDrives.Length == 0 ||
                siteDrives.Any(sd => sd == new SiteDrive(pdsParser.Site, pdsParser.Drive));

            ingester = new IngestPDSImage(pipeline, project, recreateObservations, resetTransforms, filter);
        }

        public int Ingest(MSLLocations locations, Action<IngestImage.Result> func = null)
        {
            ingester.Locations = locations;
            string imageObs = ObservationType.Image.ToString();
            double startTime = UTCTime.Now();
            int ni = 0, na = 0, nf = 0, ns = 0, nr = 0;
            var stats = new ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>>();
            foreach (var entry in BaseUrls)
            {
                pipeline.LogInfo("{0}ingesting input files from {1} for alignment project {2}",
                                 entry.Recursive ? "recursively " : "", entry.Url, project.Name);

                var images = pipeline.SearchFiles(entry.Url, "*.IMG", recursive: entry.Recursive);
                CoreLimitedParallel.ForEach(images, url => {

                        Interlocked.Increment(ref ni);
                        var res = ingester.Ingest(url);

                        if (res.Status == IngestImage.Status.Skipped)
                        {
                            Interlocked.Increment(ref ns);
                            pipeline.LogVerbose("{0} ({1})", res.ImageUrl, res.Status);
                        }
                        else if (res.Status == IngestImage.Status.Failed)
                        {
                            Interlocked.Increment(ref nf);
                            pipeline.LogVerbose("{0} ({1})", res.ImageUrl, res.Status);
                        }
                        else if (res.Status == IngestImage.Status.Added || res.Status == IngestImage.Status.Duplicate)
                        {
                            //duplicates are OK to allow ingestion being re-run on an existing proj

                            Interlocked.Increment(ref na);

                            var obs = res.Observation as RoverObservation;
                            var frame = res.ObservationFrame;

                            var sd = new SiteDrive(obs.Site, obs.Drive);
                            var sds = stats.GetOrAdd(sd, _ => new ConcurrentDictionary<string, int>());
                            sds.AddOrUpdate(obs.ObservationType, _ => 1, (_, m) => m + 1);

                            pipeline.LogVerbose("{0} ({1}) {2}x{3} {4} sitedrive={5} -> observation {6}",
                                                res.ImageUrl, res.Status, obs.Width, obs.Height,
                                                obs.ObservationType, sd, obs.Name);

                            if (obs.ObservationType == imageObs && obs.UseForReconstruction)
                            {
                                Interlocked.Increment(ref nr);
                            }

                            if (func != null) func(res);
                        }
                    });
            }

            //populate frame.ObservationNames and frame.Transforms here to avoid read-modify-write MT hazard
            pipeline.LogInfo("adding observations and transforms to frames...");
            var observationCache = new ObservationCache(pipeline, project.Name);
            observationCache.Preload();
            var frameCache = new FrameCache(pipeline, project.Name);
            frameCache.Preload();
            foreach (var observation in observationCache.GetAllObservations())
            {
                frameCache.GetFrame(observation.FrameName).AddObservation(observation);
            }
            foreach (var transform in frameCache.GetAllTransforms())
            {
                frameCache.GetFrame(transform.FrameName).AddTransform(transform);
            }
            foreach (var frame in frameCache.GetAllFrames())
            {
                frame.Save(pipeline);
            }

            pipeline.LogInfo("processed {0} files ({1:F3}s), {2} accepted, {3} failed, {4} skipped",
                             ni, UTCTime.Now() - startTime, na, nf, ns);

            var totalStats = new SortedDictionary<string, int>();
            foreach (var sd in stats.Keys.OrderBy(sd => (int)sd))
            {
                var sds = new SortedDictionary<string, int>();
                foreach (var entry in stats[sd])
                {
                    sds[entry.Key] = entry.Value;
                    if (!totalStats.ContainsKey(entry.Key))
                    {
                        totalStats[entry.Key] = 0;
                    }
                    totalStats[entry.Key] += entry.Value;
                }
                pipeline.LogInfo("sitedrive {0}: {1}", sd,
                                 string.Join(", ", sds.Select(s => s.Value + " " + s.Key + " observations").ToArray()));
            }
            foreach (var entry in totalStats)
            {
                pipeline.LogInfo("total {0} {1} observations{2}", entry.Value, entry.Key,
                                 entry.Key == imageObs ? (" (" + nr + " for reconstruction)") : "");
            }

            return na;
        }
    }
}

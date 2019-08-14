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
        private bool noProgress;
        private ConcurrentDictionary<string, int> indices;
        private HashSet<string> preExistingObservations;

        public IngestAlignmentInputs(PipelineCore pipeline, Project project, MissionSpecific mission,
                                     bool recreateObservations = false, bool resetTransforms = false,
                                     string onlyForSiteDrives = null, string onlyForFrames = null,
                                     bool noProgress = false)
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
           
            string[] frames = (onlyForFrames ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            IngestPDSImage.Filter filter = (imageUrl, pdsMetadata, pdsParser) =>
                {
                    var imgSiteDrive = new SiteDrive(pdsParser.Site, pdsParser.Drive);
                    var imgFrame = mission.GetObservationFrameName(pdsParser);
                    return
                    (siteDrives.Length == 0 || siteDrives.Any(sd => sd == imgSiteDrive)) &&
                    (frames.Length == 0 || frames.Any(frame => frame == imgFrame));
                };

            this.noProgress = noProgress;

            pipeline.LogInfo("scanning for existing observations...");
            preExistingObservations = new HashSet<string>();
            indices = new ConcurrentDictionary<string, int>();
            RoverObservation.Find(pipeline, project.Name).ToList().ForEach(ro => {
                    preExistingObservations.Add(ro.Name);
                    indices.GetOrAdd(ro.Name, _ => ro.Index);
                });
            pipeline.LogInfo("found {0} existing observations in project", preExistingObservations.Count);

            ingester = new IngestPDSImage(pipeline, project, recreateObservations, resetTransforms, filter, indices);
        }

        public int Ingest(MSLLocations locations, MSLPlaces places, MSLLegacyManifest manifest,
                          Action<IngestImage.Result> func = null)
        {
            if (places != null && !places.CredentialsLoaded())
            {
                pipeline.LogWarn("credentials for PlacesDB priors not available, disabling PlacesDB");
                places = null;
            }

            ingester.Locations = locations;
            ingester.Places = places;
            ingester.LegacyManifest = manifest;

            //site drive -> observation type -> count
            var stats = new ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>>();
            var minSol = new ConcurrentDictionary<SiteDrive, int>();
            var maxSol = new ConcurrentDictionary<SiteDrive, int>();

            var orphans = new ConcurrentDictionary<string, bool>();
            foreach (var obsName in preExistingObservations)
            {
                orphans.AddOrUpdate(obsName, _ => true, (_, __) => true);
            }

            string imageObs = ObservationType.Image.ToString();
            double startTime = UTCTime.Now();
            int nt = 0, ni = 0, na = 0, ne = 0, nf = 0, ns = 0, nr = 0, np = 0;
            foreach (var entry in BaseUrls)
            {
                pipeline.LogInfo("{0}ingesting input files from {1} for alignment project {2}",
                                 entry.Recursive ? "recursively " : "", entry.Url, project.Name);

                var images = pipeline.SearchFiles(entry.Url, "*.IMG", recursive: entry.Recursive).ToList();
                images.AddRange(pipeline.SearchFiles(entry.Url, "*.LBL", recursive: entry.Recursive).ToList());
                images.AddRange(pipeline.SearchFiles(entry.Url, "*.VIC", recursive: entry.Recursive).ToList());

                nt = images.Count();

                CoreLimitedParallel.ForEach(images, url => {

                        Interlocked.Increment(ref ni);
                        Interlocked.Increment(ref np);
                        if (!noProgress)
                        {
                            pipeline.LogInfo("ingesting {0} images in parallel, completed {1}/{2}", np, ni, nt);
                        }

                        var res = ingester.Ingest(url);

                        Interlocked.Decrement(ref np);

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

                            if (res.Status == IngestImage.Status.Duplicate)
                            {
                                Interlocked.Increment(ref ne);
                            }

                            var obs = res.Observation as RoverObservation;
                            var frame = res.ObservationFrame;

                            var sd = new SiteDrive(obs.Site, obs.Drive);
                            var sds = stats.GetOrAdd(sd, _ => new ConcurrentDictionary<string, int>());
                            sds.AddOrUpdate(obs.ObservationType, _ => 1, (_, count) => count + 1);

                            minSol.AddOrUpdate(sd, _ => obs.Day, (_, sol) => Math.Min(sol, obs.Day));
                            maxSol.AddOrUpdate(sd, _ => obs.Day, (_, sol) => Math.Max(sol, obs.Day));

                            orphans.TryRemove(obs.Name, out bool ignore);

                            pipeline.LogVerbose("{0} ({1}) -> observation {2}",
                                                res.ImageUrl, res.Status, obs.ToString(brief: true));

                            if (obs.ObservationType == imageObs && obs.UseForReconstruction)
                            {
                                Interlocked.Increment(ref nr);
                            }
                            
                            if (func != null) func(res);
                        }
                    });
            }
                                            
            //populate frame.ObservationNames and frame.Transforms here to avoid read-modify-write MT hazard
            if (na > ne) //don't write to database if we didn't add any observations
            {
                pipeline.LogInfo("adding observations and transforms to frames...");
                var observationCache = new ObservationCache(pipeline, project.Name);
                observationCache.Preload();
                var frameCache = new FrameCache(pipeline, project.Name);
                frameCache.Preload();
                foreach (var observation in observationCache.GetAllObservations())
                {
                    var frame = frameCache.GetFrame(observation.FrameName);
                    lock (frame.ObservationNames)
                    {
                        frame.ObservationNames.Add(observation.Name);
                    }
                }
                foreach (var transform in frameCache.GetAllTransforms())
                {
                    var frame = frameCache.GetFrame(transform.FrameName);
                    lock (frame.Transforms)
                    {
                        frame.Transforms.Add(transform.Source);
                    }
                }
                foreach (var frame in frameCache.GetAllFrames())
                {
                    frame.Save(pipeline);
                }
            }

            if (orphans.Count > 0)
            {
                pipeline.LogInfo("deleting {0} orphan observations", orphans.Count);
                foreach (var orphanName in orphans.Keys)
                {
                    var obs = RoverObservation.Find(pipeline, project.Name, orphanName);
                    if (obs != null)
                    {
                        pipeline.LogDebug("deleting orphan observation {0}", orphanName);
                        pipeline.DeleteDatabaseItem(obs);
                    }
                    indices.TryRemove(orphanName, out int ignore);
                }
            }

            if (indices.Count > 0)
            {
                int minIndex = indices.Values.Min();
                int maxIndex = indices.Values.Max();
                pipeline.LogInfo("min observation index {0}, max {1}", minIndex, maxIndex);
                if (minIndex < Observation.MIN_INDEX)
                {
                    pipeline.LogInfo("min observation index {0} less than min allowed index {1}",
                                     minIndex, Observation.MIN_INDEX);
                }
                if (maxIndex > Observation.MAX_INDEX)
                {
                    pipeline.LogInfo("max observation index {0} greater than max allowed index {1}",
                                     maxIndex, Observation.MAX_INDEX);
                }
            }

            pipeline.LogInfo("processed {0} files ({1:F3}s), {2} accepted, {3} existing, {4} failed, {5} skipped",
                             ni, UTCTime.Now() - startTime, na, ne, nf, ns);

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
                pipeline.LogInfo("sitedrive {0}, sol {1} to {2}: {3}", sd, minSol[sd], maxSol[sd],
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

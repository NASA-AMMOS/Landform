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
        private MissionSpecific mission;
        private IngestPDSImage ingester;
        private bool noProgress;
        private ConcurrentDictionary<string, int> indices; //observation name -> observation index
        private HashSet<string> preExistingObservations;

        private FrameCache frameCache;
        private ObservationCache observationCache;

        public IngestAlignmentInputs(PipelineCore pipeline, Project project, MissionSpecific mission,
                                     bool recreateObservations = false, bool resetTransforms = false,
                                     string onlyForSiteDrives = null, string onlyForFrames = null,
                                     bool noProgress = false)
            : base(pipeline)
        {
            this.project = project;
            this.mission = mission;

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

            SiteDrive[] siteDrives = SiteDrive.ParseList(onlyForSiteDrives);

            string[] frames = StringHelper.ParseList(onlyForFrames);

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
            ingester.Locations = locations;
            ingester.Places = places;
            ingester.LegacyManifest = manifest;

            pipeline.LogInfo("locations db priors {0}, places db priors {1}, legacy manifest db priors {2}",
                             locations != null ? "enabled" : "disabled",
                             places != null ? "enabled" : "disabled",
                             manifest != null ? "enabled" : "disabled");

            //site drive -> sensor type -> count
            var stats = new ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>>();

            //sensor type -> count
            var reconstructionStats = new ConcurrentDictionary<string, int>();

            var minSol = new ConcurrentDictionary<SiteDrive, int>();
            var maxSol = new ConcurrentDictionary<SiteDrive, int>();

            var orphans = new ConcurrentDictionary<string, bool>();
            foreach (var obsName in preExistingObservations)
            {
                orphans.AddOrUpdate(obsName, _ => true, (_, __) => true);
            }

            double startTime = UTCTime.Now();
            int nt = 0, nu = 0, ni = 0, na = 0, ne = 0, nf = 0, ns = 0, np = 0;

            ConcurrentDictionary<string, bool> done = new ConcurrentDictionary<string, bool>();

            Action<string> ingestUrl = url => {

                Interlocked.Increment(ref nu);
                Interlocked.Increment(ref ni);

                if (done.ContainsKey(url))
                {
                    return;
                }

                Interlocked.Increment(ref np);
                if (!noProgress)
                {
                    pipeline.LogInfo("ingesting {0} images in parallel, completed {1}/{2}, {3} overall",
                                     np, ni, nt, nu);
                }
                
                var res = ingester.Ingest(url);
                
                Interlocked.Decrement(ref np);

                done.AddOrUpdate(res.Url, _ => true, (_, __) => true);
                if (res.DataUrl != null)
                {
                    done.AddOrUpdate(res.DataUrl, _ => true, (_, __) => true);
                }

                if (res.Status == IngestImage.Status.Skipped)
                {
                    Interlocked.Increment(ref ns);
                    pipeline.LogVerbose(res.ToString());
                }
                else if (res.Status == IngestImage.Status.Failed)
                {
                    Interlocked.Increment(ref nf);
                    pipeline.LogVerbose(res.ToString());
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
                    var statsKey = mission.ClassifyCamera(obs.Sensor) + " " + obs.ObservationType;
                    sds.AddOrUpdate(statsKey, _ => 1, (_, count) => count + 1);
                    
                    minSol.AddOrUpdate(sd, _ => obs.Day, (_, sol) => Math.Min(sol, obs.Day));
                    maxSol.AddOrUpdate(sd, _ => obs.Day, (_, sol) => Math.Max(sol, obs.Day));
                    
                    orphans.TryRemove(obs.Name, out bool ignore);
                    
                    pipeline.LogVerbose("{0} -> observation {1}", res.ToString(), obs.ToString(brief: true));
                    
                    if (obs.UseForReconstruction)
                    {
                        reconstructionStats.AddOrUpdate(statsKey, _ => 1, (_, count) => count + 1);
                    }
                    
                    if (func != null) func(res);
                }
            };

            //if there are any LBL files ingest them first
            //because they will generally refer to other IMG files containing the actual image data
            //and for each pair (foo.LBL, foo.IMG) we want to mark both URLs as done
            //because below we're going to also ingest all IMG files
            //and we can avoid trying to ingest all the foo.IMG that were referred to by foo.LBL
            //foo.IMG will be a raw PDS data file with no headers and will error out if we try to ingest it anyway
            HashSet<string> urls = new HashSet<string>();
            foreach (var entry in BaseUrls)
            {
                pipeline.LogInfo("{0}ingesting input LBL files from {1} for alignment project {2}",
                                 entry.Recursive ? "recursively " : "", entry.Url, project.Name);
                urls.UnionWith(pipeline.SearchFiles(entry.Url, "*.LBL", recursive: entry.Recursive).ToList());
            }
            nt = urls.Count();
            CoreLimitedParallel.ForEach(urls, ingestUrl);

            urls.Clear();
            foreach (var entry in BaseUrls)
            {
                pipeline.LogInfo("{0}ingesting input IMG and VIC files from {1} for alignment project {2}",
                                 entry.Recursive ? "recursively " : "", entry.Url, project.Name);
                urls.UnionWith(pipeline.SearchFiles(entry.Url, "*.IMG", recursive: entry.Recursive).ToList());
                urls.UnionWith(pipeline.SearchFiles(entry.Url, "*.VIC", recursive: entry.Recursive).ToList());
            }
            nt = urls.Count();
            ni = 0;
            CoreLimitedParallel.ForEach(urls, ingestUrl);
                                            
            //populate frame.ObservationNames and frame.Transforms here to avoid read-modify-write MT hazard

            frameCache = new FrameCache(pipeline, project.Name);
            frameCache.Preload();

            observationCache = new ObservationCache(pipeline, project.Name);
            observationCache.Preload();

            UpdateFrames();

            CheckPriors();

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

            pipeline.LogInfo("processed {0} urls ({1:F3}s), {2} accepted, {3} existing, {4} failed, {5} skipped",
                             nu, UTCTime.Now() - startTime, na, ne, nf, ns);

            var totalStats = new SortedDictionary<string, int>();
            foreach (var sd in stats.Keys.OrderBy(sd => sd))
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
                                 string.Join(", ", sds.Select(s => s.Value + " " + s.Key).ToArray()));
            }
            foreach (var entry in totalStats)
            {
                pipeline.LogInfo("total {0} {1}, {2} for reconstruction", entry.Value, entry.Key,
                                 reconstructionStats.ContainsKey(entry.Key) ? reconstructionStats[entry.Key] : 0);
            }

            return na;
        }

        private void UpdateFrames()
        {
            pipeline.LogInfo("adding new observations and transforms to frames...");

            var framesToSave = new HashSet<string>();

            foreach (var observation in observationCache.GetAllObservations())
            {
                var frame = frameCache.GetFrame(observation.FrameName);
                lock (frame.ObservationNames)
                {
                    if (frame.ObservationNames.Add(observation.Name))
                    {
                        framesToSave.Add(frame.Name);
                    }
                }
            }

            foreach (var transform in frameCache.GetAllTransforms())
            {
                var frame = frameCache.GetFrame(transform.FrameName);
                lock (frame.Transforms)
                {
                    if (frame.Transforms.Add(transform.Source))
                    {
                        framesToSave.Add(frame.Name);
                    }
                }
            }

            pipeline.LogInfo("saving {0} updated frames", framesToSave.Count);
            foreach (var frame in framesToSave)
            {
                frameCache.GetFrame(frame).Save(pipeline);
            }
        }

        private void CheckPriors()
        {
            pipeline.LogInfo("checking priors...");

            var sdsWithNoPriors = new HashSet<string>();
            var sdsWithPDSPriors = new HashSet<string>();
            var sdsWithMixedPriors = new HashSet<string>();
            var sdsWithGoodPriors = new HashSet<string>();
            foreach (var frame in frameCache.GetAllFrames())
            {
                if (frame.ParentName == mission.RootFrameName())
                {
                    var xform = frameCache.GetBestPrior(frame);
                    var group = xform == null ? sdsWithNoPriors :
                        xform.Source == TransformSource.PDS ? sdsWithPDSPriors :
                        xform.Source == TransformSource.PlacesDBSitePDSLocal ? sdsWithMixedPriors :
                        sdsWithGoodPriors;
                    
                    group.Add(frame.Name);
                }
            }

            pipeline.LogInfo("{0} sitedrives with no priors: {1}",
                             sdsWithNoPriors.Count, string.Join(", ", sdsWithNoPriors));
            pipeline.LogInfo("{0} sitedrives with only site-relative PDS priors: {1}",
                             sdsWithPDSPriors.Count, string.Join(", ", sdsWithPDSPriors));
            pipeline.LogInfo("{0} sitedrives with only PlacesDB site but PDS local_level priors: {1}",
                             sdsWithMixedPriors.Count, string.Join(", ", sdsWithMixedPriors));
            pipeline.LogInfo("{0} sitedrives with full priors: {1}",
                             sdsWithGoodPriors.Count, string.Join(", ", sdsWithGoodPriors));

            if (sdsWithNoPriors.Count > 0)
            {
                pipeline.LogError("incomplete priors! {0} sitedrives with no priors", sdsWithNoPriors.Count);
            }
            else if (sdsWithPDSPriors.Count > 0)
            {
                int toRoot = sdsWithGoodPriors.Count + sdsWithMixedPriors.Count;
                if (toRoot > 0)
                {
                    pipeline.LogError("incomplete priors! {0} sitedrives relative to root and {1} relative to site",
                                      toRoot, sdsWithPDSPriors.Count);
                }
                else
                {
                    var sites = new HashSet<int>();
                    foreach (var sd in sdsWithPDSPriors)
                    {
                        sites.Add((new SiteDrive(sd)).Site);
                    }
                    if (sites.Count > 1)
                    {
                        pipeline.LogError("incomplete priors! sitedrives relative to {0} different site frames",
                                          sites.Count);
                    }
                    else
                    {
                        pipeline.LogError("incomplete priors! all sitedrives relative to site {0} frame, not root",
                                          sites.First());
                    }
                }
            }
            else if (sdsWithMixedPriors.Count > 0)
            {
                pipeline.LogWarn("mixed priors! {0} sitedrives with PlacesDB site offset but PDS local_level",
                                 sdsWithMixedPriors.Count);
            }
        }
    }
}

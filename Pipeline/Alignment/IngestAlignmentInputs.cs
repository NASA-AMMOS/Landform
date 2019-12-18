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
using OPS.Geometry;
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

        private ConcurrentDictionary<Tuple<int, int>, UncertainRigidTransform> pdsSiteOffsets =
            new ConcurrentDictionary<Tuple<int, int>, UncertainRigidTransform>();

        public IngestAlignmentInputs(PipelineCore pipeline, Project project, MissionSpecific mission,
                                     bool recreateObservations = false, bool resetTransforms = false,
                                     string onlyForObservations = null, string onlyForFrames = null,
                                     string onlyForCameras = null, string onlyForSiteDrives = null, 
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

            var observations = StringHelper.ParseList(onlyForObservations);
            var frames = StringHelper.ParseList(onlyForFrames);
            var cameras = RoverCamera.ParseList(onlyForCameras);
            var siteDrives = SiteDrive.ParseList(onlyForSiteDrives);
            IngestPDSImage.Filter filter = (imageUrl, pdsMetadata, pdsParser) =>
                {
                    var imgId = pdsParser.ProductIdString;
                    var imgSiteDrive = new SiteDrive(pdsParser.Site, pdsParser.Drive);
                    var imgFrame = mission.GetObservationFrameName(pdsParser);
                    var imgCam = mission.GetCamera(pdsParser);
                    return
                    (observations.Length == 0 || observations.Any(obs => obs == imgId)) &&
                    (siteDrives.Length == 0 || siteDrives.Any(sd => sd == imgSiteDrive)) &&
                    (frames.Length == 0 || frames.Any(frame => frame == imgFrame)) &&
                    (cameras.Length == 0 || cameras.Any(cam => RoverCamera.IsCamera(cam, imgCam)));
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

            ingester = new IngestPDSImage(pipeline, project, recreateObservations, resetTransforms, filter,
                                          indices, pdsSiteOffsets);
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

            //PHASE 1: ingest files

            double startTime = UTCTime.Now();
            int nt = 0, nu = 0, ni = 0, np = 0;
            var results = new ConcurrentDictionary<string, IngestImage.Result>();
            Action<string> ingestUrl = url => {

                Interlocked.Increment(ref nu);
                Interlocked.Increment(ref ni);

                if (results.ContainsKey(url))
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

                results.AddOrUpdate(res.Url, _ => res, (_, __) => res);
                if (res.DataUrl != null)
                {
                    results.AddOrUpdate(res.DataUrl, _ => res, (_, __) => res);
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
                urls.UnionWith(pipeline.SearchFiles(entry.Url, "*.LBL", recursive: entry.Recursive));
            }
            nt = urls.Count();
            CoreLimitedParallel.ForEach(urls, ingestUrl);

            urls.Clear();
            foreach (var entry in BaseUrls)
            {
                pipeline.LogInfo("{0}ingesting input IMG and VIC files from {1} for alignment project {2}",
                                 entry.Recursive ? "recursively " : "", entry.Url, project.Name);
                urls.UnionWith(pipeline.SearchFiles(entry.Url, "*.IMG", recursive: entry.Recursive));
                urls.UnionWith(pipeline.SearchFiles(entry.Url, "*.VIC", recursive: entry.Recursive));
            }
            nt = urls.Count();
            ni = 0;
            CoreLimitedParallel.ForEach(urls, ingestUrl);

            int na = CullObservations(results); //PHASE 2: cull observations (e.g. selects latest versions)

            DeleteOrphans(results.Values); //PHASE 3: delete orphan observations

            UpdateFrames(); //PHASE 4: update frames and transforms

            //PHASE 5: callback
            if (func != null)
            {
                foreach (var res in results.Values.Where(res => res.Accepted))
                {
                    func(res);
                }
            }

            SpewStats(results.Values, nu, startTime); //PHASE 6: collect and spew stats

            return na;
        }

        private int CullObservations(IDictionary<string, IngestImage.Result> results)
        {
            var acceptedUrls = new HashSet<string>();
            acceptedUrls.UnionWith(results.Values.Where(res => res.Accepted).Select(res => res.Url));
            int na = acceptedUrls.Count;
            var filteredUrls = RoverObservationComparator.FilterProductIdGroups(acceptedUrls, mission).ToList();
            pipeline.LogInfo("culled {0} -> {1} observations by product ID groups", na, filteredUrls.Count);

            var filteredObs = filteredUrls
                .Select(url => results[url].Observation)
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .ToList();
            na = filteredObs.Count;

            var obsNames = new HashSet<string>();
            obsNames.UnionWith(filteredObs.Select(obs => obs.Name));
            foreach (var res in results.Values)
            {
                if (res.Accepted && !obsNames.Contains(res.Observation.Name))
                {
                    res.Status = IngestImage.Status.Culled;
                }
            }

            return na;
        }

        private void DeleteOrphans(IEnumerable<IngestImage.Result> results)
        {
            var orphans = new HashSet<string>();
            orphans.UnionWith(preExistingObservations);
            orphans.ExceptWith(results.Where(res => res.Accepted).Select(res => res.Observation.Name));
            orphans.UnionWith(results
                              .Where(res => res.Status == IngestImage.Status.Culled)
                              .Select(res => res.Observation.Name));
            if (orphans.Count > 0)
            {
                pipeline.LogInfo("deleting {0} orphan observations", orphans.Count);
                foreach (var orphanName in orphans)
                {
                    var obs = RoverObservation.Find(pipeline, project.Name, orphanName);
                    if (obs != null)
                    {
                        pipeline.LogVerbose("deleting orphan observation {0}", orphanName);
                        obs.Delete(pipeline);
                    }
                    indices.TryRemove(orphanName, out int ignore);
                }
                //orphaned frames and transforms will be handled in UpdateFrames()
            }
        }

        private void UpdateFrames()
        {
            pipeline.LogInfo("adding new observations and transforms to frames...");

            var frameCache = new FrameCache(pipeline, project.Name);
            frameCache.Preload();

            var observationCache = new ObservationCache(pipeline, project.Name);
            observationCache.Preload();

            //register each observation with the frame it uses
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

            //de-register any missing observations referenced by a frame
            //also cull any frames not used by any observation
            var framesToDelete = new HashSet<string>();
            foreach (var frame in frameCache.GetAllFrames())
            {
                lock (frame.ObservationNames)
                {
                    var dead = frame.ObservationNames.Where(obs => !observationCache.ContainsObservation(obs)).ToList();
                    frame.ObservationNames.ExceptWith(dead);
                    if (frame.ObservationNames.Count == 0 && frameCache.GetChildren(frame).Count() == 0)
                    {
                        framesToDelete.Add(frame.Name);
                    }
                    else if (dead.Count > 0)
                    {
                        framesToSave.Add(frame.Name);
                    }
                }
            }

            pipeline.LogInfo("deleting {0} orphan frames", framesToDelete.Count);
            foreach (var frameName in framesToDelete)
            {
                pipeline.LogVerbose("deleting orphan frame {0}", frameName);
                var frame = frameCache.GetFrame(frameName);
                frame.Delete(pipeline);
                frameCache.Remove(frame);
            }

            //register each transform to its frame
            //also cull any transforms associated with a deleted frame
            var transformsToDelete = new HashSet<string>();
            foreach (var transform in frameCache.GetAllTransforms())
            {
                if (frameCache.ContainsFrame(transform.FrameName))
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
                else
                {
                    transformsToDelete.Add(transform.Name);
                }
            }

            pipeline.LogInfo("deleting {0} orphan transforms", transformsToDelete.Count);
            foreach (var transformName in transformsToDelete)
            {
                pipeline.LogVerbose("deleting orphan transform {0}", transformName);
                var transform = frameCache.GetTransform(transformName);
                transform.Delete(pipeline);
                frameCache.Remove(transform);
            }

            pipeline.LogInfo("saving {0} updated frames", framesToSave.Count);
            foreach (var frame in framesToSave)
            {
                frameCache.GetFrame(frame).Save(pipeline);
            }

            if (!frameCache.ChainPriors(pdsSiteOffsets))
            {
                pipeline.LogError("failed to chain all PDS priors");
            }

            if (!frameCache.CheckPriors(out string effectiveRoot))
            {
                pipeline.LogError("incomplete priors: not all sitedrives are connected");
            }

            pipeline.LogInfo("effective root frame for project {0}: {1}", project.Name, effectiveRoot);
        }

        private void SpewStats(IEnumerable<IngestImage.Result> results, int numUrls, double startTime)
        {
            void tally(Dictionary<string, int> table, string key)
            {
                if (!table.ContainsKey(key))
                {
                    table[key] = 1;
                }
                else
                {
                    table[key] = table[key] + 1;
                }
            }
            var stats = new Dictionary<SiteDrive, Dictionary<string, int>>(); //site drive -> sensor type -> count
            var alignmentStats = new Dictionary<string, int>(); //sensor type -> count
            var meshingStats = new Dictionary<string, int>(); //sensor type -> count
            var texturingStats = new Dictionary<string, int>(); //sensor type -> count
            var minSol = new Dictionary<SiteDrive, int>();
            var maxSol = new Dictionary<SiteDrive, int>();
            int nc = 0, ns = 0, nf = 0, na = 0, ne = 0;
            foreach (var res in results)
            {
                if (!res.Accepted)
                {
                    switch (res.Status)
                    {
                        case IngestImage.Status.Culled: nc++; break;
                        case IngestImage.Status.Skipped: ns++; break;
                        case IngestImage.Status.Failed: nf++; break;
                        default: pipeline.LogWarn("unhandled status {0}", res.Status); break;
                    }
                    pipeline.LogVerbose(res.ToString());
                    continue;
                }

                na++;

                if (res.Status == IngestImage.Status.Duplicate)
                {
                    ne++;
                }
                
                var obs = res.Observation as RoverObservation;
                var frame = res.ObservationFrame;
                
                var sd = new SiteDrive(obs.Site, obs.Drive);
                if (!stats.ContainsKey(sd))
                {
                    stats[sd] = new Dictionary<string, int>();
                }
                var sds = stats[sd];
                var statsKey = mission.ClassifyCamera(obs.Camera) + " " + obs.ObservationType;
                tally(sds, statsKey);
                
                if (!minSol.ContainsKey(sd))
                {
                    minSol[sd] = obs.Day;
                }
                else
                {
                    minSol[sd] = Math.Min(minSol[sd], obs.Day);
                }
                
                if (!maxSol.ContainsKey(sd))
                {
                    maxSol[sd] = obs.Day;
                }
                else
                {
                    maxSol[sd] = Math.Max(maxSol[sd], obs.Day);
                }
                
                pipeline.LogVerbose("{0} -> observation {1}", res.ToString(), obs.ToString(brief: true));
                
                if (obs.UseForAlignment)
                {
                    tally(alignmentStats, statsKey);
                }
                
                if (obs.UseForMeshing)
                {
                    tally(meshingStats, statsKey);
                }
                
                if (obs.UseForTexturing)
                {
                    tally(texturingStats, statsKey);
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

            pipeline.LogInfo("processed {0} urls ({1:F3}s): " +
                             "{2} accepted, {3} existing, {4} failed, {5} skipped, {6} culled",
                             numUrls, UTCTime.Now() - startTime, na, ne, nf, ns, nc);

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
                pipeline.LogInfo("total {0} {1}, {2} for alignment, {3} for meshing, {4} for texturing",
                                 entry.Value, entry.Key,
                                 alignmentStats.ContainsKey(entry.Key) ? alignmentStats[entry.Key] : 0,
                                 meshingStats.ContainsKey(entry.Key) ? meshingStats[entry.Key] : 0,
                                 texturingStats.ContainsKey(entry.Key) ? texturingStats[entry.Key] : 0);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public class IngestAlignmentInputs
    {
        public readonly PipelineCore pipeline;

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

        private string orbitalDEM, orbitalImage;
        private bool noSurface, noOrbital;
        private Frame orbitalFrame;

        private double? orbitalDEMElevation;

        public IngestAlignmentInputs(PipelineCore pipeline, Project project, MissionSpecific mission,
                                     bool recreateObservations = false, bool resetTransforms = false,
                                     string onlyForObservations = null, string onlyForFrames = null,
                                     string onlyForCameras = null, string onlyForSiteDrives = null, 
                                     string onlyForSols = null,
                                     string orbitalDEM = null, string orbitalImage = null,
                                     bool noSurface = false, bool noOrbital = false,
                                     bool noProgress = false)
        {
            this.pipeline = pipeline;
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
            var sols = ExpandSolSpecifier(onlyForSols);
            IngestPDSImage.Filter filter = (imageUrl, pdsMetadata, pdsParser) =>
                {
                    var imgId = pdsParser.ProductIdString;
                    var imgSiteDrive = new SiteDrive(pdsParser.Site, pdsParser.Drive);
                    var imgFrame = mission.GetObservationFrameName(pdsParser);
                    var imgCam = mission.GetCamera(pdsParser);
                    var imgSol = mission.DayNumber(pdsParser);
                    return
                    (observations.Length == 0 || observations.Any(obs => obs == imgId)) &&
                    (siteDrives.Length == 0 || siteDrives.Any(sd => sd == imgSiteDrive)) &&
                    (sols.Length == 0 || sols.Any(sol => sol == imgSol)) &&
                    (frames.Length == 0 || frames.Any(frame => frame == imgFrame)) &&
                    (cameras.Length == 0 || cameras.Any(cam => RoverCamera.IsCamera(cam, imgCam)));
                };

            this.orbitalDEM = orbitalDEM;
            this.orbitalImage = orbitalImage;

            this.noSurface = noSurface;
            this.noOrbital = noOrbital;

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

        public static int[] ExpandSolSpecifier(string solString)
        {
            string[] parts = (solString ?? "").Split(',');
            List<int> sols = new List<int>();
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    if (part.Contains('-'))
                    {
                        var subparts = part.Split('-');
                        int startSol = int.Parse(subparts[0]);
                        int endSol = int.Parse(subparts[1]);
                        for(int i = startSol; i <= endSol; i++)
                        {
                            sols.Add(i);
                        }
                    }
                    else
                    {
                        sols.Add(int.Parse(part));
                    }                       
                }
            }
            return sols
                .Distinct()
                .OrderBy(sol => sol)
                .ToArray();
        }

        public int Ingest(PlacesDB places, MSLLocations locations, MSLLegacyManifest manifest,
                          Action<IngestImage.Result> callback = null)
        {
            pipeline.LogInfo("LocationsDB priors {0}, PlacesDB priors {1}, legacy manifest priors {2}",
                             locations != null ? "enabled" : "disabled",
                             places != null ? "enabled" : "disabled",
                             manifest != null ? "enabled" : "disabled");

            int na = 0;
            if (!noSurface)
            {
                pipeline.LogInfo("ingesting surface observations");
                na += IngestSurfaceObservations(places, locations, manifest, callback);
            }
            else
            {
                pipeline.LogInfo("not ingesting surface observations");
                //this stuff is normally done in UpdateFrames()
                var frameCache = new FrameCache(pipeline, project.Name);
                frameCache.Preload();
                if (!noOrbital)
                {
                    EnsureOrbitalFrame(places, frameCache);
                }
                AddOrUpdateGISMetadata(places, frameCache);
            }

            if (!noOrbital)
            {
                pipeline.LogInfo("ingesting orbital observations");
                na += IngestOrbitalObservations(places);
            }
            else
            {
                pipeline.LogInfo("not ingesting orbital observations");
            }

            return na;
        }

        private int IngestSurfaceObservations(PlacesDB places, MSLLocations locations, MSLLegacyManifest manifest,
                                              Action<IngestImage.Result> callback = null)
        {
            ingester.Places = places;
            ingester.Locations = locations;
            ingester.LegacyManifest = manifest;

            //PHASE 1: ingest files

            //url without extension -> Result
            var results = new ConcurrentDictionary<string, IngestImage.Result>();

            double startTime = UTCTime.Now();
            int nt = 0, nu = 0, ni = 0, np = 0;
            Action<string> ingestUrl = url => {

                Interlocked.Increment(ref nu);
                Interlocked.Increment(ref ni);

                string urlWithoutExt = StringHelper.StripUrlExtension(url);

                if (results.ContainsKey(urlWithoutExt))
                {
                    return;
                }

                Interlocked.Increment(ref np);

                pipeline.LogVerbose("ingesting {0} images in parallel, completed {1}/{2}, {3} overall",
                                    np, ni, nt, nu);
                
                var res = ingester.Ingest(url);
                
                Interlocked.Decrement(ref np);

                results.AddOrUpdate(urlWithoutExt, _ => res, (_, __) => res);
                if (res.DataUrl != null)
                {
                    string dataUrlWithoutExt = StringHelper.StripUrlExtension(res.DataUrl);
                    if (dataUrlWithoutExt != urlWithoutExt)
                    {
                        results.AddOrUpdate(dataUrlWithoutExt, _ => res, (_, __) => res);
                    }
                }
            };

            HashSet<string> urls = new HashSet<string>();

            //if there are any LBL files ingest them before IMG
            //because they will generally refer to other IMG files containing the actual image data
            //and for each pair (foo.LBL, foo.IMG) we want to mark both URLs as done
            //because below we're going to also ingest all IMG files
            //and we can avoid trying to ingest all the foo.IMG that were referred to by foo.LBL
            //foo.IMG will be a raw PDS data file with no headers and will error out if we try to ingest it anyway
            var pdsExts = StringHelper.ParseExts(mission.GetPDSExts(prioritizePDSLabelFiles: true))
                .Select(ext => ext.ToUpper().TrimStart('.'))
                .ToArray();
            foreach (var ext in pdsExts)
            {
                urls.Clear();
                foreach (var url in BaseUrls)
                {
                    pipeline.LogInfo("{0}ingesting {1} files from {2} for alignment project {3}",
                                     url.Recursive ? "recursively " : "", ext, url.Url, project.Name);
                    urls.UnionWith(pipeline.SearchFiles(url.Url, "*." + ext, recursive: url.Recursive,
                                                        ignoreCase: true));
                }
                ni = 0;
                nt = urls.Count();
                CoreLimitedParallel.ForEach(urls, ingestUrl);
            }

            AddAlternateExtensions(results);

            int na = CullObservations(results); //PHASE 2: cull observations (e.g. selects latest versions)

            DeleteOrphans(results.Values); //PHASE 3: delete orphan observations

            UpdateFrames(places); //PHASE 4: update frames and transforms

            //PHASE 5: callback
            if (callback != null)
            {
                foreach (var res in results.Values.Where(res => res.Accepted))
                {
                    callback(res);
                }
            }

            SpewStats(results.Values, nu, startTime); //PHASE 6: collect and spew stats

            return na;
        }

        private void AddAlternateExtensions(IDictionary<string, IngestImage.Result> results)
        {
            pipeline.LogInfo("adding alternate extensions to observations");
            var obsToSave = new Dictionary<string, Observation>();
            foreach (var entry in BaseUrls)
            {
                foreach (var url in pipeline.SearchFiles(entry.Url, "*", recursive: entry.Recursive))
                {
                    if (!url.EndsWith(".LBL", StringComparison.OrdinalIgnoreCase) &&
                        !url.EndsWith(".IMG", StringComparison.OrdinalIgnoreCase) &&
                        !url.EndsWith(".VIC", StringComparison.OrdinalIgnoreCase))
                    {
                        string ext = StringHelper.GetUrlExtension(url).TrimStart('.');
                        if (!string.IsNullOrEmpty(ext))
                        {
                            string urlWithoutExt = StringHelper.StripUrlExtension(url);
                            if (results.ContainsKey(urlWithoutExt))
                            {
                                var res = results[urlWithoutExt];
                                if (res.Accepted && res.Observation != null)
                                {
                                    lock (res.Observation.AlternateExtensions)
                                    {
                                        if (res.Observation.AlternateExtensions.Add(ext))
                                        {
                                            obsToSave[res.Observation.Name] = res.Observation;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            pipeline.LogInfo("saving {0} updated observations", obsToSave.Count);
            foreach (var obs in obsToSave.Values)
            {
                obs.Save(pipeline);
            }
        }

        //NOTE this should also be synchronized with FetchData.FilterDownloads()
        private int CullObservations(IDictionary<string, IngestImage.Result> results)
        {
            Action<string> verbose = null;
            if (pipeline.Verbose)
            {
                verbose = msg => pipeline.LogInfo(msg);
            }

            var filteredUrls = results.Values.Where(res => res.Accepted).Select(res => res.Url).Distinct().ToList();

            //apply RoverObservationComparator.FilterProductIDGroups()
            int na = filteredUrls.Count;
            filteredUrls = RoverObservationComparator
                .FilterProductIDGroups(filteredUrls, mission, RoverObservationComparator.LinearVariants.Both, verbose)
                .ToList();
            pipeline.LogInfo("culled {0} -> {1} observations by product ID groups", na, filteredUrls.Count);

            //select only RoverObservations
            var filteredObs = filteredUrls
                .Select(url => results[StringHelper.StripUrlExtension(url)].Observation)
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .ToList();
            na = filteredObs.Count;

            //apply RoverObservationComparator.KeepBestRoverObservations()
            // if linear and nonlinear images are allowed this code will keep for each observation either:
            // 1) one image: the best image (regardless of linearity) if the image contents are different
            //    in higher priority compares than linearity
            // 2) two images: one of each linarity if the image contents are equivalent up to the linearity test.
            //    this condition defers the decision to a sort by systems that have a strong preference for linear
            //    or nonlinear rather than an explicit early culling here.
            var comparator = new RoverObservationComparator(mission, pipeline);
            filteredObs = comparator
                .KeepBestRoverObservations(filteredObs, RoverObservationComparator.LinearVariants.Both)
                .ToList();
            pipeline.LogInfo("culled {0} -> {1} observations by observation comparator", na, filteredObs.Count);
            na = filteredObs.Count;

            //enforce mission specific wedge and texture count limits
            var sdLists = new Dictionary<SiteDrive, SiteDriveList>();
            var idToURL = new Dictionary<RoverProductId, string>();
            foreach (var obs in filteredObs)
            {
                var id = RoverProductId.Parse(obs.Name, mission); //all ids should parse now
                idToURL[id] = obs.Url;
                var sd = obs.SiteDrive;
                if (!sdLists.ContainsKey(sd))
                {
                    sdLists[sd] = new SiteDriveList(mission, pipeline);
                }
                sdLists[sd].Add(id, obs.Url); //will be rejected if not an OPGS product ID or not XYZ or RAS
            }
            SiteDriveList.ApplyMissionLimits(sdLists, idToURL);
            var keepers = new HashSet<string>(idToURL.Keys.Select(id => id.FullId));
            filteredObs = filteredObs.Where(obs => keepers.Contains(obs.Name)).ToList();
            if (filteredObs.Count < na)
            {
                //attempt to cull orphan masks
                keepers.Clear();
                keepers.UnionWith(RoverObservationComparator.
                                  FilterProductIDGroups(filteredObs.Select(obs => obs.Name), mission,
                                                        RoverObservationComparator.LinearVariants.Both, verbose));
                filteredObs = filteredObs.Where(obs => keepers.Contains(obs.Name)).ToList();
            }
            pipeline.LogInfo("culled {0} -> {1} observations by wedge and texture limits", na, filteredObs.Count);
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

            return filteredObs.Count;
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
                pipeline.LogInfo("deleting {0} orphan or culled observations", orphans.Count);
                foreach (var orphanName in orphans)
                {
                    var obs = RoverObservation.Find(pipeline, project.Name, orphanName);
                    if (obs != null)
                    {
                        pipeline.LogVerbose("deleting orphan or culled observation {0}", orphanName);
                        obs.Delete(pipeline);
                    }
                    indices.TryRemove(orphanName, out int ignore);
                }
                //orphaned frames and transforms will be handled in UpdateFrames()
            }
        }

        private void UpdateFrames(PlacesDB places)
        {
            pipeline.LogInfo("adding new observations and transforms to frames...");

            var frameCache = new FrameCache(pipeline, project.Name);
            frameCache.Preload();

            var observationCache = new ObservationCache(pipeline, project.Name);
            observationCache.Preload(); //include both surface and orbital

            //register each observation with the frame it uses
            var framesToSave = new HashSet<string>();
            int numObs = 0;
            foreach (var observation in observationCache.GetAllObservations())
            {
                numObs++;
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
                    if (frame.ObservationNames.Count == 0 &&
                        frameCache.GetChildren(frame).Count() == 0 && //only consider leaf frames
                        frame.ParentName != null) //don't delete root frame even if it's a leaf (degenerate case)
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

            if (!frameCache.ChainPriors(pdsSiteOffsets))
            {
                pipeline.LogError("failed to chain all PDS priors");
            }

            SiteDrive? rootSiteDrive = null;

            if (numObs > 0)
            {
                rootSiteDrive = frameCache.CheckPriors(mission.GetLandingSiteDrive());
                if (!rootSiteDrive.HasValue)
                {
                    throw new Exception("incomplete priors: not all sitedrives are connected");
                }
                
                //in the common case that PlacesDB priors are available
                //the project root frame will be the landing sitedrive
                //but if there are no observations in that sitedrive in the project (also common)
                //then that frame itself will not actually be in the database yet
                //
                //another possibility is that PlacesDB priors were not available but PDS prior chaining succeeded
                //in that situation rootSiteDrive is the first site in the project
                //but that site (with drive = 0) may also not actually be in the database yet
                //
                //the terminology is a little confusing because "root frame" is always just a sentiniel
                //(it's created in InitializeAlignmentProject) and is not the same as (the frame of) rootSiteDrive
                //
                // rootFrame <- identity certain transform ------ rootSiteDrive
                // rootFrame <- nonidentity uncertain transform - otherSiteDriveA
                // rootFrame <- nonidentity uncertain transform - otherSiteDriveB
                // ...
                pipeline.LogInfo("effective root frame for project {0}: {1}", project.Name, rootSiteDrive.Value);
                EnsureRootSDFrame(frameCache, rootSiteDrive.Value);
            }

            if (!noOrbital)
            {
                EnsureOrbitalFrame(places, frameCache, rootSiteDrive);
            }

            AddOrUpdateGISMetadata(places, frameCache, framesToSave);

            pipeline.LogInfo("saving {0} updated frames", framesToSave.Count);
            foreach (var frame in framesToSave)
            {
                frameCache.GetFrame(frame).Save(pipeline);
            }
        }

        private Frame EnsureRootSDFrame(FrameCache frameCache, SiteDrive rootSiteDrive)
        {
            if (frameCache.ContainsFrame(rootSiteDrive.ToString()))
            {
                return frameCache.GetFrame(rootSiteDrive.ToString());
            }

            var source = TransformSource.Prior; 
            var identity = new UncertainRigidTransform();

            string rootName = mission.RootFrameName();
            Frame rootFrame = null;
            if (frameCache.ContainsFrame(rootName))
            {
                rootFrame = frameCache.GetFrame(rootName);
            }
            else //this should have been done in InitializeAlignmentProject...
            {
                pipeline.LogWarn("adding {0} frame", rootName);
                rootFrame = Frame.FindOrCreate(pipeline, project.Name, rootName); //saves
                var rootTransform = FrameTransform.FindOrCreate(pipeline, rootFrame, source, identity); //saves
                frameCache.Add(rootFrame);
                frameCache.Add(rootTransform);
            }

            pipeline.LogInfo("adding frame for root sitedrive {0}", rootSiteDrive);
            var rootSDFrame = Frame.Create(pipeline, project.Name, rootSiteDrive.ToString(), rootFrame); //saves
            var ft = FrameTransform.Create(pipeline, rootSDFrame, source, identity); //saves

            frameCache.Add(rootSDFrame);
            frameCache.Add(ft);

            return rootSDFrame;
        }

        private Frame EnsureOrbitalFrame(PlacesDB places, FrameCache frameCache, SiteDrive? rootSiteDrive = null)
        {
            string orbitalFrameName = project.MeshFrame;

            if (string.IsNullOrEmpty(orbitalFrameName))
            {
                orbitalFrameName = "project_root";
            }

            orbitalFrameName = orbitalFrameName.ToLower();

            //by now rootSiteDrive will either be
            //* landing site (common case) if we ingested at least one surface observation and all had full priors
            //* the earliest site of any ingested surface observation (chained PDS priors)
            //* null iff we didn't ingest any surface observations

            if (orbitalFrameName == "project_root" || orbitalFrameName == "root")
            {
                string rootSDName = rootSiteDrive.HasValue ? rootSiteDrive.Value.ToString() : null;
                if (rootSDName != null)
                {
                    pipeline.LogInfo("using root sitedrive {0} as orbital frame", rootSDName);
                    orbitalFrameName = rootSDName;
                }
                else
                {
                    throw new Exception("incomplete priors: cannot ingest orbital in project root frame");
                }
            }

            if (!SiteDrive.IsSiteDriveString(orbitalFrameName))
            {
                throw new Exception("unsupported orbital frame " + orbitalFrameName);
            }

            if (frameCache.ContainsFrame(orbitalFrameName))
            {
                orbitalFrame = frameCache.GetFrame(orbitalFrameName);
            }
            else
            {
                pipeline.LogInfo("orbital frame {0} not found", orbitalFrameName);

                var rootSDFrame = EnsureRootSDFrame(frameCache, rootSiteDrive ?? new SiteDrive(orbitalFrameName));

                if (orbitalFrameName == rootSDFrame.Name)
                {
                    pipeline.LogInfo("using project root frame {0} as orbital frame", rootSDFrame.Name);
                    orbitalFrame = rootSDFrame;
                }
                else if (places != null)
                {
                    try
                    {
                        string bestView = null;
                        Vector3 orbitalToRoot = places.GetOffset(new SiteDrive(orbitalFrameName),
                                                                 new SiteDrive(rootSDFrame.Name),
                                                                 view: v => { bestView = v; });
                        pipeline.LogInfo("got offset ({0:F3}, {1:F3}, {2:F3}) from orbital frame {3} " +
                                         "to project root {4} from PlacesDB {5}",
                                         orbitalToRoot.X, orbitalToRoot.Y, orbitalToRoot.Z,
                                         orbitalFrameName, rootSDFrame.Name, bestView);
                        var xform = new UncertainRigidTransform(Matrix.CreateTranslation(orbitalToRoot),
                                                                IngestPDSImage.PLACES_COVARIANCE);
                        var source = TransformSource.PlacesDB;
                        orbitalFrame = Frame.Create(pipeline, project.Name, orbitalFrameName); //saves
                        var ft = FrameTransform.Create(pipeline, orbitalFrame, source, xform); //saves
                        frameCache.Add(orbitalFrame);
                        frameCache.Add(ft);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("failed to add transform from orbital frame {0} to project root {1}: {2}",
                                         orbitalFrameName, rootSDFrame.Name, ex.Message);
                    }
                }
                else
                {
                    pipeline.LogWarn("cannot add transform from orbital frame {0} to project root {1} without PlacesDB",
                                     orbitalFrameName, rootSDFrame.Name);
                }
            }

            return orbitalFrame;
        }

        private void AddOrUpdateGISMetadata(PlacesDB places, FrameCache frameCache, HashSet<string> framesToSave = null)
        {
            var toSave = framesToSave ?? new HashSet<string>();
            
            //add/update GIS metadata for sitedrive frames if we have PlacesDB
            int placesDEMIndex = OrbitalConfig.Instance.DEMPlacesDBIndex;
            if (placesDEMIndex >= 0 && places != null)
            {
                var cfg = OrbitalConfig.Instance;
                var body = PlanetaryBody.GetByName(cfg.BodyName);

                string demFile = GetOrbitalAssetFile(Observation.ORBITAL_DEM_INDEX);
                GISCameraModel gisCam = null;
                if (cfg.DEMIsGeoTIFF && !string.IsNullOrEmpty(demFile) && File.Exists(demFile)) {
                    gisCam = new GISCameraModel(demFile, cfg.BodyName);
                }

                pipeline.LogInfo("adding GIS metadata for sitedrives from PlacesDB orbital({0}) for planet {1}{2}",
                                 placesDEMIndex, body.Name, gisCam != null ? $" using GeoTIFF {demFile}" : "");

                foreach (var frame in frameCache.GetAllFrames())
                {
                    if (SiteDrive.IsSiteDriveString(frame.Name))
                    {
                        var sd = new SiteDrive(frame.Name);
                        try
                        {
                            string bestView = null;
                            var ene = places.GetEastingNorthingElevation(sd, placesDEMIndex, absolute: true,
                                                                         view: v => { bestView = v; });
                            var lonLat = gisCam != null ? gisCam.EastingNorthingToLonLat(ene)
                                : body.EastingNorthingToLonLat(ene); //assumes standard parallel is equator
                            pipeline.LogInfo("site drive {0} absolute (easting, northing, elevation) = " +
                                             "({1:f3}, {2:f3}, {3:f3})m, " +
                                             "(longitude, latitude) = ({4:f7}, {5:f7})deg, source {6}",
                                             sd, ene.X, ene.Y, ene.Z, lonLat.X, lonLat.Y,
                                             (gisCam != null ? "GeoTIFF and " : "") +
                                             $"PlacesDB {bestView} orbital({placesDEMIndex})");
                            frame.EastingMeters = ene.X;
                            frame.NorthingMeters = ene.Y;
                            frame.ElevationMeters = ene.Z;
                            frame.LongitudeDegrees = lonLat.X;
                            frame.LatitudeDegrees = lonLat.Y;
                            frame.HasEastingNorthing = true;
                            frame.HasElevation = true;
                            frame.HasLonLat = true;
                            toSave.Add(frame.Name);
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogError("error getting GIS metadata for site drive {0}: {1}",
                                              frame.Name, ex.Message);
                        }
                    }
                }
                if (toSave != framesToSave)
                {
                    pipeline.LogInfo("saving {0} updated frames", toSave.Count);
                    foreach (var frame in toSave)
                    {
                        frameCache.GetFrame(frame).Save(pipeline);
                    }
                }
            }
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

        private int IngestOrbitalObservations(PlacesDB places)
        {
            if (orbitalFrame == null)
            {
                pipeline.LogError("no orbital frame, cannot ingest orbital");
                return 0;
            }

            int na = 0;
            try
            {
                //ingest DEM first to set orbitalDEMElevation
                IngestOrbitalAsset(Observation.ORBITAL_DEM_INDEX, places);
                na++;
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error ingesting orbital DEM");
            }

            try
            {
                IngestOrbitalAsset(Observation.ORBITAL_IMAGE_INDEX, places);
                na++;
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error ingesting orbital image");
            }

            return na;
        }

        protected string GetOrbitalAssetFile(int obsIndex)
        {
            var cfg = OrbitalConfig.Instance;
            bool isDEM = obsIndex == Observation.ORBITAL_DEM_INDEX;
            string filePath = isDEM ? orbitalDEM : orbitalImage;
            if (!string.IsNullOrEmpty(filePath))
            {
                if (filePath.Contains("://"))
                {
                    if (!filePath.ToLower().StartsWith("file://"))
                    {
                        throw new Exception($"only file:// URLs and paths are supported, got {filePath}");
                    }
                    filePath = filePath.Substring(7);
                }
            }
            else
            {
                filePath = isDEM ? cfg.GetDEMFile() : cfg.GetImageFile();
            }
            return filePath;
        }

        protected void IngestOrbitalAsset(int obsIndex, PlacesDB places)
        {
            int demIdx = Observation.ORBITAL_DEM_INDEX;
            int imgIdx = Observation.ORBITAL_IMAGE_INDEX;
            bool isDEM = obsIndex == demIdx;
            bool isImg = obsIndex == imgIdx;
            if (!isDEM && !isImg)
            {
                throw new Exception($"expected orbital observation index {demIdx} (DEM) or {imgIdx} (image), " +
                                    $"got {obsIndex}");
            }
            string what = isDEM ? "DEM" : "image";

            var sdName = orbitalFrame.Name;
            if (!SiteDrive.IsSiteDriveString(sdName))
            {
                throw new Exception($"orbital frame must be a site drive, got \"{sdName}\"");
            }
            var sd = new SiteDrive(sdName);

            string filePath = GetOrbitalAssetFile(obsIndex);
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new Exception($"file not found: {filePath}");
            }

            var cfg = OrbitalConfig.Instance;

            Vector2? lll = mission.GetExpectedLandingLonLat();
            bool isLanding = sd == mission.GetLandingSiteDrive() && lll.HasValue && cfg.AllowExpectedLandingLonLat;
            var landingLonLatElev = new Vector3(lll.HasValue ? lll.Value.X : double.NaN,
                                                lll.HasValue ? lll.Value.Y : double.NaN,
                                                0); //will get corrected below from DEM

            if (isLanding)
            {
                pipeline.LogInfo("orbital {0}: landing sitedrive {1} (lon, lat) = ({2:f3}, {3:f3})deg",
                                 what, sd, landingLonLatElev.X, landingLonLatElev.Y);
            }
            
            if (places == null && !isLanding)
            {
                throw new Exception("no PlacesDB");
            }

            int placesIndex = isDEM ? cfg.DEMPlacesDBIndex : cfg.ImagePlacesDBIndex;
            int placesDEMIndex = cfg.DEMPlacesDBIndex;
            if (placesDEMIndex < 0 && !isLanding)
            {
                throw new Exception("DEM PlacesDB index must be non-negative, got " + placesDEMIndex);
            }

            bool isGeoTIFF = isDEM ? cfg.DEMIsGeoTIFF : cfg.ImageIsGeoTIFF;
            bool isOrthographic = isDEM ? cfg.DEMIsOrthographic : cfg.ImageIsOrthographic;

            if (!isGeoTIFF && !isOrthographic)
            {
                throw new Exception($"must use GeoTIFF metadata or OrthographicCameraModel (or both)");
            }

            pipeline.LogInfo("orbital {0}: ingesting {1}{2}in frame {3}: {4}",
                             what, isOrthographic ? "orthographic " : "", isGeoTIFF ? "GeoTIFF " : "", sd, filePath);

            //maybe load the GeoTIFF metadata
            var gisCam = isGeoTIFF ? new GISCameraModel(filePath, cfg.BodyName) : null;
            if (gisCam != null)
            {
                pipeline.LogInfo("orbital {0}: loaded GeoTIFF metadata", what);
                gisCam.Dump(pipeline, $"orbital {what}: ");
            }

            PlanetaryBody body = PlanetaryBody.GetByName(cfg.BodyName);
            int placesENEIndex = placesIndex >= 0 ? placesIndex : placesDEMIndex;
            string bestView = null;
            Vector3 eastingNorthingElev =
                places != null ? places.GetEastingNorthingElevation(sd, placesENEIndex, absolute: true,
                                                                    view: v => { bestView = v; })
                : isLanding ? body.LonLatToEastingNorthing(landingLonLatElev)
                : new Vector3(double.NaN, double.NaN, double.NaN);
            var eneSource =
                places != null ? $"PlacesDB {bestView} orbital({placesENEIndex})"
                : isLanding ? "expected landing (lon, lat)" : null;

            double nominalMPP = isDEM ? cfg.DEMMetersPerPixel : cfg.ImageMetersPerPixel;
            Vector2 effectiveMPP = nominalMPP * Vector2.One;
            Vector2 sdLonLat = body.EastingNorthingToLonLat(eastingNorthingElev).XY();
            Vector2 sdPixel = new Vector2(double.NaN, double.NaN);
            
            if (gisCam != null)
            {
                if (nominalMPP <= 0)
                {
                    nominalMPP = gisCam.AvgMetersPerPixel;
                    effectiveMPP = nominalMPP * Vector2.One;
                }
                else if (Math.Abs(gisCam.AvgMetersPerPixel - nominalMPP) > 1e-3)
                {
                    throw new Exception
                        ($"expected {nominalMPP:f3} meters per pixel, got {gisCam.AvgMetersPerPixel:f3} from GeoTIFF");
                }

                if (isLanding)
                {
                    eastingNorthingElev = gisCam.LonLatToEastingNorthing(landingLonLatElev);
                }

                sdLonLat = gisCam.EastingNorthingToLonLat(eastingNorthingElev).XY();
                
                sdPixel = gisCam.EastingNorthingToImage(eastingNorthingElev).XY();

                var px = gisCam.LonLatToImage(sdLonLat);
                if (Vector2.Distance(px, sdPixel) > 1)
                {
                    throw new Exception
                        ($"site drive {sd} pixel (x, y) = ({sdPixel.X:f3}, {sdPixel.Y:f3}) from GeoTIFF != " +
                         $"({px.X:f3}, {px.Y:f3}) at (lon, lat) = ({sdLonLat.X:f7}, {sdLonLat.Y:f7})deg");
                }
                        
            }
            else if (nominalMPP <= 0 || placesIndex < 0)
            {
                throw new Exception($"must use GeoTIFF or have configured meters per pixel and PlacesDB index");
            }

            void variance(Exception ex) {
                if (isDEM ? cfg.EnforceDEMPlacesDBMetadata : cfg.EnforceImagePlacesDBMetadata)
                {
                    throw ex;
                }
                else
                {
                    pipeline.LogWarn($"orbital {what}: " + ex.Message);
                }
            }

            try
            {
                if (placesIndex < 0)
                {
                    variance(new Exception("invalid PlacesDB index " + placesIndex));
                }
                else
                {
                    pipeline.LogInfo("orbital {0}: using PlacesDB metadata at index {1}", what, placesIndex);
                    
                    Vector2? ulcEastingNorthing = gisCam != null ? gisCam.ULCEastingNorthing : (Vector2?)null;
                    string fileName = Path.GetFileName(filePath);
                    
                    var variances =
                        isDEM ? places.CheckOrbitalDEMMetadata(placesIndex, nominalMPP, ulcEastingNorthing, fileName)
                        : places.CheckOrbitalImageMetadata(placesIndex, nominalMPP, ulcEastingNorthing, fileName);
                    
                    if (variances != null && variances.Length > 0)
                    {
                        variance(new Exception
                                 ($"PlacesDB orbital({placesIndex}) variances: " + string.Join("; ", variances)));
                    }

                    bestView = null;
                    if (gisCam == null)
                    {
                        sdPixel = places.GetOrbitalPixel(sd, placesIndex, nominalMPP, view: v => { bestView = v; });
                    }
                    else
                    {
                        var px = places.GetOrbitalPixel(sd, placesIndex, nominalMPP, gisCam.ULCEastingNorthing);
                        if (Vector2.Distance(px, sdPixel) > 1)
                        {
                            variance(new Exception
                                     ($"site drive {sd} pixel (x, y) = ({px.X:f3}, {px.Y:f3}) from PlacesDB " +
                                      $"{bestView} orbital({placesIndex}) != ({sdPixel.X:f3}, {sdPixel.Y:f3}) " +
                                      "from GeoTIFF"));
                        }
                        
                        var gisULC = gisCam.ULCEastingNorthing;
                        var placesULC = places.GetULCEastingNorthing(placesIndex);
                        if (placesULC.HasValue && Vector2.Distance(gisULC, placesULC.Value) > 1)
                        {
                            variance(new Exception
                                     ($"ULC (easting, northing) = ({placesULC.Value.X:f3}, {placesULC.Value.Y:f3})m " +
                                      $"from PlacesDB orbital({placesIndex}) != ({gisULC.X:f3}, {gisULC.Y:f3})m " +
                                      "from GeoTIFF"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                variance(ex);
            }

            if (!sdPixel.IsFinite())
            {
                throw new Exception($"failed to get pixel for sitedrive {sd} from PlacesDB orbital({placesIndex})");
            }

            if (gisCam != null)
            {
                effectiveMPP = gisCam.CheckLocalGISImageBasisAndGetResolution(sdPixel, pipeline, $"orbital {what}: ",
                                                                              throwOnError: true);
            }

            pipeline.LogInfo("orbital {0}: site drive {1} absolute (easting, northing, elevation) = " +
                             "({2:f3}, {3:f3}, {4:f3})m, source {5}",
                             what, sd, eastingNorthingElev.X, eastingNorthingElev.Y, eastingNorthingElev.Z, eneSource);

            pipeline.LogInfo("orbital {0}: site drive {1} effective meters per pixel: ({2:f3}, {3:f3}), " +
                             "source {4}", what, sd, effectiveMPP.X, effectiveMPP.Y,
                             gisCam != null ? $"GeoTIFF and {eneSource}" : "config");

            pipeline.LogInfo("orbital {0}: site drive {1} (lon, lat) = ({2:f7}, {3:f7})deg, source {4}",
                             what, sd, sdLonLat.X, sdLonLat.Y, gisCam != null ? $"GeoTIFF and {eneSource}" : eneSource);

            pipeline.LogInfo("orbital {0}: site drive {1} pixel (x, y) = ({2:f3}, {3:f3})px, source {4}",
                             what, sd, sdPixel.X, sdPixel.Y,
                             gisCam != null ? "GeoTIFF" : $"PlacesDB orbital({placesIndex}");

            //load the asset
            Image asset = null;
            if (isDEM)
            {
                asset = isGeoTIFF ? new SparseGISElevationMap(filePath)
                    : Image.Load(filePath, ImageConverters.PassThrough);
            }
            else
            {
                asset = isGeoTIFF ? new SparseGISImage(filePath) : Image.Load(filePath);
            }
            pipeline.LogInfo("orbital {0}: loaded {1}x{2} orbital {3} as {4}",
                             what, asset.Width, asset.Height, what, asset.GetType().Name);

            //prefer the elevation at sdOriginPixel in the DEM to the value we got from eneSource
            if (isDEM)
            {
                //GetInterpolatedElevation() does not need CameraModel
                //and intentionally not using cfg.DEMMinFilter and cfg.DEMMaxFilter here
                orbitalDEMElevation = (new DEM(asset, cfg.DEMElevationScale)).GetInterpolatedElevation(sdPixel);
            }

            double sdElevation = eastingNorthingElev.Z;
            if (orbitalDEMElevation.HasValue)
            {
                if (Math.Abs(orbitalDEMElevation.Value - sdElevation) > 1e-3)
                {
                    pipeline.LogWarn("orbital {0}: using sitedrive {1} DEM elevation {2:f3}m, differs from {3} {4:f3}m",
                                     what, sd, orbitalDEMElevation.Value, eneSource, sdElevation);
                    sdElevation = orbitalDEMElevation.Value;
                }
            }
            else
            {
                pipeline.LogWarn("orbital {0}: did not get elevation for site drive {1} at pixel ({2:f3}, {3:f3}) " +
                                 "from orbital DEM, using {4:f3}m from {5}",
                                 what, sd, sdPixel.X, sdPixel.Y, sdElevation, eneSource);
            }

            ConformalCameraModel cmod = gisCam;

            if (isOrthographic) //create ortho camera model
            {
                mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevation,
                                                                out Vector3 right, out Vector3 down);

                Vector2 centerPixel = new Vector2(asset.Width - 1, asset.Height - 1) * 0.5;
            
                Vector2 sdToCenter = centerPixel - sdPixel;

                right *= effectiveMPP.X;
                down *= effectiveMPP.Y;

                //XY location in meters of center pixel
                Vector3 centerPixelInSiteDrive = sdToCenter.X * right + sdToCenter.Y * down;

                //move image plane down by sdElevation
                //so that absolute DEM elevations unproject to elevations relative to sitedrive
                Vector3 elevationOffset = -1 * sdElevation * elevation;

                cmod = new OrthographicCameraModel(centerPixelInSiteDrive + elevationOffset, elevation, right, down,
                                                   asset.Width, asset.Height);
            }
            else //finish configuring GIS camera model (isOrthographic=false implies gisCam != null)
            {
                mission.GetLocalLevelBasis(out Vector3 north, out Vector3 east, out Vector3 nadir);
                gisCam.LocalToBody = gisCam.GetLocalLevelToBodyTransform(sdPixel, north, east, nadir, sdElevation);
            }

            var originPixel = cmod.Project(Vector3.Zero);

            pipeline.LogInfo("orbital {0}: using {1}x{2} {3} at site drive {4}, pixel ({5:f3}, {6:f3}), " +
                             "({7:f3}, {8:f3}) meters per pixel, source {9}PlacesDB",
                             what, cmod.Width, cmod.Height, cmod.GetType().Name, sd,
                             originPixel.X, originPixel.Y, cmod.MetersPerPixel.X, cmod.MetersPerPixel.Y,
                             gisCam != null ? $"GeoTIFF and " : "");

            string obsName = "Orbital" + StringHelper.UppercaseFirst(what);
            string url = StringHelper.NormalizeUrl(Path.GetFullPath(filePath), "file://");
            Observation.Create(pipeline, orbitalFrame, obsName, url, cmod, day: 0, version: 0, index: obsIndex,
                               useForAlignment: isDEM, useForMeshing: isDEM,
                               useForTexturing: isImg && (asset.Bands > 1 || mission.AllowGrayscaleForTexturing()),
                               width: asset.Width, height: asset.Height, bands: asset.Bands,
                               bits: gisCam != null ? gisCam.Bits : isDEM ? 32 : 8);

            lock (orbitalFrame.ObservationNames)
            {
                orbitalFrame.ObservationNames.Add(obsName);
            }
            orbitalFrame.Save(pipeline);

            pipeline.LogInfo("orbital {0}: added observation {1} (index {2}) at site drive {3}: {4}",
                             what, obsName, obsIndex, sd, url);
        }
    }
}

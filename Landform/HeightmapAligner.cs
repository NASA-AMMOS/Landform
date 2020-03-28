using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using CommandLine;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    [Verb("heightmap-align", HelpText = "")]
    public class HeightmapAlignerOptions : BEVCommandOptions
    {
        [Option(HelpText = "Manually specify a base site drive to align others to. By default BaseSiteDrivePriority will be used to pick the base site drive", Default = null)]
        public string BaseSiteDrive { get; set; }

        [Option(HelpText = "Base site drive chosen by highest priority (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst) unless set manually with BaseSiteDrive. Remaining sorted by RemainingSiteDrivePriority", Default = SiteDrivePriority.BiggestFirst)]
        public SiteDrivePriority BaseSiteDrivePriority { get; set; }

        [Option(HelpText = "Align remaining site drives to base site drive in order of priority (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst)", Default = SiteDrivePriority.BiggestFirst)]
        public SiteDrivePriority RemainingSiteDrivePriority { get; set; }

        [Option(Required = false, Default = true, HelpText = "Only allow vertical adjustment and out of plane rotation between sitedrives (does not apply to orbital).")]
        public bool PreserveXY { get; set; }

        [Option(Required = false, Default = DEM.DEF_MIN_FILTER, HelpText = "Orbital DEM values less than this will be ignored")]
        public double DEMMinFilter { get; set; }

        [Option(Required = false, Default = DEM.DEF_MAX_FILTER, HelpText = "Orbital DEM larger than this will be ignored")]
        public double DEMMaxFilter { get; set; }

        [Option(Required = false, Default = 500, HelpText = "Orbital DEM will be ignored beyond this radius from base site drive origin")]
        public double DEMRadiusFilter { get; set; }

        [Option(Required = false, Default = 16, HelpText = "Number of ICP stages")]
        public int NumICPStages { get; set; }

        [Option(Required = false, Default = 0, HelpText = "Number of simulated annealing stages")]
        public int NumAnnealingStages { get; set; }

        [Option(Required = false, Default = 0.001f, HelpText = "Run alignment stages until reaching this RMS error (meters)")]
        public float ErrorThreshold { get; set; }

        [Option(Required = false, Default = 100, HelpText = "Minimum samples required for alignment")]
        public float MinSamples { get; set; }

        [Option(Required = false, Default = 1000, HelpText = "Maximum number of samples for alignment")]
        public int MaxSamples { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital DEM file path")]
        public string OrbitalDEM { get; set; }

        [Option(Required = false, Default = false, HelpText = "Disable final alignment of sitedrives to aligned obital DEM")]
        public bool NoAlignToDem { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't only align unaligned sitedrives to aligned obital DEM")]
        public bool NoOnlyAlignUnalignedToDem { get; set; }

        [Option(HelpText = "Disable orbital alignment", Default = false, Required = false)]
        public bool NoOrbital { get; set; }

        [Option(HelpText = "Stop after rendering site drive DEMs", Default = false)]
        public bool OnlyRenderDEMs { get; set; }
    }

    public class HeightmapAligner : BEVCommand
    {
        public const int MAX_SD_MESH_DEM_RESOLUTION = 1000;
        public const double MAX_MESH_RADIUS_METERS = 200;

        private HeightmapAlignerOptions options;

        private const string OUT_DIR = "alignment/HeightmapProducts";

        private SiteDrive baseSiteDrive;

        //sitedrive => DEM
        //populated by LoadOrRenderDEMs()
        protected Dictionary<SiteDrive, DEM> sdDEMs = new Dictionary<SiteDrive, DEM>();

        // adjustedSiteDriveToWorld = BestTransform(siteDrive) * sdAdjustment[siteDrive]
        private Dictionary<SiteDrive, Matrix> sdAdjustment = new Dictionary<SiteDrive, Matrix>();

        private DEM orbitalDEM;

        //orbitalToWorld = BestTransform(baseSiteDrive) * orbitalAdjustment
        private Matrix? orbitalAdjustment;

        public HeightmapAligner(HeightmapAlignerOptions options) : base(options)
        {
            this.options = options;
        }

        /// <summary>
        /// Create a mesh from input dem with parameters given by command line args
        /// </summary>
        /// <returns></returns>
        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                if (!options.NoOrbital)
                {
                    RunPhase("load oribital DEM", LoadOrbital); //may overwrite options.NoOrbital
                }

                if (options.NoOrbital && siteDrives.Length == 1 && !options.OnlyRenderDEMs)
                {
                    pipeline.LogWarn("at least two site drives required without orbital");
                    StopStopwatch();
                    return 0;
                }

                RunPhase("load or render site drive DEMs", LoadOrRenderDEMs);

                if (options.OnlyRenderDEMs)
                {
                    if (options.WriteDebug)
                    {
                        RunPhase("save DEM meshes", SaveDEMMeshes);
                    }
                    StopStopwatch();
                    return 0;
                }

                RunPhase("sort site drives", SortSiteDrives);

                if (options.NoOrbital && sdDEMs.Keys.Where(sd => sd != baseSiteDrive).Count() == 0)
                {
                    pipeline.LogWarn("at least two site drives required without orbital");
                    StopStopwatch();
                    return 0;
                }
                    
                RunPhase("align other site drives to base site drive", AlignSurfaceToBaseSiteDrive);

                if (!options.NoOrbital)
                {
                    RunPhase("align orbital to successfully aligned site drives", AlignOrbitalToSurface);
                }

                if (!options.NoAlignToDem && orbitalAdjustment.HasValue)
                {
                    RunPhase("align site drives to orbital", AlignSurfaceToOrbital);
                }

                if (options.WriteDebug)
                {
                    RunPhase("save DEM meshes", SaveDEMMeshes);
                }

                if (!options.NoSave)
                {
                    RunPhase("save surface transforms", WriteSurfaceTransforms);
                    RunPhase("save orbital transform", WriteOrbitalTransform);
                }
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
            if (!ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }
            
            if (siteDrives.Length < 1)
            {
                throw new Exception("at least one site drive required");
            }
            
            if (!string.IsNullOrEmpty(options.BaseSiteDrive))
            {
                baseSiteDrive = new SiteDrive(options.BaseSiteDrive); //allow either SSSDDDD or SSSSSDDDDD
                if (!siteDrives.Contains(baseSiteDrive))
                {
                    throw new Exception("specified base site drive not found: " + options.BaseSiteDrive);
                }
                pipeline.LogInfo("base site drive: {0}", baseSiteDrive);
            }
            
            return true;
        }

        protected override HashSet<TransformSource> GetDefaultExcludedAdjustedTransformSources()
        {
            return new HashSet<TransformSource>() { TransformSource.LandformHeightmap };
        }

        protected override bool AutoUseMeshRDRs()
        {
            return true;
        }

        protected override void MakeCollectOpts()
        {
            MakeCollectOpts(requireNormals: false, requireTextures: false);
        }

        protected override void MakeMeshOpts()
        {
            MakeMeshOpts(applyTexture: false);
        }

        private void SortSiteDrives()
        {
            IEnumerable<SiteDrive> sort(IEnumerable<SiteDrive> sds, SiteDrivePriority priority)
            {
                switch (priority)
                {
                    case SiteDrivePriority.NewestFirst: return sds.OrderByDescending(sd => sd.ToString());
                    case SiteDrivePriority.OldestFirst: return sds.OrderBy(sd => sd.ToString());
                    case SiteDrivePriority.BiggestFirst: return sds.OrderByDescending(sd => dems[sd].Area);
                    case SiteDrivePriority.SmallestFirst: return sds.OrderBy(sd => dems[sd].Area);
                    default: throw new Exception("unknown site drive priority: " + priority);
                }
            }

            if (string.IsNullOrEmpty(options.BaseSiteDrive))
            {
                baseSiteDrive = sort(siteDrives, options.BaseSiteDrivePriority).First();
                pipeline.LogInfo("base site drive ({0}): {1}", options.BaseSiteDrivePriority, baseSiteDrive);
            }

            if (!sdDEMs.ContainsKey(baseSiteDrive))
            {
                throw new Exception("no DEM for base site drive " + baseSiteDrive);
            }

            siteDrives = new List<SiteDrive> { baseSiteDrive }
                .Concat(sort(siteDrives.Where(sd => sd != baseSiteDrive), options.RemainingSiteDrivePriority))
                .ToArray();
        }

        protected void LoadOrRenderDEMs()
        {
            LoadOrRenderBEVs(includeBEVs: false, includeDEMs: true);

            //make OrthographicCameraModel that projects points in sitedrive frame to pixels in sitedrive DEM image
            var upDir = new Vector3(0, 0, -1); //sitedrive +Z is opposite of elevation
            var rightDir = new Vector3(1, 0, 0);
            var downDir = new Vector3(0, 1, 0);

            double elevationScale = 1; //sitedrive DEM elevations are always in meters
            double originElevation = 0; //sitredrive DEM elevations are always relative to sitedrive origin

            foreach (var siteDrive in dems.Keys)
            {
                var img = dems[siteDrive];
                sdDEMs[siteDrive] = new DEM(dems[siteDrive], upDir, rightDir, downDir,
                                            BEVMetersPerPixel, elevationScale,
                                            sdOriginPixel[siteDrive], originElevation,
                                            options.DEMMinFilter, options.DEMMaxFilter);
            }
            foreach (var sd in siteDrives)
            {
                if (!sdDEMs.ContainsKey(sd))
                {
                    pipeline.LogWarn("failed to load or render DEM for site drive {0}", sd);
                }
            }
        }

        private DEMAligner MakeAligner(bool? preserveXY = null)
        {
            var ret = new DEMAligner()
            {
                NumICPStages = options.NumICPStages,
                NumAnnealingStages = options.NumAnnealingStages,
                PreserveXY = preserveXY.HasValue ? preserveXY.Value : options.PreserveXY,
                MaxRadiusMeters = options.DEMRadiusFilter,
                MinSamples = options.MinSamples,
                MaxSamples = options.MaxSamples,
                MinRMSError = options.ErrorThreshold,
                Info = msg => pipeline.LogInfo(msg)
            };
            if (!options.NoProgress)
            {
                ret.Progress = msg => pipeline.LogInfo(msg);
            }
            return ret;
        }

        private void AlignSurfaceToBaseSiteDrive()
        {
            sdAdjustment[baseSiteDrive] = Matrix.Identity;

            var aligner = MakeAligner();

            var scenes = new List<DEM>() { sdDEMs[baseSiteDrive] };
            var sceneToWorlds = new List<Matrix>() { BestTransform(baseSiteDrive) * sdAdjustment[baseSiteDrive] };
            for (int i = 1 /* don't attempt to align base site drive */; i < siteDrives.Length; i++)
            {
                var siteDrive = siteDrives[i];

                if (!sdDEMs.ContainsKey(siteDrive))
                {
                    pipeline.LogWarn("no DEM for site drive {0}", siteDrive);
                    continue;
                }

                pipeline.LogInfo("beginning alignment for site drive {0}", siteDrive);

                try
                {
                    if (options.WriteDebug)
                    {
                        aligner.SavePriorMatchMesh = mesh =>
                        {
                            SaveMesh(mesh, string.Format("surface-{0}_Heightmap_Prior_Matches", siteDrive));
                        };
                        aligner.SaveAdjustedMatchMesh = mesh =>
                        {
                            SaveMesh(mesh, string.Format("surface-{0}_Heightmap_Adj_Matches", siteDrive));
                        };
                    }
                    pipeline.LogInfo("attempting to align site drive {0}", siteDrive);
                    var adj =
                        aligner.AlignDEMToScene(sdDEMs[siteDrive], BestTransform(siteDrive),
                                                scenes.ToArray(), sceneToWorlds.ToArray(),
                                                out double initialRMS, out double finalRMS);
                    if (adj.HasValue)
                    {
                        pipeline.LogInfo("aligned site drive {0}: RMS error {1} -> {2}",
                                         siteDrive, initialRMS, finalRMS);
                        sdAdjustment[siteDrive] = adj.Value;
                        scenes.Add(sdDEMs[siteDrive]);
                        sceneToWorlds.Add(BestTransform(siteDrive) * sdAdjustment[siteDrive]);
                    }
                    else
                    {
                        pipeline.LogInfo("insufficient overlap to align site drive {0}", siteDrive);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, string.Format("error aligning site drive {0}", siteDrive));
                }
            }
        }

        private void LoadOrbital()
        {
            try
            {
                orbitalDEM = mission.LoadOrbital(baseSiteDrive, options.OrbitalDEM,
                                                 minFilter: options.DEMMinFilter, maxFilter: options.DEMMaxFilter,
                                                 logger: pipeline);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("failed to load orbital DEM or PlacesDB, running without orbital: {0}", ex.Message);
                options.NoOrbital = true;
            }
        }

        private void AlignOrbitalToSurface()
        {
            try
            {
                var aligned = siteDrives.Where(sd => sdAdjustment.ContainsKey(sd)).ToList();
                pipeline.LogInfo("aligning orbital DEM to {0} aligned site drives", aligned.Count);

                var aligner = MakeAligner(preserveXY: false);

                if (options.WriteDebug)
                {
                    aligner.SavePriorMatchMesh = mesh => SaveMesh(mesh, "orbital_Heightmap_Prior_Matches");
                    aligner.SaveAdjustedMatchMesh = mesh => SaveMesh(mesh, "orbital_Heightmap_Adj_Matches");
                }

                var adj = aligner.AlignDEMToScene(orbitalDEM, BestTransform(baseSiteDrive),
                                                  aligned.Select(sd => sdDEMs[sd]).ToArray(),
                                                  aligned.Select(sd => BestTransform(sd) * sdAdjustment[sd]).ToArray(),
                                                  out double initialRMS, out double finalRMS);
                if (!adj.HasValue)
                {
                    pipeline.LogWarn("failed to align orbital DEM, insufficient overlap");
                    return;
                }
                pipeline.LogInfo("aligned orbital to {0} site drives: RMS error {1} -> {2}",
                                 aligned.Count, initialRMS, finalRMS);
                orbitalAdjustment = adj;
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error aligning orbital DEM to aligned site drives");
                return;
            }
        }

        private void AlignSurfaceToOrbital()
        {
            var sds = siteDrives.Where(sd => sd != baseSiteDrive).ToArray();
            if (!options.NoOnlyAlignUnalignedToDem)
            {
                sds = siteDrives.Where(sd => !sdAdjustment.ContainsKey(sd)).ToArray();
            }

            sds = sds.Where(sd => sdDEMs.ContainsKey(sd)).ToArray();
                
            pipeline.LogInfo("aligning {0} site drives to orbital DEM", sds.Length);

            var aligner = MakeAligner(preserveXY: false);

            var orbitalToWorld = BestTransform(baseSiteDrive) * orbitalAdjustment.Value;
            foreach (SiteDrive siteDrive in sds)
            {
                try
                {
                    if (options.WriteDebug)
                    {
                        aligner.SavePriorMatchMesh = mesh =>
                        {
                            SaveMesh(mesh, string.Format("orbital-{0}_Heightmap_Prior_Matches", siteDrive));
                        };
                        aligner.SaveAdjustedMatchMesh = mesh =>
                        {
                            SaveMesh(mesh, string.Format("orbital-{0}_Heightmap_Adj_Matches", siteDrive));
                        };
                    }

                    pipeline.LogInfo("attempting to align site drive {0} to orbital DEM", siteDrive);
                    var dem = dems[siteDrive];
                    var sdToWorld = BestTransform(siteDrive);
                    if (sdAdjustment.ContainsKey(siteDrive))
                    {
                        sdToWorld = sdToWorld * sdAdjustment[siteDrive];
                    }
                    var adj = aligner.AlignDEMToScene(sdDEMs[siteDrive], BestTransform(siteDrive),
                                                      new DEM[] { orbitalDEM }, new Matrix[] { orbitalToWorld },
                                                      out double initialRMS, out double finalRMS);
                    if (adj.HasValue)
                    {
                        pipeline.LogInfo("aligned site drive {0} to orbital DEM: RMS error {1} -> {2}",
                                         siteDrive, initialRMS, finalRMS);
                        sdAdjustment[siteDrive] = adj.Value;
                    }
                    else
                    {
                        pipeline.LogInfo("insufficient overlap to align site drive {0} to orbital DEM", siteDrive);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, string.Format("error aligning site drive {0} to orbital DEM", siteDrive));
                }
            }
        }

        private void SaveDEMMeshes()
        {
            foreach (var siteDrive in sdDEMs.Keys)
            {
                var sdDEM = sdDEMs[siteDrive]; 
                var dem = sdDEM.DecimateTo(MAX_SD_MESH_DEM_RESOLUTION);
                if (dem.Width != sdDEM.Width || dem.Height != sdDEM.Height)
                {
                    pipeline.LogInfo("decimated {0}x{1} DEM ({2} meters/pixel) for site drive {3} " +
                                     "to {4}x{5} ({6} meters/pixel) for meshing",
                                     sdDEM.Width, sdDEM.Height, sdDEM.AvgMetersPerPixel, siteDrive,
                                     dem.Width, dem.Height, dem.AvgMetersPerPixel);
                }
                pipeline.LogInfo("organized meshing {0}x{1} DEM ({2}x{3}m) for site drive {3}, max radius {4}",
                                 dem.Width, dem.Height, dem.WidthMeters, dem.HeightMeters, siteDrive,
                                 MAX_MESH_RADIUS_METERS);
                var mesh = dem.OrganizedMesh(MAX_MESH_RADIUS_METERS);
                SaveMesh(Mesh.Transformed(mesh, BestTransform(siteDrive)), siteDrive.ToString() + "_Heightmap");
                if (sdAdjustment.ContainsKey(siteDrive))
                {
                    SaveMesh(Mesh.Transformed(mesh, BestTransform(siteDrive) * sdAdjustment[siteDrive]),
                             siteDrive.ToString() + "_Heightmap_Adj");
                }
            }
            if (orbitalDEM != null)
            {
                pipeline.LogInfo("delaunay meshing {0}x{1} orbital DEM ({2} meters/pixel, {3}x{4}m), max radius {5}",
                                 orbitalDEM.Width, orbitalDEM.Height, orbitalDEM.AvgMetersPerPixel,
                                 orbitalDEM.WidthMeters, orbitalDEM.HeightMeters, MAX_MESH_RADIUS_METERS);
                var mesh = orbitalDEM.DelaunayMesh(MAX_MESH_RADIUS_METERS, reverseWinding: true);
                SaveMesh(Mesh.Transformed(mesh, BestTransform(baseSiteDrive)), "orbital_Heightmap");
                if (orbitalAdjustment.HasValue)
                {
                    SaveMesh(Mesh.Transformed(mesh, BestTransform(baseSiteDrive) * orbitalAdjustment.Value),
                             "orbital_Heightmap_Adj");
                }
            }
        }

        private void WriteSurfaceTransforms()
        {
            var source = TransformSource.LandformHeightmap;
            foreach (var siteDrive in siteDrives.Where(sd => sd != baseSiteDrive))
            {
                var frame = frameCache.GetFrame(siteDrive.ToString());
                if (sdAdjustment.ContainsKey(siteDrive))
                {
                    var ut = new UncertainRigidTransform(BestTransform(siteDrive) * sdAdjustment[siteDrive]);
                    var ft = FrameTransform.FindOrCreate(pipeline, frame, source, ut);
                    ft.Transform = ut;
                    ft.Save(pipeline);
                    bool added = false;
                    lock (frame.Transforms)
                    {
                        added = frame.Transforms.Add(source);
                    }
                    if (added)
                    {
                        frame.Save(pipeline);
                    }
                    pipeline.LogInfo("saved {0} adjusted transform for site drive {1}", source, siteDrive);
                }
                else
                {
                    pipeline.LogWarn("failed to generate {0} transform for site drive {1}", source, siteDrive);
                    bool removed = false;
                    lock (frame.Transforms)
                    {
                        removed = frame.Transforms.Remove(source);
                    }
                    if (removed)
                    {
                        frame.Save(pipeline);
                    }
                    //can't use frameCache here because it was loaded with only priors
                    //but that's OK because FrameTransform.Find() doesn't scan
                    var ft = FrameTransform.Find(pipeline, frame, source);
                    if (ft != null)
                    {
                        ft.Delete(pipeline);
                    }
                    continue;
                }
            }
        }

        private void WriteOrbitalTransform()
        {
            var frameName = OrbitalConfig.Instance.OrbitalFrameName;
            var source = TransformSource.LandformHeightmap;

            if (orbitalAdjustment.HasValue)
            {
                if (!frameCache.ContainsFrame(frameName))
                {
                    var parent = frameCache.GetFrame(baseSiteDrive.ToString()).GetParent(pipeline);
                    frameCache.Add(Frame.Create(pipeline, project.Name, frameName, parent));
                }
                var frame = frameCache.GetFrame(frameName);
                var ut = new UncertainRigidTransform(BestTransform(baseSiteDrive) * orbitalAdjustment.Value);
                var ft = FrameTransform.FindOrCreate(pipeline, frame, source, ut);
                ft.Transform = ut;
                ft.Save(pipeline);
                bool added = false;
                lock (frame.Transforms)
                {
                    added = frame.Transforms.Add(source);
                }
                if (added)
                {
                    frame.Save(pipeline);
                }
                pipeline.LogInfo("saved {0} adjusted transform for {1}", source, frameName);
            }
            else
            {
                pipeline.LogWarn("failed to generate {0} transform for {1}", source, frameName);
                if (frameCache.ContainsFrame(frameName))
                {
                    var frame = frameCache.GetFrame(frameName);
                    var ft = FrameTransform.Find(pipeline, frame, source);
                    if (ft != null)
                    {
                        ft.Delete(pipeline);
                    }
                    frame.Delete(pipeline);
                }
            }
        }
    }
}

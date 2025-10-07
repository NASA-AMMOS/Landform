using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using CommandLine;
using JPLOPS.ImageFeatures;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.AlignmentServer;

/// <summary>
/// Align sitedrive heightmaps to each other and optionally to an orbital DEM.
///
/// Typically performed after bev-align in a contextual mesh workflow, but can be omitted or run before bev-align.
///
/// Does nothing if there is only one sitedrive and no orbital DEM is available.
///
/// Sitedrive heightmaps are computed by birds-eye-view rendering from sitedrive meshes, which are loaded from mission
/// wedge mesh RDRs if available, falling back to organized meshes built from observation point clouds.  The same
/// algorithm is used to render these heightmaps as the sitedrive DEMs used by bev-align, though the options typically
/// differ.
///
/// All sitedrives are first aligned one at a time to a base sitedrive.  As each one is aligned it is added to an
/// aligned "scene" to which later ones are aligned.  It is possible that one or more sitedrives fails to align at this
/// stage, e.g. to insufficient overlap.
///
/// If an orbital DEM is available the aligned scene of sitedrives, which will always contain at least the base
/// sitedrive, is aligned to it. All sitedrives that failed to align in the first stage are then aligned to the orbital
/// DEM.
///
/// ICP (iterated closest point) is typically used to perform each alignment, but simulated annealing can optionally be
/// used as well.
///
/// Debug meshes can be optionally saved with the aligned and unaligned heightmaps and the sample point pair matches
/// used to perform the alignments.  Only the portion of the orbital DEM near the base sitedrive is saved.
///
/// Example:
///
/// Landform.exe heightmap-align windjana --basesitedrive 0311472
///
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("heightmap-align", HelpText = "")]
    public class HeightmapAlignerOptions : BEVCommandOptions
    {
        [Option(HelpText = "Manually specify a base site drive to align others to. By default BaseSiteDrivePriority will be used to pick the base site drive", Default = null)]
        public string BaseSiteDrive { get; set; }

        [Option(HelpText = "Base site drive chosen by highest priority (Newest, Oldest, Biggest, Smallest, ProjectThenBiggest) unless set manually with BaseSiteDrive. Remaining sorted by RemainingSiteDrivePriority", Default = SiteDrivePriority.ProjectThenBiggest)]
        public SiteDrivePriority BaseSiteDrivePriority { get; set; }

        [Option(HelpText = "Align remaining site drives to base site drive in order of priority (Newest, Oldest, Biggest, Smallest)", Default = SiteDrivePriority.Biggest)]
        public SiteDrivePriority RemainingSiteDrivePriority { get; set; }

        [Option(Required = false, Default = true, HelpText = "Only allow vertical adjustment and out of plane rotation between sitedrives (does not apply to orbital or ICP).")]
        public bool PreserveXY { get; set; }

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

        [Option(HelpText = "Stop after rendering site drive DEMs", Default = false)]
        public bool OnlyRenderDEMs { get; set; }

        [Option(HelpText = "Don't align site drives to each other before aligning to orbital", Default = false)]
        public override bool NoSurface { get; set; }
    }

    public class HeightmapAligner : BEVCommand
    {
        public const int MAX_SD_MESH_DEM_RESOLUTION = 1000;
        public const double MAX_MESH_RADIUS_METERS = 200;

        private HeightmapAlignerOptions options;

        private const string OUT_DIR = "alignment/HeightmapProducts";

        private SiteDrive baseSiteDrive;
        private SiteDrive[] remainingSiteDrives; //not including rootSiteDrive or baseSiteDrive

        protected Dictionary<SiteDrive, DEM> sdDEMs = new Dictionary<SiteDrive, DEM>();

        private Dictionary<SiteDrive, Matrix> sdAdjustment = new Dictionary<SiteDrive, Matrix>();

        private Matrix BestAdjustedTransform(SiteDrive siteDrive)
        {
            var xform = base.BestTransform(siteDrive);
            if (sdAdjustment.ContainsKey(siteDrive))
            {
                xform *= sdAdjustment[siteDrive];
            }
            return xform;
        }

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

                if (!options.OnlyRenderDEMs && (options.NoOrbital && siteDrives.Length < 2))
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

                //some sdDEMs may have failed to load or render
                if (options.NoOrbital && sdDEMs.Count < 2)
                {
                    pipeline.LogWarn("at least two site drives required without orbital");
                    StopStopwatch();
                    return 0;
                }

                if (!options.NoSurface)
                {
                    RunPhase("align site drives to each other", AlignSiteDrives);
                }

                if (!options.NoOrbital)
                {
                    RunPhase("align site drives to orbital", AlignToOrbital);
                }

                if (options.WriteDebug)
                {
                    RunPhase("save DEM meshes", SaveDEMMeshes);
                }

                if (!options.NoSave)
                {
                    RunPhase("save adjusted transforms", WriteAdjustedTransforms);
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

            if (!options.NoOrbital)
            {
                options.NoOrbital |= !LoadOrbitalDEM();
            }

            return true;
        }

        protected override HashSet<TransformSource> GetDefaultExcludedAdjustedTransformSources()
        {
            return new HashSet<TransformSource>() { TransformSource.LandformHeightmap };
        }

        protected override bool AutoUseMeshRDRs()
        {
            //we used to default to true here
            //but BEVAligner defaults to false
            //which meant that BEV align uses BEV meshes reconstructed from the point clouds (or range maps)
            //but heightmap align would load existing iv/obj wedge meshes if available
            //at some point it seemed like the iv/obj meshes worked a bit better for heightmap align
            //however in M20 workflows we currently don't even download the iv/obj meshes in ProcessContextual.cs
            //so let's return false here to avoid using them if they happen to already be downloaded
            //which is the case on some dev machines
            //return true;
            return false;
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

            if (string.IsNullOrEmpty(options.BaseSiteDrive))
            {
                baseSiteDrive = SortSiteDrives(siteDrives, options.BaseSiteDrivePriority).First();
                pipeline.LogInfo("using base site drive ({0}): {1}", options.BaseSiteDrivePriority, baseSiteDrive);
            }
            else
            {
                baseSiteDrive = new SiteDrive(options.BaseSiteDrive); //allow either SSSDDDD or SSSSSDDDDD
                if (!siteDrives.Contains(baseSiteDrive))
                {
                    throw new Exception("specified base site drive not found: " + options.BaseSiteDrive);
                }
                pipeline.LogInfo("using specified base site drive: {0}", baseSiteDrive);
            }

            var sdList = new List<SiteDrive>();
            sdList.Add(baseSiteDrive);

            remainingSiteDrives = siteDrives.Where(sd => !sdList.Contains(sd)).ToArray();
            sdList.AddRange(SortSiteDrives(remainingSiteDrives, options.RemainingSiteDrivePriority));

            siteDrives = sdList.ToArray();
        }

        protected void LoadOrRenderDEMs()
        {
            LoadOrRenderBEVs(includeBEVs: false, includeDEMs: true);

            //make OrthographicCameraModel that projects points in sitedrive frame to pixels in sitedrive DEM image
            mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevation, out Vector3 right, out Vector3 down);

            double elevationScale = 1; //sitedrive DEM elevations are always in meters
            double pixelAspect = 1; //sitedrive DEMs are always square pixels
            double originElevation = 0; //sitredrive DEM elevations are always relative to sitedrive origin

            foreach (var siteDrive in dems.Keys)
            {
                var img = dems[siteDrive];
                sdDEMs[siteDrive] = DEM.OrthoDEM(dems[siteDrive], elevation, right, down,
                                                 BEVMetersPerPixel, pixelAspect, elevationScale,
                                                 sdOriginPixel[siteDrive], originElevation);
            }

            foreach (var sd in siteDrives)
            {
                if (!sdDEMs.ContainsKey(sd))
                {
                    pipeline.LogWarn("failed to load or render DEM for site drive {0}", sd);
                }
            }

            siteDrives = siteDrives.Where(sd => sdDEMs.ContainsKey(sd)).ToArray();

            if (siteDrives.Length < 1)
            {
                throw new Exception("at least one site drive DEM required");
            }

            SortSiteDrives(); //also sets baseSiteDrive
        }

        private void SaveMatchMesh(Vector3[] modelPts, Vector3[] dataPts, string name)
        {
            SaveMesh(FeatureMatch.MakeMatchMesh(modelPts, dataPts), name);
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

        private void AlignSiteDrives()
        {
            if (remainingSiteDrives.Length > 0)
            {
                pipeline.LogInfo("aligning {0} site drives to base site drive {1}",
                                 remainingSiteDrives.Length, baseSiteDrive);
                IncrementalAlign(baseSiteDrive, remainingSiteDrives, cumulative: true);
            }
        }

        private void AlignToOrbital()
        {
            var aligned = siteDrives.Where(sd => sd == baseSiteDrive || sdAdjustment.ContainsKey(sd)).ToArray();
            pipeline.LogInfo("alining base site drive {0} and {1} additional aligned site drives to orbital DEM",
                             baseSiteDrive, aligned.Length - 1);

            var aligner = MakeAligner();

            if (options.WriteDebug)
            {
                //naming convention is <model>-<data>
                var bn = "orbital-surface_Heightmap_";
                aligner.SavePriorMatchMesh =
                    (modelPts, dataPts) => SaveMatchMesh(modelPts, dataPts, bn + "Prior_Matches");
                aligner.SaveAdjustedMatchMesh =
                    (modelPts, dataPts) => SaveMatchMesh(modelPts, dataPts, bn + "Adj_Matches");
            }

            //we actually align the orbital DEM to the surface
            //but this is just because the DEMAligner.AlignDEMToScene() API is assymetric
            //it can only align one DEM to a "scene" consisting of one or more DEMs
            //it *shouldn't* matter, we just call it this way and use the inverse of the result
            var adj = aligner.AlignDEMToScene(orbitalDEM, orbitalDEMToRoot,
                                              aligned.Select(sd => sdDEMs[sd]).ToArray(),
                                              aligned.Select(sd => BestAdjustedTransform(sd)).ToArray(),
                                              out double initialRMS, out double finalRMS);
            if (adj.HasValue)
            {
                pipeline.LogInfo("aligned surface to orbital DEM: RMS error {0} -> {1}", initialRMS, finalRMS);
                var invAdj = Matrix.Invert(adj.Value);
                foreach (var sd in aligned)
                {
                    sdAdjustment[sd] = (sdAdjustment.ContainsKey(sd) ? sdAdjustment[sd] : Matrix.Identity) * invAdj;
                }
            }
            else
            {
                pipeline.LogInfo("insufficient overlap align surface to orbital DEM");
            }

            var unaligned = siteDrives.Where(sd => sd != baseSiteDrive && !sdAdjustment.ContainsKey(sd)).ToArray();

            if (unaligned.Length > 0)
            {
                pipeline.LogInfo("aligning {0} unaligned site drives to orbital DEM", unaligned.Length);
                IncrementalAlign(orbitalDEM, orbitalDEMToRoot, "orbital", unaligned, cumulative: false);
            }
        }

        private void IncrementalAlign(DEM first, Matrix firstToRoot, string firstName, IEnumerable<SiteDrive> rest,
                                      bool cumulative)
        {
            var aligner = MakeAligner();
            var dems = new List<DEM>() { first };
            var demsToRoot = new List<Matrix>() { firstToRoot };
            var aligned = firstName;
            foreach (var sd in rest)
            {
                pipeline.LogInfo("aligning site drive {0} to {1}", sd, aligned);
                try
                {
                    if (options.WriteDebug)
                    {
                        //naming convention is <model>-<data>
                        var bn = $"{firstName}-{sd}_Heightmap_";
                        aligner.SavePriorMatchMesh =
                            (modelPts, dataPts) => SaveMatchMesh(modelPts, dataPts, bn + "Prior_Matches");
                        aligner.SaveAdjustedMatchMesh =
                            (modelPts, dataPts) => SaveMatchMesh(modelPts, dataPts, bn + "Adj_Matches");
                    }
                    var adj = aligner.AlignDEMToScene(sdDEMs[sd], BestAdjustedTransform(sd),
                                                      dems.ToArray(), demsToRoot.ToArray(),
                                                      out double initialRMS, out double finalRMS);
                    if (adj.HasValue)
                    {
                        pipeline.LogInfo("aligned site drive {0} to {1}: RMS error {2} -> {3}",
                                         sd, aligned, initialRMS, finalRMS);
                        if (!sdAdjustment.ContainsKey(sd))
                        {
                            sdAdjustment[sd] = adj.Value;
                        }
                        else
                        {
                            sdAdjustment[sd] = sdAdjustment[sd] * adj.Value;
                        }
                        if (cumulative)
                        {
                            dems.Add(sdDEMs[sd]);
                            demsToRoot.Add(BestAdjustedTransform(sd));
                            aligned += $",{sd.ToString()}";
                        }
                    }
                    else
                    {
                        pipeline.LogInfo("insufficient overlap to align site drive {0} to {1}", sd, aligned);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, string.Format("error aligning site drive {0} to {1}", sd, aligned));
                }
            }
        }

        private void IncrementalAlign(SiteDrive first, IEnumerable<SiteDrive> rest, bool cumulative)
        {
            IncrementalAlign(sdDEMs[first], BestAdjustedTransform(first), first.ToString(), rest, cumulative);
        }

        private void SaveDEMMeshes()
        {
            foreach (var sd in sdDEMs.Keys)
            {
                var sdDEM = sdDEMs[sd]; 
                var dem = sdDEM.DecimateTo(MAX_SD_MESH_DEM_RESOLUTION);
                if (dem.Width != sdDEM.Width || dem.Height != sdDEM.Height)
                {
                    pipeline.LogInfo("decimated {0}x{1} DEM ({2} meters/pixel) for site drive {3} " +
                                     "to {4}x{5} ({6} meters/pixel) for meshing",
                                     sdDEM.Width, sdDEM.Height, sdDEM.AvgMetersPerPixel, sd,
                                     dem.Width, dem.Height, dem.AvgMetersPerPixel);
                }
                pipeline.LogInfo("organized meshing {0}x{1} DEM ({2}x{3}m) for site drive {3}, max radius {4}",
                                 dem.Width, dem.Height, dem.WidthMeters, dem.HeightMeters, sd,
                                 MAX_MESH_RADIUS_METERS);
                var mesh = dem.OrganizedMesh(MAX_MESH_RADIUS_METERS);
                SaveMesh(mesh.Transformed(BestTransform(sd)), sd.ToString() + "_Heightmap");
                if (sdAdjustment.ContainsKey(sd))
                {
                    SaveMesh(mesh.Transformed(BestAdjustedTransform(sd)), sd.ToString() + "_Heightmap_Adj");
                }
            }
            if (orbitalDEM != null)
            {
                pipeline.LogInfo("organized meshing {0}x{1} orbital DEM ({2} meters/pixel, {3}x{4}m), max radius {5}",
                                 orbitalDEM.Width, orbitalDEM.Height, orbitalDEM.AvgMetersPerPixel,
                                 orbitalDEM.WidthMeters, orbitalDEM.HeightMeters, MAX_MESH_RADIUS_METERS);
                var baseSiteDriveToOrbital = BestTransform(baseSiteDrive) * Matrix.Invert(orbitalDEMToRoot);
                var centerPointInOrbital = Vector3.Transform(Vector3.Zero, baseSiteDriveToOrbital);
                var mesh = orbitalDEM.OrganizedMesh(MAX_MESH_RADIUS_METERS, centerPointInOrbital);
                SaveMesh(mesh.Transformed(orbitalDEMToRoot), "orbital_Heightmap");
            }
        }

        private void WriteAdjustedTransforms()
        {
            var aligned = siteDrives.Where(sd => sdAdjustment.ContainsKey(sd)).ToArray();
            SaveTransforms(aligned, aligned.Select(sd => BestAdjustedTransform(sd)).ToArray(),
                           TransformSource.LandformHeightmap);
        }
    }
}

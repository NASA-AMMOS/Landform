using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Xna.Framework;
using CommandLine;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
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

        [Option(Required = false, Default = 1, HelpText = "Scaler to convert dem values to verticle meters.")]
        public float VerticalScale { get; set; }

        [Option(Required = false, Default = true, HelpText = "If true, only allow rotation/vertical adjustment between scenes unless aligned to Dem.")]
        public bool PreserveXY { get; set; }

        [Option(Required = false, Default = -1000000, HelpText = "Dem values less than this will be ignored")]
        public float DEMMinFilter { get; set; }

        [Option(Required = false, Default = 1000000, HelpText = "Dem values larger than this will be ignored")]
        public float DEMMaxFilter { get; set; }

        [Option(Required = false, Default = 16, HelpText = "Number of annealing stages to run per alignment operation")]
        public int NumAnnealingStages { get; set; }

        [Option(Required = false, Default = 0.25f, HelpText = "The minimum sample percentage overlap between site drives required to run alignment. (Align to orbital if all site drive options fail)")]
        public float MinOverlapPercent { get; set; }

        [Option(Required = false, Default = 20000, HelpText = "Maximum number of samples to use when aligning SD -> SD")]
        public int TargetSampleNum { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital DEM file path")]
        public string OrbitalDEM { get; set; }

        [Option(Required = false, Default = false, HelpText = "Turn on alignment to dem if no sufficient overlap to another sitedrive")]
        public bool AlignToDem { get; set; }

        [Option(HelpText = "Disable orbital alignment.", Default = false, Required = false)]
        public bool NoOrbital { get; set; }
    }

    public class HeightmapAligner : BEVCommand
    {
        private HeightmapAlignerOptions options;

        private const string OUT_DIR = "alignment/HeightmapProducts";

        private Dictionary<SiteDrive, TransformSource> siteDrivePriorSources = new Dictionary<SiteDrive, TransformSource>();
        private Dictionary<SiteDrive, Matrix> siteDriveToWorldPreviousBestTransforms = new Dictionary<SiteDrive, Matrix>();
        private Dictionary<string, double> squaredDistances = new Dictionary<string, double>();
        private Dictionary<SiteDrive, Matrix> worldPriorToWorldTransforms = new Dictionary<SiteDrive, Matrix>();

        private SparseDEMImage dem;
        private GDALDEM gdalDEM;
        private Matrix demToWorld;

        private SiteDrive baseSiteDrive;
        private Vector2 baseSiteDriveLatLon;
        private Matrix baseSiteDriveToWorld;

        private List<SiteDrive> aligned = new List<SiteDrive>();
        private List<SiteDrive> unaligned = new List<SiteDrive>();

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

                RunPhase("load or render site drive DEMs", LoadOrRenderBEVs);
                RunPhase("sort site drives", SortSiteDrives);
                RunPhase("load prior transforms", LoadPriorTransforms);
                RunPhase("compute pairwise distances between site drive centers", ComputePairwiseDistances);
                RunPhase("per site drive alignment to base site drive", AlignSurfaceToBaseSiteDrive);

                if (!options.NoOrbital)
                {
                    RunPhase("load oribital dem", LoadOrbital);
                }

                if (!options.NoOrbital) //May be overwritten if LoadOrbital fails
                {
                    RunPhase("align orbital to successfully aligned sitedrives", AlignOrbital);
                    //Alignment to dem off by enough that it's generally better to leave out failed sitedrives
                    if (options.AlignToDem)
                    {
                        RunPhase("align remaining sitedrives to orbital", AlignRemainingSiteDrivesToOrbital);
                    }
                }

                RunPhase("save surface transforms", WriteSurfaceTransforms);
                if (!options.NoOrbital)
                {
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
            
            siteDrives = new List<SiteDrive> { baseSiteDrive }
                .Concat(sort(siteDrives.Where(sd => sd != baseSiteDrive), options.RemainingSiteDrivePriority))
                .ToArray();
        }

        protected override void LoadOrRenderBEVs()
        {
            LoadOrRenderBEVs(includeBEVs: false, includeDEMs: true);
            foreach (Image img in dems.Values)
            {
                img.CameraModel = new OrthographicCameraModel(Matrix.Identity, img.Width, img.Height, MetersPerPixel);
            }
        }

        private Matrix CreateBEVToWorldMatrix(SiteDrive siteDrive)
        {
            //Computes transform from unprojected (orthographic) bev to root frame
            //First convert to site drive frame:
            //  Flip Y axis as ortho projects y-up, bevs are rendered y-down
            //  Offset to center on site drive computed in pixel space, then scaled to meters:
            //  1. Compute pixel corresponding to site drive origin in bev. 
            //     This logic is the same as BevAligner.PointToPixel() but includes z offset.
            //     Note that bevs are rendered using priors, we use the same here 
            //  2. Points are unprojected relative to image center, so offset by width/2, height/2
            //Then convert to root frame using best available transform

            Matrix flipY = new Matrix(1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

            var sdToRootPrior = frameCache.GetBestPrior(siteDrive.ToString()).Transform.Mean;
            var zOff = Vector3.Transform(new Vector3(0, 0, 0), sdToRootPrior).Z;

            var sdDem = dems[siteDrive];
            var sdOriginPixel = this.sdOriginPixel[siteDrive];
            var offsetCorrection = Matrix.CreateTranslation((sdDem.Width / 2.0 - sdOriginPixel.X) * MetersPerPixel,
                                                            (sdDem.Height / 2.0 - sdOriginPixel.Y) * MetersPerPixel,
                                                            -zOff);

            var sdToRoot = frameCache.GetBestTransform(siteDrive.ToString()).Transform.Mean;

            return flipY* offsetCorrection * sdToRoot;
        }

        private void LoadPriorTransforms()
        {
            foreach (SiteDrive siteDrive in siteDrives)
            {
                var rec = frameCache.GetBestTransform(siteDrive.ToString());
                siteDriveToWorldPreviousBestTransforms[siteDrive] = rec.Transform.Mean;
                siteDrivePriorSources[siteDrive] = rec.Source;
                pipeline.LogInfo("loaded {0} transform for site drive {1}", rec.Source, siteDrive);
            }

            baseSiteDriveToWorld = siteDriveToWorldPreviousBestTransforms[baseSiteDrive];
        }

        private void ComputePairwiseDistances()
        {
            //Compute pairwise distances between site drive centers
            foreach (SiteDrive sd1 in siteDrives)
            {
                var p1 = rootOriginPixel[sd1];
                foreach (SiteDrive sd2 in siteDrives)
                {
                    var p2 = rootOriginPixel[sd2];
                    squaredDistances[sd1.ToString() + sd2.ToString()] = Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2);
                }
            }
        }

        private void AlignSurfaceToBaseSiteDrive()
        {
            for (int i = 1; i < siteDrives.Length; i++)
            {
                var siteDrive = siteDrives[i];

                pipeline.LogInfo("beginning alignment for site drive: {0}", siteDrive.ToString());
                var image = dems[siteDrive];

                //Align to the highest priority sitedrive with sufficient overlap
                bool success = false;
                Matrix adjustedSiteDriveToWorld = Matrix.Identity;

                //Try to align to closest site drives first
                foreach (SiteDrive otherSiteDrive in siteDrives.OrderBy(sd => squaredDistances[siteDrive.ToString() + sd.ToString()]))
                {
                    //Only align unalinged to aligned
                    if (!aligned.Contains(otherSiteDrive))
                    {
                        continue;
                    }

                    pipeline.LogInfo("attempting alignment from site drive {0} to site drive {1}", siteDrive, otherSiteDrive);

                    var otherImg = dems[otherSiteDrive];

                    Matrix sceneToWorld = CreateBEVToWorldMatrix(siteDrive);
                    Matrix otherSceneToWorld = CreateBEVToWorldMatrix(otherSiteDrive);

                    var temp = DemOperations.AlignSceneToDem(image, sceneToWorld, otherImg, otherSceneToWorld, options.PreserveXY,
                        options.NumAnnealingStages, null, options.MinOverlapPercent, options.DEMMinFilter, options.DEMMaxFilter, options.TargetSampleNum);
                    if (!temp.HasValue)
                    {
                        continue;
                    }
                    Matrix adjustment = temp.Value;
                    //Align current site drive's world prior to some other site drives prior, then chain that site drive's transform.
                    worldPriorToWorldTransforms[siteDrive] = Matrix.Invert(adjustment) * worldPriorToWorldTransforms[otherSiteDrive];
                    pipeline.LogInfo("aligned site drive {0} to site drive {1}", siteDrive, otherSiteDrive);
                    success = true;
                    aligned.Add(siteDrive);
                    break;
                }

                if (!success)
                {
                    pipeline.LogInfo("no sufficient overlap with any aligned sitedrive");
                    unaligned.Add(siteDrive);
                }
            }
        }

        private void LoadOrbital()
        {
            try
            {
                var cfg = OrbitalConfig.Instance;

                string demFilePath = options.OrbitalDEM;
                if (string.IsNullOrEmpty(demFilePath) && !string.IsNullOrEmpty(cfg.OrbitalDEMStoragePath))
                {
                    demFilePath = Path.Combine(LocalPipelineConfig.Instance.StorageDir, cfg.OrbitalDEMStoragePath);
                }
                else
                {
                    throw new Exception("orbital DEM not available for mission " + mission.GetMission());
                }

                if (!File.Exists(demFilePath))
                {
                    throw new Exception(string.Format("mission {0} orbital DEM not found at {1}",
                                                      mission.GetMission(), demFilePath));
                }

                dem = new SparseDEMImage(demFilePath);
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height,
                                                              cfg.OrbitalDEMMetersPerPixel);

                gdalDEM = GDALDEM.MarsDEM(demFilePath);

                var placesDB = new PlacesDB(pipeline, requireOrbital: true);
                baseSiteDriveLatLon = placesDB.GetEstimatedLatLon(baseSiteDrive);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("failed to load orbital DEM or PlacesDB, running without orbital: {0}", ex.Message);
                options.NoOrbital = true;
            }
        }

        private void AlignOrbital()
        {
            var cfg = OrbitalConfig.Instance;

            var demToBaseSiteDrive = DemOperations.GetOrbitalDEMToSiteDriveTransform(baseSiteDriveLatLon, gdalDEM,
                                                                                     cfg.OrbitalDEMMetersPerPixel);

            var alignedImages = aligned.Select(sd => dems[sd]).ToArray();
            Matrix[] bevsToWorld = aligned.Select(sd => CreateBEVToWorldMatrix(sd) * worldPriorToWorldTransforms[sd]).ToArray();

            //First do naive vertical alignment in base site drive since transform to world may have slight rotation (not commutative)
            Matrix zCorrectedPrior = demToBaseSiteDrive *
                DemOperations.AlignSceneToDem(alignedImages[0], bevsToWorld[0] * Matrix.Invert(baseSiteDriveToWorld), dem, demToBaseSiteDrive,
                false, 0, null, 0, options.DEMMinFilter, options.DEMMaxFilter, options.TargetSampleNum).Value;

            //Run alignment for dem in world frame
            Matrix demToWorldPrior = zCorrectedPrior * frameCache.GetBestTransform(baseSiteDrive.ToString()).Transform.Mean;
            Matrix demWorldPriorToWorld = DemOperations.AlignScenesToDem(alignedImages, bevsToWorld, dem, demToWorldPrior, false,
                options.NumAnnealingStages, null, 0.0, options.DEMMinFilter, options.DEMMaxFilter, options.TargetSampleNum).Value;
            demToWorld = demToWorldPrior * demWorldPriorToWorld;


            if (options.WriteDebug)
            {
                Vector2 baseSDOriginPixelInOrbitalDEM = gdalDEM.LatLonToImage(baseSiteDriveLatLon);

                //Get subset of dem around sitedrive
                int pixelRadius = (int)(200 / cfg.OrbitalDEMMetersPerPixel);
                int baseC = (int)Math.Max(baseSDOriginPixelInOrbitalDEM.X - pixelRadius, 0);
                int baseR = (int)Math.Max(baseSDOriginPixelInOrbitalDEM.Y - pixelRadius, 0);
                int pixelWidth = (int)Math.Min(baseSDOriginPixelInOrbitalDEM.X + pixelRadius, dem.Width) - baseC;
                int pixelHeight = (int)Math.Min(baseSDOriginPixelInOrbitalDEM.Y + pixelRadius, dem.Height) - baseR;

                //Create dem mesh in root site drive frame
                Mesh demPointCloud = new Mesh();
                for (int r = 0; r < pixelHeight; r++)
                {
                    for (int c = 0; c < pixelWidth; c++)
                    {
                        var pos = DemOperations.GetXYZ(dem, baseR + r, baseC + c, options.VerticalScale, true, options.DEMMinFilter, options.DEMMaxFilter);
                        if (pos.HasValue)
                        {
                            Vertex v = new Vertex();
                            v.Position = pos.Value;
                            v.UV = dem.PixelToUV(new Vector2(baseC + c, baseR + r));
                            demPointCloud.Vertices.Add(v);
                        }
                    }
                }
                Mesh demMesh = Delaunay.Triangulate(demPointCloud.Vertices);
                var sdFrame = baseSiteDrive.ToString();
                demMesh.Transform(demToWorld * Matrix.Invert(frameCache.GetBestTransform(sdFrame).Transform.Mean));
                SaveMesh(demMesh, "clippedOrbitalDEMin" + sdFrame + ".ply");
            }
        }

        private void AlignRemainingSiteDrivesToOrbital()
        {
            foreach (SiteDrive siteDrive in unaligned)
            {
                pipeline.LogInfo("aligning site drive {0} to orbital DEM", siteDrive);
                var image = dems[siteDrive];

                Matrix sdToWorldPrior = CreateBEVToWorldMatrix(siteDrive);
                Matrix demWorldToSDWorld =
                    DemOperations.AlignSceneToDem(image, sdToWorldPrior, dem, demToWorld, false,
                                                  options.NumAnnealingStages, null, 0,
                                                  options.DEMMinFilter, options.DEMMaxFilter, options.TargetSampleNum)
                    .Value;
                worldPriorToWorldTransforms[siteDrive] = Matrix.Invert(demWorldToSDWorld);
            }
        }

        private void WriteSurfaceTransforms()
        {
            foreach (SiteDrive siteDrive in siteDrives)
            {
                if (!worldPriorToWorldTransforms.ContainsKey(siteDrive))
                {
                    pipeline.LogWarn("failed to generate {0} transform for site drive {1}",
                                     TransformSource.LandformOrbital, siteDrive);
                    continue;
                }

                var adjustedSiteDriveToWorld = frameCache.GetBestTransform(siteDrive.ToString()).Transform.Mean
                                                * worldPriorToWorldTransforms[siteDrive];

                var ut = new UncertainRigidTransform(adjustedSiteDriveToWorld);
                var frame = frameCache.GetFrame(siteDrive.ToString());
                var ft = FrameTransform.FindOrCreate(pipeline, frame, TransformSource.LandformOrbital, ut);
                ft.Transform = ut;
                ft.Save(pipeline);
                bool added = false;
                lock (frame.Transforms)
                {
                    added = frame.Transforms.Add(ft.Source);
                }
                if (added)
                {
                    frame.Save(pipeline);
                }
                pipeline.LogInfo("saved {0} adjusted transform for site drive {1}",
                                 TransformSource.LandformOrbital, siteDrive);
            }
        }

        private void WriteOrbitalTransform()
        {
            string orbitalFrameName = OrbitalConfig.Instance.OrbitalFrameName;
            var demUt = new UncertainRigidTransform(demToWorld);
            var rootFrame = frameCache.GetFrame(baseSiteDrive.ToString()).GetParent(pipeline);
            if (!frameCache.ContainsFrame(orbitalFrameName))
            {
                frameCache.Add(Frame.Create(pipeline, project.Name, orbitalFrameName, rootFrame));
            }
            var orbitalFrame = frameCache.GetFrame(orbitalFrameName);
            var demFt = FrameTransform.FindOrCreate(pipeline, orbitalFrame, TransformSource.LandformOrbital, demUt);
            demFt.Transform = demUt;
            demFt.Save(pipeline);
            bool added = false;
            lock (orbitalFrame.Transforms)
            {
                added = orbitalFrame.Transforms.Add(demFt.Source);
            }
            if (added)
            {
                orbitalFrame.Save(pipeline);
            }
            pipeline.LogInfo("saved {0} adjusted transform for {1}", TransformSource.LandformOrbital, orbitalFrameName);
        }
    }
}

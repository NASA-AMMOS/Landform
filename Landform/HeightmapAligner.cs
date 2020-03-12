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
using OPS.Landform;
using log4net;
using OPS.Pipeline.AlignmentServer;
using Util;
using OPS.Imaging.Imaging;

namespace OPS.Landform
{
    [Verb("heightmap-align", HelpText = "")]
    public class HeightmapAlignerOptions : BEVCommandOptions
    {
        [Option(HelpText = "Manually specify a base site drive to align others to. By default BaseSiteDrivePriority will be used to pick the base site drive", Default = "")]
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

        [Option(Required = false, Default = null, HelpText = "Override default dem location from config")]
        public string OrbitalDEM { get; set; }

        [Option(Required = false, Default = false, HelpText = "Turn on alignment to dem if no sufficient overlap to another sitedrive")]
        public bool AlignToDem { get; set; }

        [Option(HelpText = "Debug option to write out the clipped dem in base site drive frame after alignment. Default does not write", Default = "")]
        public string WriteClippedDemToPath { get; set; }
    }

    public class HeightmapAligner : BEVCommand
    {
        private HeightmapAlignerOptions options;

        private const string OUT_DIR = "orbital/Products";

        protected new List<SiteDrive> siteDrives;

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
            this.ParseArgumentsAndLoadCaches(OUT_DIR);

            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return 1;
            }

            var wedgeOpts = new WedgeObservations.CollectOptions(options.OnlyForSiteDrives, options.OnlyForFrames,
                                                           options.OnlyForCameras, mission)
            {
                RequirePoints = true,
                RequireNormals = false,
                RequireTextures = false,
                IncludeForAlignment = true,
                IncludeForMeshing = false,
                IncludeForTexturing = false,
                RequirePriorTransform = true,
                TargetFrame = "root"
            };
            var meshObservations = WedgeObservations.Collect(frameCache, observationCache, wedgeOpts);

            siteDrives = meshObservations
                .Select(obs => obs.SiteDrive)
                .Distinct()
                .OrderByDescending(sd => sd)
                .ToList();

            LoadOrRenderBEVs();

            foreach (Image img in dems.Values)
            {
                img.CameraModel = new OrthographicCameraModel(Matrix.Identity, img.Width, img.Height, MetersPerPixel);
            }

            //Select highest priority site drive as base
            Action<SiteDrivePriority> sortSiteDrives = priority =>
            {
                switch (priority)
                {
                    case SiteDrivePriority.NewestFirst:
                        {
                            siteDrives = siteDrives.OrderByDescending(sd => sd.ToString()).ToList();
                            break;
                        }
                    case SiteDrivePriority.OldestFirst:
                        {
                            siteDrives = siteDrives.OrderBy(sd => sd.ToString()).ToList();
                            break;
                        }
                    case SiteDrivePriority.BiggestFirst:
                        {
                            siteDrives = siteDrives.OrderByDescending(sd => dems[sd].Area).ToList();
                            break;
                        }
                    case SiteDrivePriority.SmallestFirst:
                        {
                            siteDrives = siteDrives.OrderBy(sd => dems[sd].Area).ToList();
                            break;
                        }
                }
            };

            //Choose the highest priority site drive as the base for alignment
            SiteDrive baseSiteDrive;
            if (String.IsNullOrEmpty(options.BaseSiteDrive))
            {
                sortSiteDrives(options.BaseSiteDrivePriority);
                baseSiteDrive = siteDrives[0];
            } else
            {
                baseSiteDrive = new SiteDrive(options.BaseSiteDrive); //Allow either SSSDDDD or SSSSSDDDDD
            }
            siteDrives.Remove(baseSiteDrive);

            pipeline.LogInfo("Base site drive for alignment is {0}", baseSiteDrive);

            //Sort remaining by secondary priority
            sortSiteDrives(options.RemainingSiteDrivePriority);
            siteDrives.Insert(0, baseSiteDrive);

            //Site drive to world priors
            Dictionary<SiteDrive, TransformSource> siteDrivePriorSources = new Dictionary<SiteDrive, TransformSource>();
            Dictionary<SiteDrive, Matrix> siteDriveToWorldPreviousBestTransforms = new Dictionary<SiteDrive, Matrix>();
            foreach (SiteDrive siteDrive in siteDrives)
            {
                var rec = frameCache.GetBestTransform(siteDrive.ToString());
                siteDriveToWorldPreviousBestTransforms[siteDrive] = rec.Transform.Mean;
                siteDrivePriorSources[siteDrive] = rec.Source;
                pipeline.LogInfo("Read in {0} transform for site drive {1}", rec.Source, siteDrive);
            }

            Matrix baseSiteDriveToWorld = siteDriveToWorldPreviousBestTransforms[baseSiteDrive];

            //Compute pairwise distances between site drive centers
            pipeline.LogInfo("Computing pairwise distances between site drive centers");
            Dictionary<string, double> squaredDistances = new Dictionary<string, double>();
            foreach (SiteDrive sd1 in siteDrives)
            {
                var p1 = rootOriginPixel[sd1];
                foreach (SiteDrive sd2 in siteDrives)
                {
                    var p2 = rootOriginPixel[sd2]; 
                    squaredDistances[sd1.ToString() + sd2.ToString()] = Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2);
                }
            }

            //Alignments computed for each site drive in world frame 
            Dictionary<SiteDrive, Matrix> worldPriorToWorldTransforms = new Dictionary<SiteDrive, Matrix>();
            worldPriorToWorldTransforms[baseSiteDrive] = Matrix.Identity;

            List<SiteDrive> aligned = new List<SiteDrive> { baseSiteDrive };
            List<SiteDrive> unaligned = new List<SiteDrive>();
          
            Func<SiteDrive, Matrix> CreateBEVToWorldMatrix = siteDrive =>
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

                return flipY * offsetCorrection * sdToRoot;
            };

            foreach (SiteDrive siteDrive in siteDrives.GetRange(1, siteDrives.Count - 1))
            {
                pipeline.LogInfo("Beginning alignment for site drive: {0}", siteDrive.ToString());            
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

                    pipeline.LogInfo("Attempting alignment from site drive {0} to site drive {1}", siteDrive, otherSiteDrive);

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
                    pipeline.LogInfo("Aligned site drive {0} to site drive {1}", siteDrive, otherSiteDrive);
                    success = true;
                    aligned.Add(siteDrive);
                    break;
                }

                if (!success)
                {
                    pipeline.LogInfo("No sufficient overlap with any aligned sitedrive. Will align to orbital");
                    unaligned.Add(siteDrive);
                }
            }

            string demFilePath = !string.IsNullOrEmpty(options.OrbitalDEM) ? options.OrbitalDEM : //override by cmdline opt
                OrbitalConfig.Instance.GetDEMFullPath(project.Mission);

            bool runOrbitalAlign = true;
            SparseDEMImage dem = null;
            Matrix demToBaseSiteDrive = Matrix.Identity;

            ImageSerializer s = ImageSerializers.Instance.GetSerializer(Path.GetExtension(demFilePath));
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
            }

            if (File.Exists(demFilePath))
            {
                ((GDALSerializer)s).GetMetadata(demFilePath, out int bands, out int width, out int height);
                dem = new SparseDEMImage(demFilePath);
                if (dem.CameraModel == null)
                {
                    dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, mission.GetDemMetersPerPixel());
                }
                if (mission.GetDemToSiteDriveOffset(baseSiteDrive, out Matrix demToSD, demFilePath)) {

                    demToBaseSiteDrive = demToSD * DemOperations.demToSitedriveCoordinateFlip;
                } else
                {
                    pipeline.LogWarn("Failed to access places; running without orbital");
                    runOrbitalAlign = false;
                }
            }
            else
            {
                pipeline.LogWarn("Failed to load orbital DEM, running without orbital");
                runOrbitalAlign = false;
            }

            //Align dem to all aligned site drives
            if (runOrbitalAlign)
            {
                pipeline.LogInfo("Beginning alignment for DEM");

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
                Matrix demToWorld = demToWorldPrior * demWorldPriorToWorld;

                //Alignment to dem off by enough that it's better to leave out
                if (options.AlignToDem)
                {
                    //Align remaining sitedrives to dem
                    foreach (SiteDrive siteDrive in unaligned)
                    {
                        pipeline.LogInfo("Aligning site drive {0} to DEM.", siteDrive);
                        var image = dems[siteDrive];

                        Matrix sdToWorldPrior = CreateBEVToWorldMatrix(siteDrive);
                        Matrix demWorldToSDWorld = DemOperations.AlignSceneToDem(image, sdToWorldPrior, dem, demToWorld,
                            false, options.NumAnnealingStages, null, 0, options.DEMMinFilter, options.DEMMaxFilter, options.TargetSampleNum).Value;
                        worldPriorToWorldTransforms[siteDrive] = Matrix.Invert(demWorldToSDWorld);
                    }
                }

                if (!String.IsNullOrEmpty(options.WriteClippedDemToPath))
                {
                    Vector2 sdOriginPixel;
                    mission.GetSiteDriveOriginPixelInDem(baseSiteDrive, out sdOriginPixel);

                    //Get subset of dem around sitedrive
                    int pixelRadius = (int)(200 / mission.GetDemMetersPerPixel());
                    int baseC = (int)Math.Max(sdOriginPixel.X - pixelRadius, 0);
                    int baseR = (int)Math.Max(sdOriginPixel.Y - pixelRadius, 0);
                    int pixelWidth = (int)Math.Min(sdOriginPixel.X + pixelRadius, dem.Width) - baseC;
                    int pixelHeight = (int)Math.Min(sdOriginPixel.Y + pixelRadius, dem.Height) - baseR;

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
                    demMesh.Transform(demToWorld * Matrix.Invert(frameCache.GetBestTransform(baseSiteDrive.ToString()).Transform.Mean));
                    demMesh.Save(options.WriteClippedDemToPath);
                }

                string orbitalFrameName = OrbitalConfig.Instance.GetOrbitalFrameName();
                //Orbital frame
                {
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

            foreach (SiteDrive siteDrive in siteDrives)
            {
                if(!worldPriorToWorldTransforms.ContainsKey(siteDrive))
                {
                    pipeline.LogWarn("Failed to generate {0} transform for site drive {1}", TransformSource.LandformOrbital, siteDrive);
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
                pipeline.LogInfo("saved {0} adjusted transform for site drive {1}", TransformSource.LandformOrbital, siteDrive);
            }            
            return 0;
        }
    }
}

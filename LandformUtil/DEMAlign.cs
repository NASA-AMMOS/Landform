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

namespace OPS.LandformUtil
{
    [Verb("orbitalalign", HelpText = "")]
    public class OrbitalAlignerOptions : WedgeCommandOptions
    {
        [Value(1, Required = true, Default = 1, HelpText = "Size of a pixel in the DEM in meters")]
        public double MetersPerPixel { get; set; }

        [Value(2, Required = true, HelpText = "Image containing heights as values")]
        public string InputDem { get; set; }

        [Option(HelpText = "Specify a base site drive to align others to. By default BaseSiteDrivePriority will be used to pick the base site drive", Default = "")]
        public string BaseSiteDrive { get; set; }

        [Option(HelpText = "Base site drive chosen by highest priority (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst). Remaining sorted by RemainingSiteDrivePriority", Default = SiteDrivePriority.NewestFirst)]
        public SiteDrivePriority BaseSiteDrivePriority { get; set; }

        [Option(HelpText = "Align remaining site drives to base site drive by priority (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst)", Default = SiteDrivePriority.NewestFirst)]
        public SiteDrivePriority RemainingSiteDrivePriority { get; set; }

        [Option(Required = false, Default = "", HelpText = "Optionally write out transformed dem.")]
        public string DemDebugPath { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Scaler to convert dem values to verticle meters.  i.e. (meters/pixel value)")]
        public float VerticalScale { get; set; }

        [Option(Required = false, Default = true, HelpText = "If true, only allow rotation/vertical adjustment. Preferred if BEV align already run")]
        public bool PreserveXY { get; set; }

        [Option(Required = false, Default = true, HelpText = "Use cached heightmaps (if exist) for alignment. Recreate and save otherwise")]
        public bool UseCachedHeightmaps { get; set; }

        [Option(Required = false, Default = 256, HelpText = "Target resolution for intermediate scene heightmaps")]
        public int SceneHeightmapRes { get; set; }

        [Option(Required = false, Default = -1000000, HelpText = "Dem values less than this will be ignored")]
        public float DEMMinFilter { get; set; }

        [Option(Required = false, Default = 1000000, HelpText = "Dem values larger than this will be ignored")]
        public float DEMMaxFilter { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Disable generating organized mesh normals when normal image missing", Default = false)]
        public bool NoGenerateNormals { get; set; }
    }

    public class OribitalAligner : WedgeCommand
    {
        static ILog logger = LogManager.GetLogger(typeof(MultiMeshClipper));

        OrbitalAlignerOptions options;

        private const string OUT_DIR = "orbital/Products";

        //WedgeCommand.siteDrives is an array of SiteDrive corresponding to the OnlyForSiteDrives option
        //LocalBEVAligner.siteDrives is a sorted array of the sitedrives to be aligned
        protected new List<string> siteDrives;

        public OribitalAligner(OrbitalAlignerOptions options) : base(options)
        {
            this.options = options;
        }

        /// <summary>
        /// Create a mesh from input dem with parameters given by command line args
        /// </summary>
        /// <returns></returns>
        public int Run()
        {
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(Path.GetExtension(options.InputDem));
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
            }
            ((GDALSerializer)s).GetMetadata(options.InputDem, out int bands, out int width, out int height);

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
                .Select(obs => obs.SiteDrive.ToString())
                .Distinct()
                .OrderByDescending(sd => sd)
                .ToList();

            var dem = new SparseDEMImage(options.InputDem);
            if (dem.CameraModel == null)
            {
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, options.MetersPerPixel);
            }
            GDALDEM gdalDem = GDALDEM.MarsDEM(options.InputDem);

            //Select highest priority site drive as base
            Action<SiteDrivePriority> sortSiteDrives = priority =>
            {
                switch (priority)
                {
                    case SiteDrivePriority.NewestFirst:
                        {
                            siteDrives = siteDrives.OrderByDescending(sd => sd).ToList();
                            break;
                        }
                    case SiteDrivePriority.OldestFirst:
                        {
                            siteDrives = siteDrives.OrderBy(sd => sd).ToList();
                            break;
                        }
                    case SiteDrivePriority.BiggestFirst:
                        {
                            //TODO: Will want this case for minimizing reliance on dem when site drive -> site drive alignment possible
                            throw new NotImplementedException();
                        }
                    case SiteDrivePriority.SmallestFirst:
                        {
                            throw new NotImplementedException();
                        }
                }
            };

            //Choose the highest priority site drive as the base for alignment
            string baseSiteDrive = options.BaseSiteDrive;
            if (String.IsNullOrEmpty(baseSiteDrive))
            {
                sortSiteDrives(options.BaseSiteDrivePriority);
                baseSiteDrive = siteDrives[0];
            }
            siteDrives.Remove(baseSiteDrive);

            logger.InfoFormat("Base site drive for alignment is {0}", baseSiteDrive);

            //Sort remaining by secondary priority
            sortSiteDrives(options.RemainingSiteDrivePriority);
            siteDrives.Insert(0, baseSiteDrive);

            MSLPlaces places = new MSLPlaces();
            Vector2 latlon = places.GetEstimatedLatLon(new SiteDrive(baseSiteDrive));
            Vector3 colRowOffset = gdalDem.LatLonToImage(new Vector3(latlon.Y, latlon.X, 0));

            //Site drive to world priors
            Dictionary<string, FrameTransform> siteDrivePriors = new Dictionary<string, FrameTransform>();
            foreach(string siteDrive in siteDrives)
            {
                siteDrivePriors[siteDrive] = frameCache.GetBestTransform(siteDrive);
            }

            Matrix baseSiteDriveToWorld = siteDrivePriors[baseSiteDrive].Transform.Mean;

            Func<string, Mesh> BuildMesh = (siteDrive) =>
            {
                logger.Info("Building mesh for site drive : " + siteDrive);
                var siteDriveObs = meshObservations.Where(o => o.SiteDrive.ToString() == siteDrive);
                var siteDriveMeshes = siteDriveObs.Select(o =>
                {
                    var meshOpts = new WedgeObservations.MeshOptions()
                    {
                        Frame = siteDrive,
                        UsePriors = true,
                        MaxTriangleAspect = options.MaxTriangleAspect,
                        GenerateNormals = !options.NoGenerateNormals
                    };
                    meshOpts.Decimate = WedgeObservations.AutoDecimate(o.Points, -1, (int)(1024 / Math.Sqrt(siteDriveObs.Count())));
                    return o.BuildOrganizedMesh(pipeline, frameCache, masker, meshOpts);
                }).Where(m => m != null).ToArray();
                return siteDriveMeshes.Count() > 0 ? Mesh.Merge(siteDriveMeshes) : new Mesh();
            };

            Func<string, Mesh> BuildMeshInBaseSiteDrive = (siteDrive) =>
            {
                var ret = BuildMesh(siteDrive);
                ret.Transform(siteDrivePriors[siteDrive].Transform.Mean);
                ret.Transform(Matrix.Invert(baseSiteDriveToWorld));
                return ret;
            };

            //TODO: How reusable should heightmaps be? Different under rotation, but not translation
            Func<string, string, TransformSource, string> GetName = (sd, frame, source) => sd + "-" + frame + "-" + source;

            //Create a heightmap record for site drive in base site drive frame
            Func<string, SceneHeightmap> FindOrCreateHeightmap = (siteDrive) =>
            {
                SceneHeightmap rec = SceneHeightmap.Find(pipeline, project.Name, GetName(siteDrive, baseSiteDrive, siteDrivePriors[siteDrive].Source));
                if (rec != null)
                {
                    return rec;
                } else
                {
                    Mesh mesh = BuildMeshInBaseSiteDrive(siteDrive);                
                    Image img = MeshToHeightMap.BuildDem(mesh, options.SceneHeightmapRes, out double m2p, out double xOffset, out double yOffset);
                    img.Save<float>("D:\\dems\\heightmap_" + siteDrive + ".tiff");
                    return SceneHeightmap.Create(pipeline, project, GetName(siteDrive, baseSiteDrive, siteDrivePriors[siteDrive].Source), img, new Vector2(xOffset, yOffset), m2p);
                }
            };

            Func<SceneHeightmap, Image> GetImage = rec =>
            {
                var image = pipeline.GetDataProduct<TiffDataProduct>(project, rec.DEMGuid).Image;
                if (image.CameraModel == null)
                {
                    image.CameraModel = new OrthographicCameraModel(Matrix.Identity, image.Width, image.Height, rec.MetersPerPixel);
                }
                return image;
            };

            //Transform priors from each site drive to the base site drive
            Dictionary<string, Matrix> priorToBaseSiteDriveTransforms = new Dictionary<string, Matrix>();
            //Alignments computed for each site drive in base site drive frame 
            Dictionary<string, Matrix> baseSiteDriveAdjustments = new Dictionary<string, Matrix>();

            siteDrivePriors[baseSiteDrive] = frameCache.GetBestTransform(baseSiteDrive);
            priorToBaseSiteDriveTransforms[baseSiteDrive] = Matrix.Identity;
            baseSiteDriveAdjustments[baseSiteDrive] = Matrix.Identity;           

            List<string> aligned = new List<string> { baseSiteDrive };
            List<string> unaligned = new List<string>();

            //BEV does a good job aligning in XY plane
            //For site drives, allow rotation/vertical alignment, but not XY translation
            double[] sigmaNoXY = new double[] { Math.PI / 2880, Math.PI / 2880, Math.PI / 2880, 0, 0 };
            SimulatedAnnealingOptions saOptsNoXY = new SimulatedAnnealingOptions();
            saOptsNoXY.maxIterations = 400;
            saOptsNoXY.verbose = false;
            saOptsNoXY.temperatureScale = 1;
            saOptsNoXY.probabilityScale = 100;
            saOptsNoXY.sigma = sigmaNoXY;

            foreach (string siteDrive in siteDrives.GetRange(1, siteDrives.Count - 1))
            {
                logger.Info("Beginning alignment for site drive: " + siteDrive);
                var rec = FindOrCreateHeightmap(siteDrive);
                var image = GetImage(rec);

                //Align to the highest priority sitedrive with sufficient overlap
                bool success = false;
                Matrix adjustedSiteDriveToWorld = Matrix.Identity;

                foreach (string otherSiteDrive in siteDrives)
                {
                    if(unaligned.Contains(otherSiteDrive))
                    {
                        continue;
                    }
                    if(siteDrive == otherSiteDrive)
                    {
                        break; //Don't allow alignment to lower priority sitedrive
                    }

                    logger.InfoFormat("Attempting alignment from site drive {0} to site drive {1}", siteDrive, otherSiteDrive);

                    var otherRec = FindOrCreateHeightmap(otherSiteDrive);
                    var otherImg = GetImage(otherRec);

                    var temp = DemOperations.AlignSceneToScene(image, rec.OriginX, rec.OriginY, rec.MetersPerPixel, otherImg, otherRec.OriginX, otherRec.OriginY,
                        otherRec.MetersPerPixel, options.PreserveXY, 8, saOptsNoXY, 0.5, options.SceneHeightmapRes, options.DEMMinFilter, options.DEMMaxFilter);
                    if (!temp.HasValue)
                    {
                        continue;
                    }                 
                    Matrix adjustment = temp.Value;
                    baseSiteDriveAdjustments[siteDrive] = Matrix.Invert(adjustment) * baseSiteDriveAdjustments[otherSiteDrive];
                    logger.InfoFormat("Aligned site drive {0} to site drive {1}", siteDrive, otherSiteDrive);
                    success = true;
                    aligned.Add(siteDrive);
                    break;
                }

                if(!success)
                {
                    logger.InfoFormat("No sufficient overlap with any aligned sitedrive. Will align to orbital");
                    unaligned.Add(siteDrive);                 
                }
            }

            //Align dem to all aligned site drives
            logger.Info("Beginning alignment for DEM");

            SceneHeightmap baseRec = FindOrCreateHeightmap(baseSiteDrive);
            Image sceneImage = pipeline.GetDataProduct<TiffDataProduct>(project, baseRec.DEMGuid).Image;

            var records = aligned.Select(sd => FindOrCreateHeightmap(sd));
            var images = records.Select(rec => GetImage(rec)).ToArray();
            var xOffsets = records.Select(rec => rec.OriginX).ToArray();
            var yOffsets = records.Select(rec => rec.OriginY).ToArray();
            var metersPerPixel = records.Select(rec => rec.MetersPerPixel).ToArray();
            var priorTransforms = aligned.Select(sd => baseSiteDriveAdjustments[sd]).ToArray();

            Matrix demToBaseSiteDrive = DemOperations.AlignScenesToDem(images, xOffsets, yOffsets, metersPerPixel, dem, colRowOffset.Y, colRowOffset.X,
                options.MetersPerPixel, options.PreserveXY, 8, null, 0.0, options.SceneHeightmapRes, options.DEMMinFilter, options.DEMMaxFilter, priorTransforms).Value;

            //Align remaining sitedrives to dem
            foreach(string siteDrive in unaligned)
            {
                logger.InfoFormat("Aligning site drive {0} to DEM.", siteDrive);
                var rec = FindOrCreateHeightmap(siteDrive);
                var image = GetImage(rec);

                Matrix demToPriorBase = DemOperations.AlignSceneToDem(image, rec.OriginX, rec.OriginY, rec.MetersPerPixel, dem, colRowOffset.Y, colRowOffset.X, options.MetersPerPixel,
                    options.PreserveXY, 8, null, 0, options.SceneHeightmapRes, options.DEMMinFilter, options.DEMMaxFilter).Value;
                baseSiteDriveAdjustments[siteDrive] = Matrix.Invert(demToPriorBase) * demToBaseSiteDrive;
            }

            foreach (string siteDrive in siteDrives)
            {
                var adjustedSiteDriveToWorld = siteDrivePriors[siteDrive].Transform.Mean
                                               * Matrix.Invert(baseSiteDriveToWorld)
                                               * baseSiteDriveAdjustments[siteDrive]
                                               * baseSiteDriveToWorld;

                var ut = new UncertainRigidTransform(adjustedSiteDriveToWorld);
                var frame = frameCache.GetFrame(siteDrive);
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

            if (!String.IsNullOrEmpty(options.DemDebugPath))
            {
                //Get subset of dem around sitedrive
                int pixelRadius = (int)(200 / options.MetersPerPixel);
                int baseC = (int)Math.Max(colRowOffset.X - pixelRadius, 0);
                int baseR = (int)Math.Max(colRowOffset.Y - pixelRadius, 0);
                int pixelWidth = (int)Math.Min(colRowOffset.X + pixelRadius, gdalDem.Width) - baseC;
                int pixelHeight = (int)Math.Min(colRowOffset.Y + pixelRadius, gdalDem.Height) - baseR;

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
                demMesh.Transform(demToBaseSiteDrive);
                demMesh.Save(options.DemDebugPath);
            }

            return 0;
        }
    }
}

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

        [Option(HelpText = "Alignment relative to highest priority site drive (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst)", Default = SiteDrivePriority.OldestFirst)]
        public SiteDrivePriority SiteDrivePriority { get; set; }

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
                return 1; //help
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
            switch (options.SiteDrivePriority)
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
                        throw new NotImplementedException();
                    }
                case SiteDrivePriority.SmallestFirst:
                    {
                        throw new NotImplementedException();
                    }
            }

            string baseSiteDrive = siteDrives[0];
            baseSiteDrive = "0003101330";
            siteDrives.Remove(baseSiteDrive);
            siteDrives.Add(baseSiteDrive);
            siteDrives.Reverse();

            //Get offset of base site drive in dem
            double lon, lat;
            //TODO: places

            MSLPlaces places = new MSLPlaces();
            Vector2 latlon = places.GetEstimatedLatLon(new SiteDrive(baseSiteDrive));

            {
                if (baseSiteDrive == "0003101472")
                {
                    lon = 137.40220081556527;
                    lat = -4.639160357049109;
                }
                else if (baseSiteDrive == "0003101444")
                {
                    lon = 137.40215532608957;
                    lat = -4.639095324431385;
                }
                else if (baseSiteDrive == "0003101414")
                {
                    lon = 137.40209643859887;
                    lat = -4.63899034486038;
                }
                else if (baseSiteDrive == "0003101360")
                {
                    lon = 137.40200916648143;
                    lat = -4.638844859094681;
                }
                else if (baseSiteDrive == "0003101330")
                {
                    lon = 137.40201003292486;
                    lat = -4.638842854237814;
                }
                else if (baseSiteDrive == "0003101256")
                {
                    lon = 137.40203313205978;
                    lat = -4.638860190558752;
                }
                else
                {
                    throw new Exception("No lat/lon for sitedrive " + baseSiteDrive);
                }
            }

            Vector3 colRowOffset = gdalDem.LatLonToImage(new Vector3(lon, lat, 0));

            Matrix baseSiteDriveToWorld = frameCache.GetBestTransform(baseSiteDrive).Transform.Mean;

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
                Matrix siteDriveToWorldPrior = frameCache.GetBestTransform(siteDrive).Transform.Mean;
                ret.Transform(siteDriveToWorldPrior);
                ret.Transform(Matrix.Invert(baseSiteDriveToWorld));
                return ret;
            };

            Func<string, string, string> GetName = (sd, frame) => sd + "-" + frame;

            //Create a heightmap record for site drive in base site drive frame
            Func<string, SceneHeightmap> FindOrCreateHeightmap = (siteDrive) =>
            {
                SceneHeightmap rec = SceneHeightmap.Find(pipeline, project.Name, GetName(siteDrive, baseSiteDrive));
                if (rec != null)
                {
                    return rec;
                } else
                {
                    Mesh mesh = BuildMeshInBaseSiteDrive(siteDrive);                
                    Image img = MeshToHeightMap.BuildDem(mesh, options.SceneHeightmapRes, out double m2p, out double xOffset, out double yOffset);
                    img.Save<float>("D:\\dems\\heightmap_" + siteDrive + ".tiff");
                    return SceneHeightmap.Create(pipeline, project, GetName(siteDrive, baseSiteDrive), img, new Vector2(xOffset, yOffset), m2p);
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

            //Align remaining site drives to root via base site drive
            //Dictionary<string, Matrix> baseSiteDriveToWorldTransforms = new Dictionary<string, Matrix>();
            //baseSiteDriveToWorldTransforms[baseSiteDrive] = baseSiteDriveToWorld;
            Dictionary<string, Matrix> baseSiteDriveAdjustments = new Dictionary<string, Matrix>();
            Dictionary<string, Matrix> priorToBaseSiteDriveTransforms = new Dictionary<string, Matrix>();
            baseSiteDriveAdjustments[baseSiteDrive] = Matrix.Identity;
            priorToBaseSiteDriveTransforms[baseSiteDrive] = Matrix.Identity;

            List<string> aligned = new List<string> { baseSiteDrive };
            List<string> unaligned = new List<string>();

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

                Matrix siteDriveToWorldPrior = frameCache.GetBestTransform(siteDrive).Transform.Mean;
                priorToBaseSiteDriveTransforms[siteDrive] = siteDriveToWorldPrior * Matrix.Invert(baseSiteDriveToWorld);

                //Mesh loaded in its original site drive. Use best prior to get it to our base site drive for dem alignment

                /*logger.Info("Using best transform : " + frameCache.GetBestTransform(siteDrive).Source);
                siteDriveMesh.Transform(siteDriveToWorldPrior);
                siteDriveMesh.Transform(Matrix.Invert(baseSiteDriveToWorld));*/

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
                    logger.InfoFormat("Aligned stiedrive {0} to sitedrive {1}", siteDrive, otherSiteDrive);
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
                    options.PreserveXY, 8, saOptsNoXY, 0, options.SceneHeightmapRes, options.DEMMinFilter, options.DEMMaxFilter).Value;
                baseSiteDriveAdjustments[siteDrive] = Matrix.Invert(demToPriorBase) * demToBaseSiteDrive;
            }

            const bool DEBUG = true;
            if (DEBUG)
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
                Mesh demMesh = Delaunay.Triangulate(demPointCloud.Vertices); //for debug
                demMesh.Transform(demToBaseSiteDrive);
                demMesh.Transform(baseSiteDriveToWorld);
                demMesh.Save(Path.Combine(Path.GetDirectoryName(options.InputDem), "TEST_DEM_ALIGNED_" + baseSiteDrive + ".obj"));
            }

            foreach (string siteDrive in siteDrives)
            {
                var sdMesh = BuildMesh(siteDrive);
                var adjustedSiteDriveToWorld = priorToBaseSiteDriveTransforms[siteDrive]
                                               * baseSiteDriveAdjustments[siteDrive]
                                               * baseSiteDriveToWorld;
                sdMesh.Transform(adjustedSiteDriveToWorld);
                sdMesh.Save("D:\\dems\\" + GetName(siteDrive, baseSiteDrive) + ".obj");
            }

            /*var ut = new UncertainRigidTransform(adjustedSiteDriveToWorld);
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
                pipeline.LogInfo("saved {0} adjusted transform for site drive {1}", TransformSource.LandformOrbital, siteDrive);*/

            /*if (DEBUG)
            {
                Matrix adjustedBaseSiteDriveToWorld = Matrix.Invert(adjustedDemToBaseSiteDrive)
                                      * demToBaseSiteDrive
                                      * baseSiteDriveToWorld;
                siteDriveMesh.Transform(adjustedBaseSiteDriveToWorld);
                siteDriveMesh.Save(Path.Combine(Path.GetDirectoryName(options.InputDem), "TEST_ALIGNED_" + siteDrive + ".obj"));
            }*/

            return 0;
            //if (options.StereoEye != RoverStereoEye.Any)
            //{
            //    meshObservations = WedgeObservations.FilterForEye(meshObservations, options.StereoEye).ToList();
            //}

            /*if (siteDrives.Length < 2)
            {
                throw new Exception("at least two site drives required");
            }*/

            //Mesh wedge = Mesh.Load(options.AlignToScene);

            //string outputImage = null;
            //if (options.InputOrthoImage != null)
            //{
            //    //TODO: Properly clip ortho and map uvs
            //    Image ortho = Image.Load(options.InputOrthoImage);
            //    outputImage = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.ImageFormat);
            //    ortho.Save<byte>(outputImage); // TODO, add support for matching input type
            //}
            //pointCloud.HasUVs = true;
            //pointCloud.Save(this.options.OutputPath, outputImage);
            //mesh.HasUVs = true;
            //mesh.Save(Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.MeshFormat), outputImage);
            return 0;
        }
    }
}

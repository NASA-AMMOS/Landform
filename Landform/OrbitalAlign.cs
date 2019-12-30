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
    [Verb("orbital-align", HelpText = "")]
    public class OrbitalAlignerOptions : WedgeCommandOptions
    {
        [Value(1, Required = true, Default = 1, HelpText = "Size of a pixel in the DEM in meters")]
        public double MetersPerPixel { get; set; }

        [Value(2, Required = true, HelpText = "Image containing heights as values")]
        public string InputDem { get; set; }

        [Option(HelpText = "Specify a base site drive to align others to. By default BaseSiteDrivePriority will be used to pick the base site drive", Default = "")]
        public string BaseSiteDrive { get; set; }

        [Option(HelpText = "Base site drive chosen by highest priority (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst). Remaining sorted by RemainingSiteDrivePriority", Default = SiteDrivePriority.BiggestFirst)]
        public SiteDrivePriority BaseSiteDrivePriority { get; set; }

        [Option(HelpText = "Align remaining site drives to base site drive by priority (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst)", Default = SiteDrivePriority.BiggestFirst)]
        public SiteDrivePriority RemainingSiteDrivePriority { get; set; }

        [Option(Required = false, Default = "", HelpText = "Optionally write out transformed dem.")]
        public string DemDebugPath { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Scaler to convert dem values to verticle meters.  i.e. (meters/pixel value)")]
        public float VerticalScale { get; set; }

        [Option(Required = false, Default = true, HelpText = "If true, only allow rotation/vertical adjustment between scenes. Preferred if BEV align already run")]
        public bool PreserveXY { get; set; }

        [Option(Required = false, Default = true, HelpText = "Use cached heightmaps (if exist) for alignment. Recreate and save otherwise")]
        public bool UseCachedHeightmaps { get; set; }

        [Option(Required = false, Default = 256, HelpText = "Target resolution for intermediate scene heightmaps. Higher res = More alignment samples")]
        public int SceneHeightmapRes { get; set; }

        [Option(Required = false, Default = 1024, HelpText = "Target resolution for single observation site drives." +
            "Higher resolution = More accurate alignment samples, but slower. For multiple observations, resolution is scaled down by square root number of observations.")]
        public int DecimationResFactor { get; set; }

        [Option(Required = false, Default = -1000000, HelpText = "Dem values less than this will be ignored")]
        public float DEMMinFilter { get; set; }

        [Option(Required = false, Default = 1000000, HelpText = "Dem values larger than this will be ignored")]
        public float DEMMaxFilter { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Disable generating organized mesh normals when normal image missing", Default = false)]
        public bool NoGenerateNormals { get; set; }

        [Option(HelpText = "Debug directory to write out meshes/heightmaps. Default does not write", Default = "")]
        public string DebugProductsDir { get; set; }
    }

    public class OrbitalAligner : WedgeCommand
    {
        OrbitalAlignerOptions options;

        private const string OUT_DIR = "orbital/Products";

        protected new List<string> siteDrives;

        public OrbitalAligner(OrbitalAlignerOptions options) : base(options)
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

            Func<string, BirdsEyeView> FindBEV = (siteDrive) =>
            {
                BirdsEyeView bev = BirdsEyeView.Find(pipeline, project.Name, siteDrive);
                if (bev != null)
                {
                    return bev;
                }
                else
                {
                    throw new NotImplementedException("TODO");
                }
            };

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
                            siteDrives = siteDrives.OrderByDescending(sd =>
                            {
                                var bev = FindBEV(sd);
                                return bev.Width * bev.Height;
                            }).ToList();
                            break;
                        }
                    case SiteDrivePriority.SmallestFirst:
                        {
                            siteDrives = siteDrives.OrderBy(sd =>
                            {
                                var rec = FindBEV(sd);
                                return rec.Width * rec.Height;
                            }).ToList();
                            break;
                        }
                }
            };

            //Choose the highest priority site drive as the base for alignment
            SiteDrive bsd;
            if (String.IsNullOrEmpty(options.BaseSiteDrive))
            {
                sortSiteDrives(options.BaseSiteDrivePriority);
                bsd = new SiteDrive(siteDrives[0]);
            } else
            {
                bsd = new SiteDrive(options.BaseSiteDrive); //Allow either SSSDDDD or SSSSSDDDDD
            }
            string baseSiteDrive = bsd.ToString();
            siteDrives.Remove(baseSiteDrive);

            pipeline.LogInfo("Base site drive for alignment is {0}", baseSiteDrive);

            //Sort remaining by secondary priority
            sortSiteDrives(options.RemainingSiteDrivePriority);
            siteDrives.Insert(0, baseSiteDrive);

            //Site drive to world priors
            Dictionary<string, TransformSource> siteDrivePriorSources = new Dictionary<string, TransformSource>();
            Dictionary<string, Matrix> siteDriveToWorldPreviousBestTransforms = new Dictionary<string, Matrix>();
            foreach (string siteDrive in siteDrives)
            {
                var rec = frameCache.GetBestTransform(siteDrive);
                siteDriveToWorldPreviousBestTransforms[siteDrive] = rec.Transform.Mean;
                siteDrivePriorSources[siteDrive] = rec.Source;
                pipeline.LogInfo("Read in {0} transform for site drive {1}", rec.Source, siteDrive);
            }

            Matrix baseSiteDriveToWorld = siteDriveToWorldPreviousBestTransforms[baseSiteDrive];

            Func<string, Mesh> BuildMesh = (siteDrive) =>
            {
                pipeline.LogInfo("Building mesh for site drive : {0}", siteDrive);
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
                    //Shooting for site drive meshes with comparable size - need to decimate larger site drives, but don't want to reduce smaller ones (error more significant)
                    //Assuming all observations are rougly same size, total mesh size is approximately resolution ^ 2 * number of observations.
                    //Fixing total size yields heuristic for resolution proportional to 1 / sqrt(number of obs) 
                    meshOpts.Decimate = WedgeObservations.AutoDecimate(o.Points, -1, (int)(options.DecimationResFactor / Math.Sqrt(siteDriveObs.Count())));
                    return o.BuildOrganizedMesh(pipeline, frameCache, masker, meshOpts);
                }).Where(m => m != null).ToArray();
                return siteDriveMeshes.Count() > 0 ? Mesh.Merge(siteDriveMeshes) : new Mesh();
            };

            Func<string, Mesh> BuildMeshInBaseSiteDrive = (siteDrive) =>
            {
                var ret = BuildMesh(siteDrive);
                ret.Transform(siteDriveToWorldPreviousBestTransforms[siteDrive]);
                ret.Transform(Matrix.Invert(baseSiteDriveToWorld));
                return ret;
            };

            //TODO: How reusable should heightmaps be? Different under rotation, but not translation
            Func<string, string, TransformSource, string> GetName = (sd, frame, source) => sd + "-" + frame + "-" + source;

            //Create a heightmap record for site drive in base site drive frame
            //Func<string, SceneHeightmap> FindOrCreateHeightmap = (siteDrive) =>
            //{
            //    SceneHeightmap rec = SceneHeightmap.Find(pipeline, project.Name, GetName(siteDrive, baseSiteDrive, siteDrivePriorSources[siteDrive]));
            //    if (rec != null)
            //    {
            //        return rec;
            //    } else
            //    {
            //        Mesh mesh = BuildMeshInBaseSiteDrive(siteDrive);
            //        Image img = MeshToHeightMap.BuildDem(mesh, options.SceneHeightmapRes, out double m2p, out double xOffset, out double yOffset);
            //        if (!string.IsNullOrEmpty(options.DebugProductsDir))
            //        {
            //            mesh.Save(Path.Combine(options.DebugProductsDir, siteDrive + "_mesh.obj"));
            //            img.Save<float>(Path.Combine(options.DebugProductsDir, siteDrive + "_heightmap.tif"));
            //        }
            //        return SceneHeightmap.Create(pipeline, project, GetName(siteDrive, baseSiteDrive, siteDrivePriorSources[siteDrive]), img, new Vector2(xOffset, yOffset), m2p);
            //    }
            //};

            Func<BirdsEyeView, Image> GetImage = rec =>
            {
                var image = pipeline.GetDataProduct<TiffDataProduct>(project, rec.DEMGuid).Image;
                if (image.CameraModel == null)
                {
                    image.CameraModel = new OrthographicCameraModel(Matrix.Identity, image.Width, image.Height, rec.MetersPerPixel);
                }
                var mask = pipeline.GetDataProduct<PngDataProduct>(project, rec.MaskGuid).Image;
                Image newMask = (Image)mask.Clone();
                for (int i = 0; i < 7; i++)
                {
                    for (int r = 1; r < image.Height - 1; r++)
                    {
                        for (int c = 1; c < image.Width - 1; c++)
                        {
                            if (mask[0, r, c + 1] == 1 || mask[0, r + 1, c] == 1 || mask[0, r - 1, c] == 1 || mask[0, r, c - 1] == 1)
                            {
                                newMask[0, r, c] = 1;
                            }
                        }
                    }
                    var temp = newMask;
                    newMask = mask;
                    mask = temp;
                }
                image.UnionMask(mask, new float[] { 1 });              
                return image;
            };

            //Compute pairwise distances between site drive centers
            pipeline.LogInfo("Computing pairwise distances between site drive centers");
            Dictionary<string, double> squaredDistances = new Dictionary<string, double>();
            foreach (string sd1 in siteDrives)
            {
                BirdsEyeView m1 = FindBEV(sd1);
                foreach (string sd2 in siteDrives)
                {
                    BirdsEyeView m2 = FindBEV(sd2);
                    squaredDistances[sd1 + sd2] = Math.Pow(m1.OriginX - m2.OriginX, 2) + Math.Pow(m1.OriginY - m2.OriginY, 2);
                }
            }

            //Alignments computed for each site drive in world frame 
            Dictionary<string, Matrix> worldPriorToWorldTransforms = new Dictionary<string, Matrix>();
            worldPriorToWorldTransforms[baseSiteDrive] = Matrix.Identity;

            List<string> aligned = new List<string> { baseSiteDrive };
            List<string> unaligned = new List<string>();
          
            Func<BirdsEyeView, string, Matrix> CreateBEVToWorldMatrix = (bev, siteDrive) =>
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

                var sdToRootPrior = frameCache.GetBestPrior(siteDrive).Transform.Mean;
                var sdOriginOffset = Vector3.Transform(new Vector3(0, 0, 0), sdToRootPrior);
                var zOff = sdOriginOffset.Z;
                var sdOriginPixelOffset = sdOriginOffset / bev.MetersPerPixel;
                var sdOriginPixel = new Vector2(sdOriginPixelOffset.X + bev.OriginX, sdOriginPixelOffset.Y + bev.OriginY);
                var offsetCorrection = Matrix.CreateTranslation((bev.Width / 2.0 - sdOriginPixel.X) * bev.MetersPerPixel,
                                                                (bev.Height / 2.0 - sdOriginPixel.Y) * bev.MetersPerPixel,
                                                                -zOff);

                var sdToRoot = frameCache.GetBestTransform(siteDrive).Transform.Mean;

                return flipY * offsetCorrection * sdToRoot;
            };

            foreach (string siteDrive in siteDrives.GetRange(1, siteDrives.Count - 1))
            {
                pipeline.LogInfo("Beginning alignment for site drive: {0}", siteDrive);
                var rec = FindBEV(siteDrive);
                var image = GetImage(rec);

                //Align to the highest priority sitedrive with sufficient overlap
                bool success = false;
                Matrix adjustedSiteDriveToWorld = Matrix.Identity;

                //Try to align to closest site drives first
                foreach (string otherSiteDrive in siteDrives.OrderBy(sd => squaredDistances[siteDrive + sd]))
                {
                    //Only align unalinged to aligned
                    if (!aligned.Contains(otherSiteDrive))
                    {
                        continue;
                    }

                    pipeline.LogInfo("Attempting alignment from site drive {0} to site drive {1}", siteDrive, otherSiteDrive);

                    var otherRec = FindBEV(otherSiteDrive);
                    var otherImg = GetImage(otherRec);

                    Matrix sceneToWorld = CreateBEVToWorldMatrix(rec, siteDrive);
                    Matrix otherSceneToWorld = CreateBEVToWorldMatrix(otherRec, otherSiteDrive);

                    var temp = DemOperations.AlignSceneToDem(image, sceneToWorld, otherImg, otherSceneToWorld, options.PreserveXY, 8, null, 0.5, options.DEMMinFilter, options.DEMMaxFilter);
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

            //Align dem to all aligned site drives
            pipeline.LogInfo("Beginning alignment for DEM");

            //BirdsEyeView baseRec = FindBEV(baseSiteDrive);
            //Image sceneImage = pipeline.GetDataProduct<TiffDataProduct>(project, baseRec.DEMGuid).Image;

            var bevs = aligned.Select(sd => FindBEV(sd)).ToArray();
            var bevImages = bevs.Select(rec => GetImage(rec)).ToArray();
            //var xOffsets = bevs.Select(rec => rec.OriginX).ToArray();
            //var yOffsets = bevs.Select(rec => rec.OriginY).ToArray();
            //var metersPerPixel = bevs.Select(rec => rec.MetersPerPixel).ToArray();
            //var priorTransforms = aligned.Select(sd => worldPriorToWorldTransforms[sd]).ToArray();

            Matrix[] bevsToWorld = aligned.Select(sd => {
                var bev = FindBEV(sd);
                Matrix bevToWorldPrior = CreateBEVToWorldMatrix(bev, sd);
                Matrix worldPriorToWorld = worldPriorToWorldTransforms[sd];
                return bevToWorldPrior * worldPriorToWorld;
            }).ToArray();


            //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

            Matrix demToBevPrior;

            {
                //Matrix flipY = new Matrix(1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

                //Pixel in bev corresponding to site drive center
                var sdToRootPrior = frameCache.GetBestPrior(baseSiteDrive).Transform.Mean;
                var sdOriginOffset = Vector3.Transform(new Vector3(0, 0, 0), sdToRootPrior);
                var zOff = sdOriginOffset.Z;
                var sdOriginPixelOffset = sdOriginOffset / bevs[0].MetersPerPixel;
                var bevSDOriginPixel = new Vector2(sdOriginPixelOffset.X + bevs[0].OriginX, sdOriginPixelOffset.Y + bevs[0].OriginY);
                Vector3 bevSDOriginXYZ = new Vector3((bevSDOriginPixel.X - bevs[0].Width / 2.0) * bevs[0].MetersPerPixel,
                                                     -1 * (bevSDOriginPixel.Y - bevs[0].Height / 2.0) * bevs[0].MetersPerPixel,
                                                     /*-zOff*/ 0);

                //Get the pixel in the dem corresponding to the site drive center
                const double DemMetersPerPixel = 1;

                MSLPlaces places = new MSLPlaces();
                Vector2 latlon = places.GetEstimatedLatLon(new SiteDrive(baseSiteDrive));
                GDALDEM gdalDem = GDALDEM.MarsDEM(options.InputDem);
                Vector3 colRowOffset = gdalDem.LatLonToImage(new Vector3(latlon.Y, latlon.X, 0));

                Vector3 demSDOriginXYZ = new Vector3((colRowOffset.X - dem.Width / 2.0) * DemMetersPerPixel,
                                                     -1 * (colRowOffset.Y - dem.Height / 2.0) * DemMetersPerPixel,
                                                     0);

                Matrix demToBevTranslation = Matrix.CreateTranslation(bevSDOriginXYZ - demSDOriginXYZ);

                demToBevPrior = demToBevTranslation;
            }

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


            //Matrix demToBevPrior = mission.GetDemToBevTransform(baseSiteDrive, bevs[0], dem.Width, dem.Height, options.InputDem);
            Matrix demToWorldPrior = demToBevPrior * frameCache.GetBestTransform(baseSiteDrive).Transform.Mean;
            Matrix demWorldPriorToWorld = DemOperations.AlignScenesToDem(bevImages, bevsToWorld, dem, demToWorldPrior, false, 8, null, 0.0, options.DEMMinFilter, options.DEMMaxFilter).Value;

            Matrix demToWorld = demToWorldPrior * demWorldPriorToWorld;

            //Align remaining sitedrives to dem
            /*foreach(string siteDrive in unaligned)
            {
                pipeline.LogInfo("Aligning site drive {0} to DEM.", siteDrive);
                var rec = FindBEV(siteDrive);
                var image = GetImage(rec);

                Matrix sdToWorldPrior = CreateBEVToWorldMatrix(FindBEV(siteDrive), siteDrive);
                Matrix demWorldToSDWorld = DemOperations.AlignSceneToDem(image, sdToWorldPrior, dem, demToWorld,
                    options.PreserveXY, 8, null, 0, options.DEMMinFilter, options.DEMMaxFilter).Value;
                worldPriorToWorldTransforms[siteDrive] = Matrix.Invert(demWorldToSDWorld) * demWorldPriorToWorld;
            }*/

            foreach (string siteDrive in siteDrives)
            {          
                /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                var rec = FindBEV(siteDrive);
                var image = GetImage(rec);
                Matrix sceneToWorld = CreateBEVToWorldMatrix(rec, siteDrive);
                Mesh pointCloud = new Mesh();
                for (int r = 0; r < image.Height; r++)
                {
                    for (int c = 0; c < image.Width; c++)
                    {
                        var pos = DemOperations.GetXYZ(image, r, c);
                        if (pos.HasValue)
                        {
                            Vertex v = new Vertex();
                            v.Position = pos.Value;
                            v.UV = dem.PixelToUV(new Vector2(c, r));
                            pointCloud.Vertices.Add(v);
                        }
                    }
                }
                Mesh sceneMesh = Delaunay.Triangulate(pointCloud.Vertices);
                sceneMesh.Transform(sceneToWorld);
                sceneMesh.Save("D://dems//" + siteDrive + "_prior.obj");

                /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

                if(!worldPriorToWorldTransforms.ContainsKey(siteDrive))
                {
                    continue;
                }

                var adjustedSiteDriveToWorld = frameCache.GetBestTransform(siteDrive).Transform.Mean
                                               * worldPriorToWorldTransforms[siteDrive];

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
                //TODO: mission specific
                MSLPlaces places = new MSLPlaces();
                Vector2 latlon = places.GetEstimatedLatLon(new SiteDrive(baseSiteDrive));
                GDALDEM gdalDem = GDALDEM.MarsDEM(options.InputDem);
                Vector3 colRowOffset = gdalDem.LatLonToImage(new Vector3(latlon.Y, latlon.X, 0));

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
                            //v.Position = pos.Value;
                            //v.Position.Z *= -1; //GetXYZ normally flips z which is already taken care of.
                            v.UV = dem.PixelToUV(new Vector2(baseC + c, baseR + r));
                            var vBev = Vector3.Transform(pos.Value, demToBevPrior);
                            var vWorldPrior = Vector3.Transform(vBev, bevsToWorld[0]);
                            v.Position = Vector3.Transform(vWorldPrior, demWorldPriorToWorld);
                            demPointCloud.Vertices.Add(v);
                        }
                    }
                }
                Mesh demMesh = Delaunay.Triangulate(demPointCloud.Vertices);
                demMesh.Transform(demToWorld);               
                demMesh.Save(options.DemDebugPath);
            }

            return 0;
        }
    }
}

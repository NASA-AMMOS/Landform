using System;
using System.IO;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;

/// <summary>
///
/// Utility to convert an orbital DEM and optionally orthoimage to a mesh.
///
/// Examples:
///
/// Landform.exe dem2mesh out_deltaradii_smg_1m.tif out_deltaradii_smg_1m_0311472_200m.obj
///     --inputimage out_clean_25cm.iGrid.ClipToDEM.tif --mission MSL --outputframe 0311472 --radiusmeters 200
///
/// Landform.exe dem2mesh out_deltaradii_smg_1m.tif out_deltaradii_smg_1m_decimate4_full.obj
///     --inputimage out_clean_25cm.iGrid.ClipToDEM.tif --mission MSL --decimatedem 32 --decimateimage 32
///
/// Landform.exe dem2mesh M20_PrimeMission_HiRISE_DEM_1m.tif M20_PrimeMission_HiRISE_DEM_1m.obj
///    --inputimage M20_PrimeMission_HiRISE_CLR_25cm.tif --mission M2020 --decimatedem 8 --decimateimage 8
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("dem2mesh", HelpText = "Convert a DEM and optional image to a mesh")]
    public class DEM2MeshOptions : CommandHelper.BaseOptions
    {
        [Value(0, Required = true, HelpText = "DEM image for mesh geometry")]
        public string InputDEM { get; set; }

        [Value(1, Required = false, HelpText = "Optional output mesh.  Must have same extension as --meshformat.  If omitted he input DEM filename is used with the mesh format extension.")]
        public string OutputMesh { get; set; }

        [Option(Required = false, HelpText = "Optional image to texture the mesh.  The image must be the same aspect and physical extent as the DEM, but can have a different resolution.")]
        public string InputImage { get; set; }

        [Option(Default = "auto", HelpText = "Size of a pixel in the input DEM in meters, or \"auto\" to use mission default, or 1 if no mission.")]
        public string DEMMetersPerPixel { get; set; }

        [Option(Default = "auto", HelpText = "Size of a pixel in the input image in meters, or \"auto\" to use mission default, or 1 if no mission.")]
        public string ImageMetersPerPixel { get; set; }

        [Option(Default = "auto", HelpText = "Scale DEM values to vertical meters, or \"auto\" to use mission default, or 1 if no mission")]
        public string VerticalScale { get; set; }

        [Option(Default = "auto", HelpText = "DEM body, \"mars\", \"earth\", or \"auto\" to use mission default, or mars if no mission.")]
        public string DEMBody { get; set; }

        [Option(Default = "png", HelpText = "Export format for texture (examples: jpg or png")]
        public string ImageFormat { get; set; }

        [Option(Default = "obj", HelpText = "Export format for mesh (examples: obj or ply")]
        public string MeshFormat { get; set; }

        [Option(Default = 0, HelpText = "Adaptive mesh to this error threshold.  Set to 0 to build a full organized mesh instead of adaptive meshing.")]
        public double MaxError { get; set; }

        [Option(Default = 1, HelpText = "Organized mesh subsample factor.  Set > 1 to decimate, < 1 to interpolate")]
        public double SubsampleMesh { get; set; }

        [Option(Default = DEM.DEF_MIN_FILTER, HelpText = "Dem values less than this will be ignored")]
        public double DEMMinFilter { get; set; }

        [Option(Default = DEM.DEF_MAX_FILTER, HelpText = "Dem values larger than this will be ignored")]
        public double DEMMaxFilter { get; set; }

        [Option(Default = "", HelpText = "Origin at and output to sitedrive frame SSSSSDDDDD or SSSDDDD, requires --mission and PlacesDB support")]
        public string OutputFrame { get; set; }

        [Option(Default = -1, HelpText = "Radius in meters around origin pixel to build mesh, negative for unlimited")]
        public float RadiusMeters { get; set; }

        [Option(Default = null, HelpText = "Origin pixel in format \"(X,Y)[m]\" or \"(LON,LAT)deg\", exclusive with --outputframe, defaults to center of DEM")]
        public string OriginPixel { get; set; }

        [Option(Default = 0, HelpText = "If greater than one then decimate the input DEM by this blocksize")]
        public int DecimateDEM { get; set; }

        [Option(Default = 0, HelpText = "If greater than one then decimate the input image by this blocksize")]
        public int DecimateImage { get; set; }

        [Option(Default = 8192, HelpText = "Maximum output texture resolution, 0 disables output texture, negative for unlimited")]
        public int MaxTextureResolution { get; set; }

        [Option(Default = "None", HelpText = "Mission flag enables mission specific behavior, optional :venue override, e.g. None, MSL, M2020, M20SOPS, M20SOPS:dev, M20SOPS:sbeta")]
        public string Mission { get; set; }

        [Option(Default = -1, HelpText = "index of DEM metadata in PlacesDB, negative to use orbital config default for mission")]
        public int DEMPlacesDBIndex { get; set; }

        [Option(Default = false, HelpText = "Compare planar approximation to spherical for mesh region")]
        public bool CheckPlanarity { get; set; }

        [Option(Default = false, HelpText = "Only compare planar approximation to spherical for mesh region")]
        public bool OnlyCheckPlanarity { get; set; }

        [Option(Default = false, HelpText = "Dry run")]
        public bool DryRun { get; set; }

        [Option(Default = false, HelpText = "Synonym for --dryrun")]
        public bool NoSave { get; set; }
    }

    public class DEM2Mesh
    {
        private static readonly ILogger logger = new ThunkLogger(LogManager.GetLogger("dem2mesh"));

        private DEM2MeshOptions options;

        private MissionSpecific mission;

        private string meshExt, imageExt;
        private string outputMesh, outputImage;

        private string demBody;
        private GISCameraModel demCamera, imageCamera;
        private DEM dem;
        private Image image;

        private double demMetersPerPixel, imageMetersPerPixel, elevationScale;

        public DEM2Mesh(DEM2MeshOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadInputs())
                {
                    return 0; //help
                }

                if (options.CheckPlanarity || options.OnlyCheckPlanarity)
                {
                    CheckPlanarity();
                }

                if (!options.OnlyCheckPlanarity)
                {
                    if (image != null)
                    {
                        BuildAndSaveTexture();
                    }
                    
                    BuildAndSaveMesh();
                }
            }
            catch (Exception ex)
            {
                logger.LogException(ex);
                return 1;
            }
            
            return 0;
        }

        private bool ParseArgumentsAndLoadInputs()
        {
            options.NoSave |= options.DryRun;

            meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, logger);
            if (meshExt == null)
            {
                return false; //help
            }
            
            imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, logger);
            if (imageExt == null)
            {
                return false; //help
            }

            if (options.SubsampleMesh != 1 && options.MaxError != 0)
            {
                throw new Exception("--subsamplesmesh requires --maxerror=0");
            }

            if (string.IsNullOrEmpty(options.InputDEM) || !File.Exists(options.InputDEM))
            {
                throw new Exception("input DEM not found: " + options.InputDEM);
            }

            if (!string.IsNullOrEmpty(options.OutputMesh)) {
                outputMesh = options.OutputMesh;
                if (!outputMesh.EndsWith(meshExt, StringComparison.OrdinalIgnoreCase)) {
                    throw new Exception($"output mesh {options.OutputMesh} does not have extension {meshExt}");
                }
            }
            else
            {
                outputMesh = Path.ChangeExtension(options.InputDEM, meshExt);
            }

            //even if we don't directly use the mission instance
            //this has the important side effect of setting defaults for PlacesConfig and OrbitalConfig
            mission = MissionSpecific.GetInstance(options.Mission);

            var cfg = OrbitalConfig.Instance;

            demMetersPerPixel = 1;
            if (string.IsNullOrEmpty(options.DEMMetersPerPixel) || options.DEMMetersPerPixel.ToLower() == "auto")
            {
                demMetersPerPixel = cfg.DEMMetersPerPixel;
                if (mission == null)
                {
                    logger.LogWarn("no mission, using default orbital DEM meters per pixel: {0}", demMetersPerPixel);
                }
            }
            else
            {
                demMetersPerPixel = double.Parse(options.DEMMetersPerPixel);
            }

            elevationScale = 1;
            if (string.IsNullOrEmpty(options.VerticalScale) || options.VerticalScale.ToLower() == "auto")
            {
                elevationScale = cfg.DEMElevationScale;
                if (mission == null)
                {
                    logger.LogWarn("no mission, using default orbital DEM elevation scale: {0}", elevationScale);
                }
            }
            else
            {
                elevationScale = double.Parse(options.VerticalScale);
            }

            demBody = "mars";
            if (string.IsNullOrEmpty(options.DEMBody) || options.DEMBody.ToLower() == "auto")
            {
                demBody = cfg.BodyName;
                if (mission == null)
                {
                    logger.LogWarn("no mission, using default orbital DEM body: {0}", demBody);
                }
            }
            else
            {
                demBody = options.DEMBody;
            }

            demCamera = new GISCameraModel(options.InputDEM, demBody);
            logger.LogInfo("loaded GeoTIFF metadata from {0}", options.InputDEM);
            demCamera.Dump(logger);

            var elevationMap = new SparseGISElevationMap(options.InputDEM);

            double? originElevation = null; //DEM.OrthoDEM() will look this up given originPixel
            Vector2 originPixel = new Vector2(demCamera.Width - 1, demCamera.Height - 1) * 0.5;

            if (SiteDrive.IsSiteDriveString(options.OutputFrame))
            {
                if (mission == null)
                {
                    throw new Exception("--mission required for output in site drive frame");
                }

                if (!string.IsNullOrEmpty(options.OriginPixel))
                {
                    throw new Exception("--originpixel exclusive with --outputframe");
                }

                int index = options.DEMPlacesDBIndex >= 0 ? options.DEMPlacesDBIndex : cfg.DEMPlacesDBIndex;
                var sd = new SiteDrive(options.OutputFrame);

                var placesDB = new PlacesDB(logger, options.Debug);

                string bestView = null;
                var eastingNorthingElev = placesDB.GetEastingNorthingElevation(sd, index, view: v => { bestView = v; });

                originPixel = demCamera.EastingNorthingToImage(eastingNorthingElev).XY();

                logger.LogInfo("resolved output frame site drive {0} using PlacesDB {1} orbital index {2}: " +
                               "(easting, northing, elevation) = ({3:f3}, {4:f3}, {5:f3})m, " +
                               "(x, y) = ({6:f2}, {7:f2})px",
                               sd, bestView, index, eastingNorthingElev.X, eastingNorthingElev.Y, eastingNorthingElev.Z,
                               originPixel.X, originPixel.Y);
            
                var mpp = demCamera.CheckLocalGISImageBasisAndGetResolution(originPixel, logger);
                
                mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevationDir,
                                                                out Vector3 rightDir, out Vector3 downDir);
                
                dem = DEM.OrthoDEM(elevationMap, elevationDir, rightDir, downDir,
                                   demMetersPerPixel, mpp.X / mpp.Y, elevationScale,
                                   originPixel, originElevation, options.DEMMinFilter, options.DEMMaxFilter);
            }
            else
            {
                if (!string.IsNullOrEmpty(options.OriginPixel))
                {
                    var op = options.OriginPixel.Trim();
                    string sfx = "";
                    if (op.EndsWith("deg"))
                    {
                        sfx = "deg";
                    }
                    if (op.EndsWith("m"))
                    {
                        sfx = "m";
                    }
                    op = op.Substring(0, op.Length - sfx.Length);
                    
                    var opc = op.TrimStart('(').TrimEnd(')').Split(',');
                    if (opc.Length != 2)
                    {
                        throw new Exception("expected --originpixel=(X,Y)[m] or X,Y[m] or (LON,LAT)deg or LON,LATdeg");
                    }
                    var opv = new Vector2(double.Parse(opc[0].Trim()), double.Parse(opc[1].Trim()));

                    if (sfx == "deg")
                    {
                        originPixel = demCamera.LonLatToImage(opv);
                    }
                    else
                    {
                        double toPixels = sfx == "m" ? (1 / demMetersPerPixel) : 1;
                        originPixel = opv * toPixels;
                    }
                }

                var mpp = demCamera.CheckLocalGISImageBasisAndGetResolution(originPixel, logger);
                dem = DEM.OrthoDEM(elevationMap, demMetersPerPixel, mpp.X / mpp.Y,
                                   elevationScale, originPixel, originElevation,
                                   options.DEMMinFilter, options.DEMMaxFilter); 
            }

            var demOriginLonLat = demCamera.ImageToLonLat(originPixel);
            originElevation = dem.GetInterpolatedElevation(originPixel);

            logger.LogInfo("origin pixel ({0:f3}, {1:f3}), (lon, lat) ({2:f7}, {3:f7})deg, elevation {4:f3}m",
                           originPixel.X, originPixel.Y, demOriginLonLat.X, demOriginLonLat.Y,
                           originElevation.HasValue ? originElevation.Value : double.NaN);
                
            var demMinLonLat = demCamera.ImageToLonLat(Vector2.Zero);
            var demMaxPixel = new Vector2(demCamera.Width - 1, demCamera.Height - 1);
            var demMaxLonLat = demCamera.ImageToLonLat(demMaxPixel);
            var demCtrPixel = 0.5 * demMaxPixel;
            var demCtrLonLat = demCamera.ImageToLonLat(demCtrPixel);
            var demMinEastingNorthing = demCamera.ImageToEastingNorthing(Vector2.Zero);
            var demMaxEastingNorthing = demCamera.ImageToEastingNorthing(demMaxPixel);
            var demCtrEastingNorthing = demCamera.ImageToEastingNorthing(demCtrPixel);
            logger.LogInfo("dem min pixel (0, 0) is (lon, lat) ({0:f7}, {1:f7})deg",
                           demMinLonLat.X, demMinLonLat.Y);
            logger.LogInfo("dem center pixel ({0:f3}, {1:f3}) is (lon, lat) ({2:f7}, {3:f7})deg",
                           demCtrPixel.X, demCtrPixel.Y, demCtrLonLat.X, demCtrLonLat.Y);
            logger.LogInfo("dem max pixel ({0:f3}, {1:f3}) is (lon, lat) ({2:f7}, {3:f7})deg",
                           demMaxPixel.X, demMaxPixel.Y, demMaxLonLat.X, demMaxLonLat.Y);
            logger.LogInfo("dem min pixel (0, 0) is (easting, northing) ({0:f3}, {1:f3})m",
                           demMinEastingNorthing.X, demMinEastingNorthing.Y);
            logger.LogInfo("dem center pixel ({0:f3}, {1:f3}) is (easting, northing) ({2:f3}, {3:f3})m",
                           demCtrPixel.X, demCtrPixel.Y, demCtrEastingNorthing.X, demCtrEastingNorthing.Y);
            logger.LogInfo("dem max pixel ({0:f3}, {1:f3}) is (easting, northing) ({2:f3}, {3:f3})m",
                           demMaxPixel.X, demMaxPixel.Y, demMaxEastingNorthing.X, demMaxEastingNorthing.Y);

            if (!string.IsNullOrEmpty(options.InputImage) && options.MaxTextureResolution != 0)
            {
                imageMetersPerPixel = 1;
                if (string.IsNullOrEmpty(options.ImageMetersPerPixel) ||
                    options.ImageMetersPerPixel.ToLower() == "auto")
                {
                    imageMetersPerPixel = cfg.ImageMetersPerPixel;
                    if (mission == null)
                    {
                        logger.LogWarn("no mission, using default orbital image meters per pixel: {0}",
                                       imageMetersPerPixel);
                    }
                }
                else
                {
                    imageMetersPerPixel = double.Parse(options.ImageMetersPerPixel);
                }

                imageCamera = new GISCameraModel(options.InputImage, demBody);
                logger.LogInfo("loaded GeoTIFF metadata from {0}", options.InputImage);
                imageCamera.Dump(logger);

                image = new SparseGISImage(options.InputImage);

                var imgMinLonLat = imageCamera.ImageToLonLat(Vector2.Zero);
                var imgMaxPixel = new Vector2(imageCamera.Width - 1, imageCamera.Height - 1);
                var imgMaxLonLat = imageCamera.ImageToLonLat(imgMaxPixel);
                var imgCtrPixel = 0.5 * imgMaxPixel;
                var imgCtrLonLat = imageCamera.ImageToLonLat(imgCtrPixel);
                var imgMinEastingNorthing = imageCamera.ImageToEastingNorthing(Vector2.Zero);
                var imgMaxEastingNorthing = imageCamera.ImageToEastingNorthing(imgMaxPixel);
                var imgCtrEastingNorthing = imageCamera.ImageToEastingNorthing(imgCtrPixel);
                logger.LogInfo("image min pixel (0, 0) is (lon, lat) ({0:f7}, {1:f7})", imgMinLonLat.X, imgMinLonLat.Y);
                logger.LogInfo("image center pixel ({0:f3}, {1:f3}) is (lon, lat) ({2:f7}, {3:f7})deg",
                               imgCtrPixel.X, imgCtrPixel.Y, imgCtrLonLat.X, imgCtrLonLat.Y);
                logger.LogInfo("image max pixel ({0:f3}, {1:f3}) is (lon, lat) ({2:f7}, {3:f7})deg",
                               imgMaxPixel.X, imgMaxPixel.Y, imgMaxLonLat.X, imgMaxLonLat.Y);
                logger.LogInfo("image min pixel (0, 0) is (easting, northing) ({0:f3}, {1:f3})m",
                               imgMinEastingNorthing.X, imgMinEastingNorthing.Y);
                logger.LogInfo("image center pixel ({0:f3}, {1:f3}) is (easting, northing) ({2:f3}, {3:f3})m",
                               imgCtrPixel.X, imgCtrPixel.Y, imgCtrEastingNorthing.X, imgCtrEastingNorthing.Y);
                logger.LogInfo("image max pixel ({0:f3}, {1:f3}) is (easting, northing) ({2:f3}, {3:f3})m",
                               imgMaxPixel.X, imgMaxPixel.Y, imgMaxEastingNorthing.X, imgMaxEastingNorthing.Y);

                outputImage = Path.Combine(Path.GetDirectoryName(outputMesh),
                                           Path.GetFileNameWithoutExtension(outputMesh) + "_texture" + imageExt);
            }

            logger.LogInfo("output mesh {0}", outputMesh);
            if (outputImage != null) {
                logger.LogInfo("output texture {0}", outputImage);
            }

            if (options.DecimateDEM > 1)
            {
                logger.LogInfo("decimating DEM, blocksize {0}", options.DecimateDEM);
                dem = dem.Decimated(options.DecimateDEM, progress: msg => logger.LogInfo(msg));
                demMetersPerPixel *= options.DecimateDEM;
            }
            
            if (image != null && options.DecimateImage > 1)
            {
                logger.LogInfo("decimating image, blocksize {0}", options.DecimateImage);
                image = image.Decimated(options.DecimateImage, progress: msg => logger.LogInfo(msg));
                imageMetersPerPixel *= options.DecimateImage;
            }

            return true;
        }

        private void BuildAndSaveTexture()
        {
            var texture = image;
            int maxRes = options.MaxTextureResolution;
            if (options.RadiusMeters < 0)
            {
                double maxDim = Math.Max(texture.Width, texture.Height);
                if (maxRes > 0 && maxDim > maxRes)
                {
                    double s = maxRes / maxDim;
                    int w = (int)Math.Floor(texture.Width * s);
                    int h = (int)Math.Floor(texture.Height * s);
                    logger.LogInfo("resizing {0}x{1} texture to {2}x{3}", texture.Width, texture.Height, w, h);
                    texture = texture.Resize(w, h);
                }
            }
            else
            {
                double imagePixelsPerDemPixel = demMetersPerPixel / imageMetersPerPixel;
                Vector2 originPixel = dem.OriginPixel * imagePixelsPerDemPixel;

                var subrect = texture.GetSubrect(originPixel, options.RadiusMeters / imageMetersPerPixel);

                double maxDim = Math.Max(subrect.Width, subrect.Height);
                if (maxRes > 0 && maxDim > maxRes)
                {
                    double s = maxRes / maxDim;
                    int w = (int)Math.Floor(subrect.Width * s);
                    int h = (int)Math.Floor(subrect.Height * s);
                    logger.LogInfo("resampling {0}x{1} texture subrect to {2}x{3}",
                                   subrect.Width, subrect.Height, w, h);
                    texture = new Image(texture.Bands, w, h);
                    for (int b = 0; b < texture.Bands; b++)
                    {
                        for (int r = 0; r < h; r++)
                        {
                            float srcRow = subrect.MinY + subrect.Height * (((float)r) / h);
                            for (int c = 0; c < w; c++)
                            {
                                float srcCol = subrect.MinX + subrect.Width * (((float)c) / w);
                                texture[b, r, c] = image.BilinearSample(b, srcRow, srcCol);
                            }
                        }
                    }
                }
                else
                {
                    logger.LogInfo("cropping {0}x{1} subrect from {2}x{3} texture",
                                   subrect.Width, subrect.Height, texture.Width, texture.Height);
                    texture = texture.Crop(subrect);
                }
            }

            logger.LogInfo("{0}saving {1}x{2} {3} band texture {4}",
                           options.NoSave ? "not " : "", texture.Width, texture.Height, texture.Bands, outputImage);
            if (!options.NoSave)
            {
                texture.Save<byte>(outputImage);
            }
        }

        private void BuildAndSaveMesh()
        {
            logger.LogInfo("{0} meshing DEM, radius {1}{2}",
                           options.MaxError == 0 ? "organized" : "adaptive", options.RadiusMeters,
                           options.SubsampleMesh != 1 ? $", subsample {options.SubsampleMesh:f3}" : "");

            Mesh mesh = null;
            if (options.MaxError == 0)
            {
                var subrect = dem.GetSubrectMeters(options.RadiusMeters);
                mesh = dem.OrganizedMesh(subrect, subsample: options.SubsampleMesh, withUV: true);
            }
            else
            {
                mesh = dem.AdaptiveMesh(options.MaxError, options.RadiusMeters, withUV: true);
            }
            
            logger.LogInfo("{0}saving {1} triangle mesh {2}",
                           options.NoSave ? "not " : "", Fmt.KMG(mesh.Faces.Count), outputMesh);
            if (!options.NoSave)
            {
                mesh.Save(outputMesh, image != null ? Path.GetFileName(outputImage) : null);
            }
        }

        private void CheckPlanarity()
        {
            var demOriginLonLat = demCamera.ImageToLonLat(dem.OriginPixel);

            logger.LogInfo("checking planarity around origin pixel ({0:f3}, {1:f3}) ({2:f3}, {3:f3})m, " +
                           "(lon, lat) ({4:f7}, {5:f7})deg",
                           dem.OriginPixel.X, dem.OriginPixel.Y,
                           dem.OriginPixel.X * demMetersPerPixel, dem.OriginPixel.Y * demMetersPerPixel,
                           demOriginLonLat.X, demOriginLonLat.Y);

            logger.LogInfo("DEM body {0}, radius {1:f3}", demBody, demCamera.Body.Radius);

            var subrect = dem.GetSubrectMeters(options.RadiusMeters);

            var demMinLonLat = demCamera.ImageToLonLat(subrect.Min);
            var demMaxLonLat = demCamera.ImageToLonLat(subrect.Max);

            logger.LogInfo("subrect min pixel ({0:f3}, {1:f3}) is (lon, lat) ({2:f7}, {3:f7})deg",
                           subrect.MinX, subrect.MinY, demMinLonLat.X, demMinLonLat.Y);
            logger.LogInfo("subrect max pixel ({0:f3}, {1:f3}) is (lon, lat) ({2:f7}, {3:f7})deg",
                           subrect.MaxX, subrect.MaxY, demMaxLonLat.X, demMaxLonLat.Y);
            
            logger.LogInfo("checking {0} pixels in {1:f3}x{2:f3} ({3:f3}x{4:f3})m subrect",
                           Fmt.KMG(subrect.Area), subrect.Width, subrect.Height,
                           subrect.Width * demMetersPerPixel, subrect.Height * demMetersPerPixel);

            double distancePointToPlane(Vector3 point, Vector3 ptOnPlane, Vector3 planeUnitNormal)
            {
                return Vector3.Dot(point - ptOnPlane, planeUnitNormal);
            }

            Vector3 projectPointOntoPlane(Vector3 point, Vector3 pointOnPlane, Vector3 planeUnitNormal)
            {
                var rel = point - pointOnPlane;

                var relPerpendicular = planeUnitNormal * Vector3.Dot(rel, planeUnitNormal);

                //rel = relInPlane + relPerpendicular -> relInPlane = rel - relPerpendicular
                var relInPlane = rel - relPerpendicular;

                return pointOnPlane + relInPlane;
            }


            var originPixel = subrect.Center;

            var orbitalOriginXYZ = demCamera.GetLocalGISImageBasisInBodyFrame(originPixel,
                                                                              out Vector3 orbitalElevation,
                                                                              out Vector3 orbitalRight,
                                                                              out Vector3 orbitalDown);

            var orbitalResolution = demCamera.CheckLocalGISImageBasisAndGetResolution(originPixel, logger);
            double orbitalPixelAspect = orbitalResolution.X / orbitalResolution.Y;

            var zenith = Vector3.Normalize(orbitalOriginXYZ); //gravity-aligned vector pointing away from center of body

            var orthographicCamera =
                new OrthographicCameraModel(orbitalOriginXYZ, //center
                                            Vector3.Normalize(orbitalElevation) * elevationScale,
                                            Vector3.Normalize(orbitalRight) * demMetersPerPixel * orbitalPixelAspect,
                                            Vector3.Normalize(orbitalDown) * demMetersPerPixel,
                                            subrect.Width, subrect.Height);

            double maxElevationDeviation = 0;
            double maxDeviationFromOrtho = 0;
            double maxInPlaneDeviationFromOrtho = 0;

            for (int r = subrect.MinY; r <= subrect.MaxY; r++)
            {
                for (int c = subrect.MinX; c <= subrect.MaxX; c++)
                {
                    var pixelInDEM = new Vector3(c, r, 0);

                    var orbitalXYZ = demCamera.ImageToXYZ(pixelInDEM);

                    double relSphericalElevation = distancePointToPlane(orbitalXYZ, orbitalOriginXYZ, zenith);
                    maxElevationDeviation = Math.Max(maxElevationDeviation, Math.Abs(relSphericalElevation));

                    var pixelInOrthoCamera = new Vector2(c - subrect.MinX, r - subrect.MinY);

                    var orthoXYZ = orthographicCamera.Unproject(pixelInOrthoCamera, 0);

                    double deviationFromOrtho = Vector3.Distance(orthoXYZ, orbitalXYZ);
                    maxDeviationFromOrtho = Math.Max(maxDeviationFromOrtho, deviationFromOrtho);

                    var orbitalXYZProjected = projectPointOntoPlane(orbitalXYZ, orbitalOriginXYZ, zenith);

                    double inPlaneDeviationFromOrtho = Vector3.Distance(orthoXYZ, orbitalXYZProjected);
                    maxInPlaneDeviationFromOrtho = Math.Max(maxInPlaneDeviationFromOrtho, inPlaneDeviationFromOrtho);
                }
            }

            logger.LogInfo("max abs deviation of spherical elevation vs tangent plane about origin: {0}m",
                           maxElevationDeviation);

            logger.LogInfo("max deviation orthographic vs orbital: {0}m", maxDeviationFromOrtho);
            logger.LogInfo("max in-plane deviation orthographic vs orbital: {0}m", maxInPlaneDeviationFromOrtho);
        }
    }
}

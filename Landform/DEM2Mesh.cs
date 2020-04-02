using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

/// <summary>
/// Utility to convert an orbital DEM to a mesh.
///
/// Example:
///
/// Landform.exe dem2mesh out_deltaradii_smg_1m.tif out_clean_25cm.iGrid.ClipToDEM.tif --mission MSL
///   --outputframe 0311472
/// </summary>
namespace OPS.Landform
{
    [Verb("dem2mesh", HelpText = "Convert a DEM and optional image to a mesh")]
    public class DEM2MeshOptions
    {
        [Value(0, Required = true, HelpText = "DEM image for mesh geometry")]
        public string InputDEM { get; set; }

        [Value(1, Required = false, HelpText = "Optional image to texture the mesh.  The image must be the same aspect and physical extent as the DEM, but can have a different resolution.")]
        public string InputImage { get; set; }

        [Option(Required = false, Default = "auto", HelpText = "Size of a pixel in the input DEM in meters, or \"auto\" to use mission default, or 1 if no mission.")]
        public string DEMMetersPerPixel { get; set; }

        [Option(Required = false, Default = "auto", HelpText = "Size of a pixel in the input image in meters, or \"auto\" to use mission default, or 1 if no mission.")]
        public string ImageMetersPerPixel { get; set; }

        [Option(Required = false, Default = "auto", HelpText = "Scale DEM values to vertical meters, or \"auto\" to use mission default, or 1 if no mission")]
        public string VerticalScale { get; set; }

        [Option(Required = false, Default = "png", HelpText = "Export format for texture (examples: jpg or png")]
        public string ImageFormat { get; set; }

        [Option(Required = false, Default = "obj", HelpText = "Export format for mesh (examples: obj or ply")]
        public string MeshFormat { get; set; }

        [Option(Required = false, Default = 0, HelpText = "Adaptive mesh to this error threshold.  Set to 0 to build a full organized mesh instead of adaptive meshing.")]
        public double MaxError { get; set; }

        [Option(Required = false, Default = DEM.DEF_MIN_FILTER, HelpText = "Dem values less than this will be ignored")]
        public double DEMMinFilter { get; set; }

        [Option(Required = false, Default = DEM.DEF_MAX_FILTER, HelpText = "Dem values larger than this will be ignored")]
        public double DEMMaxFilter { get; set; }

        [Option(Required = false, Default = "", HelpText = "Output to sitedrive frame SSSSSDDDDD or SSSDDDD, requires --mission, default puts origin at DEM center")]
        public string OutputFrame { get; set; }

        [Option(Required = false, Default = 200, HelpText = "Radius in meters around origin to build mesh, negative for unlimited")]
        public float Radius { get; set; }

        [Option(Required = false, Default = 0, HelpText = "If greater than one then decimate the input DEM and image by this blocksize")]
        public int DecimateBlocksize { get; set; }

        [Option(Required = false, Default = 4096, HelpText = "Maximum output texture resolution, 0 disables output texture, negative for unlimited")]
        public int MaxTextureResolution { get; set; }

        [Option(Required = false, Default = Mission.None, HelpText = "Mission flag enables mission specific behavior, e.g. None, MSL, M2020")]
        public Mission Mission { get; set; }

        [Option(Required = false, Default = false, HelpText = "Dry run")]
        public bool NoSave { get; set; }
    }

    public class DEM2Mesh
    {
        private static readonly ILog logger = LogManager.GetLogger("dem2mesh");

        private DEM2MeshOptions options;

        private MissionSpecific mission;

        private string meshExt, imageExt;
        private string outputMesh, outputImage;

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

                if (image != null)
                {
                    BuildAndSaveTexture();
                }

                BuildAndSaveMesh();
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
                return 1;
            }
            
            return 0;
        }

        private bool ParseArgumentsAndLoadInputs()
        {
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

            if (string.IsNullOrEmpty(options.InputDEM) || !File.Exists(options.InputDEM))
            {
                throw new Exception("input DEM not found: " + options.InputDEM);
            }

            outputMesh = Path.ChangeExtension(options.InputDEM, meshExt);

            //even if we don't directly use the mission instance
            //this has the important side effect of setting defaults for PlacesConfig and OrbitalConfig
            mission = MissionSpecific.GetInstance(options.Mission);

            demMetersPerPixel = 1;
            if (string.IsNullOrEmpty(options.DEMMetersPerPixel) || options.DEMMetersPerPixel.ToLower() == "auto")
            {
                demMetersPerPixel = OrbitalConfig.Instance.OrbitalDEMMetersPerPixel;
                if (mission == null)
                {
                    logger.WarnFormat("no mission, using default orbital DEM meters per pixel: {0}", demMetersPerPixel);
                }
            }
            else
            {
                demMetersPerPixel = double.Parse(options.DEMMetersPerPixel);
            }

            elevationScale = 1;
            if (string.IsNullOrEmpty(options.VerticalScale) || options.VerticalScale.ToLower() == "auto")
            {
                elevationScale = OrbitalConfig.Instance.OrbitalDEMElevationScale;
                if (mission == null)
                {
                    logger.WarnFormat("no mission, using default orbital DEM elevation scale: {0}", elevationScale);
                }
            }
            else
            {
                elevationScale = double.Parse(options.VerticalScale);
            }

            if (SiteDrive.IsSiteDriveString(options.OutputFrame))
            {
                if (mission == null)
                {
                    throw new Exception("--mission required for output in site drive frame");
                }

                dem = mission.LoadOrbitalDEM(new SiteDrive(options.OutputFrame), options.InputDEM,
                                             demMetersPerPixel, elevationScale,
                                             options.DEMMinFilter, options.DEMMaxFilter, new ThunkLogger(logger));
            }
            else
            {
                Vector2? originPixel = null; //DEM constructor will compute this as center of dem
                double? originElevation = null; //DEM constructor will look this up given originPixel
                dem = new DEM(new DEM.SparseDEM(options.InputDEM), demMetersPerPixel, elevationScale,
                              originPixel, originElevation, options.DEMMinFilter, options.DEMMaxFilter); 
            }

            if (!string.IsNullOrEmpty(options.InputImage) && options.MaxTextureResolution != 0)
            {
                imageMetersPerPixel = 1;
                if (string.IsNullOrEmpty(options.ImageMetersPerPixel) ||
                    options.ImageMetersPerPixel.ToLower() == "auto")
                {
                    imageMetersPerPixel = OrbitalConfig.Instance.OrbitalImageMetersPerPixel;
                    if (mission == null)
                    {
                        logger.WarnFormat("no mission, using default orbital image meters per pixel: {0}",
                                          imageMetersPerPixel);
                    }
                }
                else
                {
                    imageMetersPerPixel = double.Parse(options.ImageMetersPerPixel);
                }

                image = new DEM.SparseDEMImage(options.InputImage);
                outputImage = Path.Combine(Path.GetDirectoryName(outputMesh),
                                           Path.GetFileNameWithoutExtension(outputMesh) + "_texture" + imageExt);
            }

            if (options.DecimateBlocksize > 1)
            {
                dem = dem.Decimated(options.DecimateBlocksize);
                if (image != null)
                {
                    image = image.Decimated(options.DecimateBlocksize);
                }
            }

            return true;
        }

        private void BuildAndSaveTexture()
        {
            var texture = image;
            int maxRes = options.MaxTextureResolution;
            if (options.Radius < 0)
            {
                double maxDim = Math.Max(texture.Width, texture.Height);
                if (maxRes > 0 && maxDim > maxRes)
                {
                    double s = maxRes / maxDim;
                    int w = (int)Math.Floor(texture.Width * s);
                    int h = (int)Math.Floor(texture.Height * s);
                    logger.InfoFormat("resizing {0}x{1} texture to {2}x{3}", texture.Width, texture.Height, w, h);
                    texture = texture.Resize(w, h);
                }
            }
            else
            {
                double imagePixelsPerDemPixel = demMetersPerPixel / imageMetersPerPixel;
                Vector2 originPixel = dem.OriginPixel * imagePixelsPerDemPixel;

                double imageMPP = imageMetersPerPixel * (options.DecimateBlocksize > 1 ? options.DecimateBlocksize : 1);

                var subrect = texture.GetSubrect(originPixel, options.Radius / imageMPP);

                double maxDim = Math.Max(subrect.Width, subrect.Height);
                if (maxRes > 0 && maxDim > maxRes)
                {
                    double s = maxRes / maxDim;
                    int w = (int)Math.Floor(subrect.Width * s);
                    int h = (int)Math.Floor(subrect.Height * s);
                    logger.InfoFormat("resampling {0}x{1} texture subrect to {2}x{3}",
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
                    logger.InfoFormat("cropping {0}x{1} subrect from {2}x{3} texture",
                                      subrect.Width, subrect.Height, texture.Width, texture.Height);
                    texture = texture.Crop(subrect);
                }
            }

            logger.InfoFormat("{0}saving {1}x{2} texture {3}",
                              options.NoSave ? "not " : "", texture.Width, texture.Height, outputImage);
            if (!options.NoSave)
            {
                texture.Save<byte>(outputImage);
            }
        }

        private void BuildAndSaveMesh()
        {
            logger.InfoFormat("{0} meshing DEM, radius {1}",
                              options.MaxError == 0 ? "organized" : "adaptive", options.Radius);
            
            var mesh = options.MaxError == 0 ?
                dem.OrganizedMesh(options.Radius, withUV: true) :
                dem.AdaptiveMesh(options.MaxError, options.Radius, withUV: true);
            
            logger.InfoFormat("{0}saving {1} triangle mesh {2}",
                              options.NoSave ? "not " : "", Fmt.KMG(mesh.Faces.Count), outputMesh);
            if (!options.NoSave)
            {
                mesh.Save(outputMesh, image != null ? Path.GetFileName(outputImage) : null);
            }
        }
    }
}

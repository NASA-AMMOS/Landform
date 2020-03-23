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

namespace OPS.LandformUtil
{
    [Verb("dem2mesh", HelpText = "Convert a DEM and optional image to a mesh")]
    public class DEM2MeshOptions
    {
        [Value(0, Required = true, HelpText = "DEM image for mesh geometry")]
        public string InputDEM { get; set; }

        [Value(1, Required = false, HelpText = "Optional image to texture the mesh.  The image must be the same aspect and physical extent as the DEM, but can have a different resolution.")]
        public string InputImage { get; set; }

        [Option(Required = false, Default = "auto", HelpText = "Size of a pixel in the DEM in meters, or \"auto\" to use mission default, or 1 if no mission.")]
        public string MetersPerPixel { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Scale DEM values to vertical meters, ignored if zero")]
        public double VerticalScale { get; set; }

        [Option(Required = false, Default = "png", HelpText = "Export format for texture (examples: jpg or png")]
        public string ImageFormat { get; set; }

        [Option(Required = false, Default = "obj", HelpText = "Export format for mesh (examples: obj or ply")]
        public string MeshFormat { get; set; }

        [Option(Required = false, Default = 0, HelpText = "Decimate (roughly) to this error threshold against original points. Error 0 is the special case in which the full grid mesh is built (no sampling/decimation).")]
        public double Error { get; set; }

        [Option(Required = false, Default = DEM.DEF_MIN_FILTER, HelpText = "Dem values less than this will be ignored")]
        public double DEMMinFilter { get; set; }

        [Option(Required = false, Default = DEM.DEF_MAX_FILTER, HelpText = "Dem values larger than this will be ignored")]
        public double DEMMaxFilter { get; set; }

        [Option(Required = false, Default = "", HelpText = "Output to sitedrive frame SSSSSDDDDD or SSSDDDD, default puts origin at DEM center")]
        public string OutputFrame { get; set; }

        [Option(Required = false, Default = 200, HelpText = "Radius in meters around origin to build mesh, negative for unlimited")]
        public float Radius { get; set; }

        [Option(Required = false, Default = Mission.None, HelpText = "Mission flag enables mission specific behavior, e.g. None, MSL, M2020")]
        public Mission Mission { get; set; }

        // TODO: Skirt option?
    }

    public class DEM2Mesh
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(DEM2Mesh));

        private DEM2MeshOptions options;

        private MissionSpecific mission;

        private string meshExt, imageExt;
        private string outputMesh, outputImage;

        private double metersPerPixel;

        private DEM dem;

        public DEM2Mesh(DEM2MeshOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadDEM())
                {
                    return 0; //help
                }

                var mesh = options.Error == 0 ?
                    dem.OrganizedMesh(options.Radius, withUV: true) :
                    dem.DecimatedMesh(options.Error, options.Radius, withUV: true);

                mesh.Save(outputMesh, outputImage);

                if (outputImage != null)
                {
                    if (options.Radius >= 0)
                    {
                        //TODO: Properly clip ortho when radius option is set
                        logger.Warn("clipping image to radius not implemented, using full texture image");
                    }
                    Image.Load(options.InputImage).Save<byte>(outputImage);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
                return 1;
            }
            
            return 0;
        }

        private bool ParseArgumentsAndLoadDEM()
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

            //even if we don't directly use the mission instance
            //this has the important side effect of setting defaults for PlacesConfig and OrbitalConfig
            mission = MissionSpecific.GetInstance(options.Mission);

            if (string.IsNullOrEmpty(options.MetersPerPixel) || options.MetersPerPixel.ToLower() == "auto")
            {
                metersPerPixel = OrbitalConfig.Instance.OrbitalDEMMetersPerPixel;
                if (mission == null)
                {
                    logger.WarnFormat("no mission, using default orbital DEM meters per pixel: {0}", metersPerPixel);
                }
            }
            else
            {
                metersPerPixel = double.Parse(options.MetersPerPixel);
            }

            if (SiteDrive.IsSiteDriveString(options.OutputFrame))
            {
                if (mission == null)
                {
                    throw new Exception("--mission required for output in site drive frame");
                }

                dem = mission.LoadOrbital(new SiteDrive(options.OutputFrame), options.InputDEM, metersPerPixel,
                                          new ThunkLogger(logger));
            }
            else
            {
                var img = new DEM.SparseDEMImage(options.InputDEM);
                var cmod = DEM.DefaultOrbitalCameraModel(img.Width, img.Height, metersPerPixel);
                dem = new DEM(img, cmod);
            }

            if (options.VerticalScale != 0)
            {
                dem.ScaleValues(options.VerticalScale);
            }

            outputMesh = Path.ChangeExtension(options.InputDEM, options.MeshFormat);

            if (!string.IsNullOrEmpty(options.InputImage))
            {
                outputImage = Path.ChangeExtension(outputMesh, options.ImageFormat);
            }

            return true;
        }
    }
}

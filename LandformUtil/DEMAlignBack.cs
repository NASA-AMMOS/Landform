//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.IO;
//using Microsoft.Xna.Framework;
//using CommandLine;
//using OPS.Util;
//using OPS.Imaging;
//using OPS.Geometry;
//using OPS.Pipeline;

//namespace OPS.LandformUtil
//{
//    [Verb("demalign", HelpText = "Convert a dem and optional ortho image to a mesh.  X North (points toward top of dem image space), Y up (values from dem), Z East (points right in dem image space)")]
//    public class DEMAlignOptions
//    {
//        [Value(0, Required = true, Default = 1, HelpText = "Size of a pixel in the DEM in meters")]
//        public double MetersPerPixel { get; set; }

//        [Value(1, Required = true, HelpText = "Image containing heights as values")]
//        public string InputDem { get; set; }

//        [Option(Required = true, Default = "", HelpText = "Scene in given sitedrive frame to align with")]
//        public string AlignToScene { get; set; }

//        [Option(Required = true, Default = "", HelpText = "Output to sitedrive frame SSSSSDDDDD")]
//        public string OutputFrame { get; set; }

//        [Value(2, Required = false, HelpText = "Optional input ortho image.  The image must be the same aspect and physical extent as the DEM, but can have a different resolution.")]
//        public string InputOrthoImage { get; set; }

//        [Option(Required = false, Default = 1, HelpText = "Scaler to convert dem  values to verticle meters.  i.e. (meters/pixel value)")]
//        public float VerticalScale { get; set; }

//        [Option(Required = false, HelpText = "Output path of mesh.  If ortho image is supplied it will be written to the same path but with a different extension.  If ommited output is written to same directory as input but with a '.mesh' appended to the filename")]
//        public string OutputPath { get; set; }

//        [Option(Required = false, Default = "png", HelpText = "Export format for textures (examples: jpg or png")]
//        public string ImageFormat { get; set; }

//        [Option(Required = false, Default = "obj", HelpText = "Export format for mesh (examples: obj or ply")]
//        public string MeshFormat { get; set; }

//        [Option(Required = false, Default = -1000000, HelpText = "Dem values less than this will be ignored")]
//        public float DEMMinFilter { get; set; }

//        [Option(Required = false, Default = 1000000, HelpText = "Dem values larger than this will be ignored")]
//        public float DEMMaxFilter { get; set; }

//        [Option(Required = false, Default = "", HelpText = "Path to save in memory heightmap created for AlignToScene mesh, default does not save")]
//        public string WriteHeightmapPath { get; set; }

//        [Option(Required =false, Default = 200, HelpText = "Radius in meters around origin to build mesh")]
//        public float Radius { get; set; }
//    }

//    public class DEMAlign
//    {
//        DEMAlignOptions options;

//        //x = col, y = row
//        Vector3 colRowOffset;
//        double zOffset;

//        public DEMAlign(DEMAlignOptions options)
//        {
//            this.options = options;
//            int site = 0;
//            int drive = 0;
//            bool success = options.OutputFrame.Length == 10 &&
//                           Int32.TryParse(options.OutputFrame.Substring(0, 5), out site) &&
//                           Int32.TryParse(options.OutputFrame.Substring(5, 5), out drive);
//            if (!success)
//            {
//                throw new Exception("Failed to parse sitedrive from OutputFrame.");
//            }
//            MSLPlaces places = new MSLPlaces();
//            Vector2 latlon = places.GetEstimatedLatLon(new SiteDrive(site, drive));
//            GDALDEM dem = GDALDEM.MarsDEM(options.InputDem);
//            colRowOffset = dem.LatLonToImage(new Vector3(latlon.Y, latlon.X, 0));
//            zOffset = dem.InterpolateElevationAtLatLon(latlon.X, latlon.Y);       
//        }

//        /// <summary>
//        /// Create a mesh from input dem with parameters given by command line args
//        /// </summary>
//        /// <returns></returns>
//        public int Run()
//        {
//            if (!string.IsNullOrEmpty(this.options.OutputPath))
//            {
//                PathHelper.EnsureExists(this.options.OutputPath);
//            }
//            else 
//            {
//                this.options.OutputPath = Path.Combine(Path.GetDirectoryName(options.InputDem), 
//                    Path.GetFileNameWithoutExtension(options.InputDem) + ".aligned_cloud." + options.MeshFormat);
//            }

//            ImageSerializer s = ImageSerializers.Instance.GetSerializer(Path.GetExtension(options.InputDem));
//            if (s.GetType() != typeof(GDALSerializer))
//            {
//                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
//            }
//            ((GDALSerializer)s).GetMetadata(options.InputDem, out int bands, out int width, out int height);

//            //Read in the dem, in chunks if too large
//            var dem = new SparseDEMImage(options.InputDem);          
//            if (dem.CameraModel == null)
//            {
//                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, options.MetersPerPixel);
//            }

//            Mesh scene = Mesh.Load(options.AlignToScene);
//            var box = scene.Bounds();
//            int w = (int)Math.Ceiling((box.Max.X - box.Min.X) / options.MetersPerPixel);
//            int h = (int)Math.Ceiling((box.Max.Y - box.Min.Y) / options.MetersPerPixel);
//            Matrix siteDriveTransform = Matrix.Identity;
//            siteDriveTransform = DemOperations.Align(scene, dem, colRowOffset.Y, colRowOffset.X, w, h, 
//                options.MetersPerPixel, out List<Vector3> samples, zOffset, 1.0, options.WriteHeightmapPath);

//            //Get subset of dem around sitedrive
//            int pixelRadius = (int)(options.Radius / options.MetersPerPixel);
//            int baseC = (int) Math.Max(colRowOffset.X - pixelRadius, 0);
//            int baseR = (int) Math.Max(colRowOffset.Y - pixelRadius, 0);
//            int pixelWidth = (int)Math.Min(colRowOffset.X + pixelRadius, dem.Width) - baseC;
//            int pixelHeight = (int)Math.Min(colRowOffset.Y + pixelRadius, dem.Height) - baseR;

//            Mesh pointCloud = new Mesh();
//            for (int r = 0; r < pixelHeight; r++)
//            {
//                for (int c = 0; c < pixelWidth; c++)
//                {
//                    var pos = DemOperations.GetXYZ(dem, baseR + r, baseC + c, options.VerticalScale);
//                    if (pos.HasValue)
//                    {
//                        Vertex v = new Vertex();
//                        v.Position = Vector3.Transform(pos.Value, siteDriveTransform);
//                        v.UV = dem.PixelToUV(new Vector2(baseC + c, baseR + r));
//                        pointCloud.Vertices.Add(v);
//                    }
//                }
//            }
//            Mesh mesh = Delaunay.Triangulate(pointCloud.Vertices); //for debug

//            string outputImage = null;
//            if (options.InputOrthoImage != null)
//            {
//                //TODO: Properly clip ortho and map uvs
//                Image ortho = Image.Load(options.InputOrthoImage);
//                outputImage = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.ImageFormat);
//                ortho.Save<byte>(outputImage); // TODO, add support for matching input type
//            }
//            pointCloud.HasUVs = true;
//            pointCloud.Save(this.options.OutputPath, outputImage);
//            mesh.HasUVs = true;
//            mesh.Save(Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.MeshFormat), outputImage);
//            return 0;
//        }
//    }
//}

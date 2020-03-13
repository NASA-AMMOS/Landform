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

namespace OPS.LandformUtil
{
    [Verb("dem2mesh", HelpText = "Convert a dem and optional ortho image to a mesh.  X North (points toward top of dem image space), Y up (values from dem), Z East (points right in dem image space)")]
    public class DEM2MeshOptions
    {
        [Value(0, Required = true, Default = 1, HelpText = "Size of a pixel in the DEM in meters")]
        public double MetersPerPixel { get; set; }

        [Value(1, Required = true, HelpText = "Image containing heights as values")]
        public string InputDem { get; set; }

        [Value(2, Required = false, HelpText = "Optional input ortho image.  The image must be the same aspect and physical extent as the DEM, but can have a different resolution.")]
        public string InputOrthoImage { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Scaler to convert dem  values to verticle meters.  i.e. (meters/pixel value)")]
        public float VerticalScale { get; set; }

        [Option(Required = false, HelpText = "Output path of mesh.  If ortho image is supplied it will be written to the same path but with a different extension.  If ommited output is written to same directory as input but with a '.mesh' appended to the filename")]
        public string OutputPath { get; set; }

        [Option(Required = false, Default = "png", HelpText = "Export format for textures (examples: jpg or png")]
        public string ImageFormat { get; set; }

        [Option(Required = false, Default = "obj", HelpText = "Export format for mesh (examples: obj or ply")]
        public string MeshFormat { get; set; }

        [Option(Required = false, Default = 0, HelpText = "Decimate (roughly) to this error threshold against original points. Error 0 is the special case in which the full grid mesh is built (no sampling/decimation needed)")]
        public float Error { get; set; }

        [Option(Required = false, Default = 30, HelpText = "Number of points to add with each split")]
        public int SampleNum { get; set; }

        [Option(Required = false, Default = 4, HelpText = "Number of points to test error against at each split")]
        public int TestNum { get; set; }

        [Option(Required = false, Default = 2.0, HelpText = "Set higher to scale the sampling region to smooth transition between high and low density")]
        public double SampleRegionScale { get; set; }

        [Option(Required = false, Default = -1000000, HelpText = "Dem values less than this will be ignored")]
        public float DEMMinFilter { get; set; }

        [Option(Required = false, Default = 1000000, HelpText = "Dem values larger than this will be ignored")]
        public float DEMMaxFilter { get; set; }

        [Option(Required = false, Default = "", HelpText = "Output to sitedrive frame SSSSSDDDDD, default puts origin at dem center")]
        public string OutputFrame { get; set; }

        [Option(Required = false, Default = "", HelpText = "Scene in given sitedrive frame to align with")]
        public string AlignToScene { get; set; }

        [Option(Required = false, Default = "", HelpText = "Path to save in memory heightmap created for AlignToScene mesh, default does not save")]
        public string WriteHeightmapPath { get; set; }

        [Option(Required =false, Default = -1, HelpText = "Radius in meters around origin to build mesh")]
        public float Radius { get; set; }

        // TODO: Skirt option?
    }

    public class DEM2Mesh
    {
        DEM2MeshOptions options;

        bool useSiteDriveFame;
        //x = col, y = row
        Vector3 colRowOffset;
        double zOffset;

        public DEM2Mesh(DEM2MeshOptions options)
        {
            this.options = options;
            if(options.OutputFrame.Length == 10)
            {
                int site = 0;
                int drive = 0;
                useSiteDriveFame = Int32.TryParse(options.OutputFrame.Substring(0, 5), out site) &&
                                   Int32.TryParse(options.OutputFrame.Substring(5, 5), out drive);
                if (useSiteDriveFame)
                {
                    var placesDB = new PlacesDB();
                    Vector2 latlon = placesDB.GetEstimatedLatLon(new SiteDrive(site, drive));
                    GDALDEM dem = GDALDEM.MarsDEM(options.InputDem);
                    colRowOffset = dem.LatLonToImage(new Vector3(latlon.Y, latlon.X, 0));
                    zOffset = dem.InterpolateElevationAtLatLon(latlon.X, latlon.Y);
                }
            } else
            {
                useSiteDriveFame = false;
            }
        }

        private const long MAX_SINGLE_CHUNK_SIZE = 10000; //If input dem width x height is larger than this value squared, chunk the input and use SparseImage w/ cache to limit memory consumption   

        /// <summary>
        /// Recursively subsample regions where geometric error is too large
        /// </summary>
        /// <param name="rowCols"></param>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="error"></param>
        /// <param name="xyz"></param>
        /// <param name="original_mask"></param>
        /// <param name="mask"></param>
        /// <param name="size"></param>
        /// <param name="sampleNum"></param>
        /// <param name="testNum"></param>
        /// <param name="sampleScale"></param>
        /// <param name="rand"></param>
        /// <returns></returns>
        List<Vector2> Split(List<Vector2> rowCols, double r, double c, double width, double height, double error, Image dem, Mask mask, double scale, int sampleNum, int testNum, double sampleScale, Random rand)
        {
            //Mesh the current set of vertices
            var verts = rowCols.Select(rc => new Vertex(DemOperations.GetXYZ(dem, null, (int)rc.Y, (int)rc.X, scale).Value)).ToArray();
            Mesh mesh = Delaunay.Triangulate(verts);

            //Sample
            List<Vector2> newRowCols = new List<Vector2>();
            double tested = 0;
            bool shouldSplit = false;
            for (int i = 0; i < sampleNum; i++)
            {
                int testR = (int)(r + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * height);
                int testC = (int)(c + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * width);
                Vector3? v = DemOperations.GetXYZ(dem, mask, testR, testC, scale);
                if(v.HasValue)
                {
                    mask.setInvalid(testR, testC); //Prevent point from being resampled
                    newRowCols.Add(new Vector2(testC, testR));
                    
                    //Test error between mesh and samples
                    if(!shouldSplit && tested < testNum && testR > r && testR < r + height && testC > c && testC < c + width)
                    {
                        double dist = double.MaxValue;
                        List<Triangle> tris = mesh.Triangles();
                        foreach (Triangle t in tris)
                        {
                            double tmp = t.SquaredDistance(v.Value);
                            dist = Math.Min(tmp, dist);
                        }
                        if(dist > error)
                        {
                            shouldSplit = true;
                        }
                        tested++;
                    }
                }
            }

            //Subsample if error exceeded threshold
            if(!shouldSplit)
            {
                return newRowCols;
            }

            //Compute new child tile bounds
            Vector3? tl = DemOperations.Interpolate(c - Math.Floor(c), r - Math.Floor(r),
                DemOperations.GetXYZ(dem, null, (int)Math.Floor(r), (int)Math.Floor(c), scale, false),
                DemOperations.GetXYZ(dem, null, (int)Math.Floor(r), (int)Math.Ceiling(c), scale, false),
                DemOperations.GetXYZ(dem, null, (int)Math.Ceiling(r), (int)Math.Floor(c), scale, false),
                DemOperations.GetXYZ(dem, null, (int)Math.Ceiling(r), (int)Math.Ceiling(c), scale, false));
            double r1 = Math.Min(r + height, dem.Height - 1);
            double c1 = Math.Min(c + width, dem.Width - 1);
            Vector3? br = DemOperations.Interpolate(c1 - Math.Floor(c1), r1 - Math.Floor(r1),
                DemOperations.GetXYZ(dem, null, (int)Math.Floor(r1), (int)Math.Floor(c1), scale, false),
                DemOperations.GetXYZ(dem, null, (int)Math.Floor(r1), (int)Math.Ceiling(c1), scale, false),
                DemOperations.GetXYZ(dem, null, (int)Math.Ceiling(r1), (int)Math.Floor(c1), scale, false),
                DemOperations.GetXYZ(dem, null, (int)Math.Ceiling(r1), (int)Math.Ceiling(c1), scale, false));
            if (!tl.HasValue || !br.HasValue)
            {
                throw new Exception("Failed to get tile corner");
            }
            double minX = tl.Value.X;
            double maxY = tl.Value.Y;
            double maxX = br.Value.X;
            double minY = br.Value.Y;

            double midX = (minX + maxX) / 2.0;
            double midY = (minY + maxY) / 2.0;
            double umidX = (minX + maxX + (maxX - minX) * 0.1) / 2.0;
            double lmidX = (minX + maxX - (maxX - minX) * 0.1) / 2.0;
            double umidY = (minY + maxY + (maxY - minY) * 0.1) / 2.0;
            double lmidY = (minY + maxY - (maxY - minY) * 0.1) / 2.0;

            //Add boundary conditions to each tile child (try to find approximate tile corners, and include full dem corners in case of failure)
            List<Vector2> vIdxs1 = DemOperations.FindCorners(dem, (int)r, (int)c, (int)((width - 1)/2), (int)((height - 1) / 2));
            List<Vector2> vIdxs2 = DemOperations.FindCorners(dem, (int)(r + height / 2), (int)c, (int)((width - 1) / 2), (int)((height - 1) / 2));
            List<Vector2> vIdxs3 = DemOperations.FindCorners(dem, (int)r, (int)(c + width / 2), (int)((width - 1) / 2), (int)((height - 1) / 2));
            List<Vector2> vIdxs4 = DemOperations.FindCorners(dem, (int)(r + height / 2), (int)(c + width / 2), (int)((width - 1)/2), (int)((height - 1) / 2));
            vIdxs1.AddRange(rowCols.GetRange(0, 4));
            vIdxs2.AddRange(rowCols.GetRange(0, 4));
            vIdxs3.AddRange(rowCols.GetRange(0, 4));
            vIdxs4.AddRange(rowCols.GetRange(0, 4));

            //Partition our current set of vertices + new samples into children
            foreach (Vector2 rc in rowCols.Union(newRowCols))
            {
                Vector3 v = DemOperations.GetXYZ(dem, null, (int)rc.Y, (int)rc.X, scale).Value;
                if(v.X < umidX)
                {
                    if(v.Y > lmidY)
                    {
                        vIdxs1.Add(rc);
                    }
                    if(v.Y < umidY)
                    {
                        vIdxs2.Add(rc);
                    }
                }
                if(v.X > lmidX)
                {
                    if(v.Y > lmidY)
                    {
                        vIdxs3.Add(rc);
                    }
                    if(v.Y < umidY)
                    {
                        vIdxs4.Add(rc);
                    }
                }
            }

            //Recurse on children
            newRowCols.AddRange(Split(vIdxs1, r, c, width/2.0, height/2.0, error, dem, mask, scale, sampleNum, testNum, sampleScale, rand));
            newRowCols.AddRange(Split(vIdxs2, r + height/2.0, c, width / 2.0, height / 2.0, error, dem, mask, scale, sampleNum, testNum, sampleScale, rand));
            newRowCols.AddRange(Split(vIdxs3, r, c + width / 2.0, width / 2.0, height / 2.0, error, dem, mask, scale, sampleNum, testNum, sampleScale, rand));
            newRowCols.AddRange(Split(vIdxs4, r + height / 2.0, c + width / 2.0, width / 2.0, height / 2.0, error, dem, mask, scale, sampleNum, testNum, sampleScale, rand));

            return newRowCols;
        }       

        /// <summary>
        /// Create a mesh from input dem with parameters given by command line args
        /// </summary>
        /// <returns></returns>
        public int Run()
        {
            if(!string.IsNullOrEmpty(this.options.OutputPath))
            {
                PathHelper.EnsureExists(this.options.OutputPath);
            }
            else 
            {
                this.options.OutputPath = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.MeshFormat);
            }

            //TODO: Get Metadata without requiring GDAL
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(Path.GetExtension(options.InputDem));
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
            }
            ((GDALSerializer)s).GetMetadata(options.InputDem, out int bands, out int width, out int height);

            //Read in the dem, in chunks if too large
            Image dem = null;
            bool useHashForMask;
            if((long)width * (long)height > MAX_SINGLE_CHUNK_SIZE * MAX_SINGLE_CHUNK_SIZE || options.Radius != -1)
            {
                dem = new SparseDEMImage(options.InputDem);
                useHashForMask = true;
            }
            else
            {
                dem = Image.Load(options.InputDem, ImageConverters.PassThrough);
                useHashForMask = false;
            }    
            
            if (dem.CameraModel == null)
            {
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, options.MetersPerPixel);
            }

            Mesh mesh = new Mesh();
            //No decimation:
            //  Convert the entire dem to xyz's with mask
            //  Build the mesh by connecting neighboring points with a regular grid of tris  

            if (options.Error == 0 && options.Radius == -1)
            {
                dem.ScaleValues(options.VerticalScale);
                Image xyz = null;
                Image mask = new Image(1, dem.Width, dem.Height);

                if (dem.Bands == 3)
                {
                    xyz = dem;  // Unusual but handle the case where we are passed a 3 band xyz image instead of a dem
                    for (int row = 0; row < dem.Height; row++)
                    {
                        for (int col = 0; col < dem.Width; col++)
                        {
                            //Mask out (0,0,0) points as they inidcate missing value.
                            //See msl cam sis 5.2.1.4 for detailed description.
                            //Same for M2020 as of 3/12/20.
                            mask[0, row, col] = (xyz[0, row, col] == 0 &&
                                                 xyz[1, row, col] == 0 &&
                                                 xyz[2, row, col] == 0) ? 0 : 1;
                        }
                    }
                }
                else
                {
                    xyz = new Image(3, dem.Width, dem.Height);
                    for (int row = 0; row < dem.Height; row++)
                    {
                        for (int col = 0; col < dem.Width; col++)
                        {
                            Vector3? v = DemOperations.GetXYZ(dem, null, row, col);
                            if (v.HasValue)
                            {
                                xyz[0, row, col] = (float)v.Value.X;
                                xyz[1, row, col] = (float)v.Value.Y;
                                xyz[2, row, col] = (float)v.Value.Z;
                                mask[0, row, col] = 1;
                            }
                            else
                            {
                                mask[0, row, col] = 0;
                            }
                        }
                    }
                }
                mesh = OrganizedPointCloud.BuildOrganizedMesh(xyz, mask:mask);
            }
            //Build decimated mesh by iterative sampling:
            // Start with two tris that connect the dem corners
            // Test error and sample regions that need subdividing (currently quad scheme)
            else
            {
                List<Vector2> rowCols;
                //This mask is only used to avoid resampling the same point. Invalid point data is masked out by the GetXYZ function (computed lazily)
                Mask mask;
                double squaredError = options.Error * options.Error;
                if (options.Radius != -1)
                {
                    if(!useSiteDriveFame)
                    {
                        //set origin to image center
                        colRowOffset = new Vector3(width / 2.0, height/ 2.0, 0);
                        zOffset = 0;
                    }
                    //Mesh subset of dem around sitedrive
                    int pixelRadius = (int)(options.Radius / options.MetersPerPixel);
                    int baseC = (int) Math.Max(colRowOffset.X - pixelRadius, 0);
                    int baseR = (int) Math.Max(colRowOffset.Y - pixelRadius, 0);
                    int pixelWidth = (int)Math.Min(colRowOffset.X + pixelRadius, dem.Width) - baseC;
                    int pixelHeight = (int)Math.Min(colRowOffset.Y + pixelRadius, dem.Height) - baseR;
                    //Always use hashset to avoid building full mask for partial dem
                    mask = new Mask(dem.Width, dem.Height, true);             
                    if (options.Error != 0)
                    {
                        rowCols = DemOperations.FindCorners(dem, baseR, baseC, pixelWidth - 1, pixelHeight - 1);
                        rowCols.AddRange(Split(rowCols, baseR, baseC, pixelWidth, pixelHeight, squaredError, dem, mask, options.VerticalScale, options.SampleNum, options.TestNum, options.SampleRegionScale, OPS.Util.NumberHelper.MakeRandomGenerator()));
                        //Split allows sampling outside the bounds to smooth density transitions, so filter to our restricted bounds at the end
                        rowCols = rowCols.Where((rc) => rc.X >= baseC && rc.X < baseC + pixelWidth && rc.Y >= baseR && rc.Y < baseR + pixelHeight).ToList();
                    } else
                    {
                        rowCols = new List<Vector2>();
                        for(int r = 0; r < pixelHeight; r++)
                        {
                            for(int c = 0; c < pixelWidth; c++)
                            {
                                rowCols.Add(new Vector2(baseC + c, baseR + r));
                            }
                        }
                    }
                }
                else
                {
                    //Mesh the full dem
                    mask = new Mask(dem.Width, dem.Height, useHashForMask);
                    rowCols = DemOperations.FindCorners(dem);
                    rowCols.AddRange(Split(rowCols, 0, 0, dem.Width, dem.Height, squaredError, dem, mask, options.VerticalScale, options.SampleNum, options.TestNum, options.SampleRegionScale, OPS.Util.NumberHelper.MakeRandomGenerator()));
                }
                //Construct vertices
                var verts = rowCols.Select(rc => {
                    var v = new Vertex(DemOperations.GetXYZ(dem, (int)rc.Y, (int)rc.X, options.VerticalScale).Value);
                    v.UV = dem.PixelToUV(rc);
                    return v;
                    }).ToArray();
                mesh = Delaunay.Triangulate(verts);
            }

            Matrix siteDriveTransform = Matrix.Identity;
            List<Vector3> samples = new List<Vector3>();
            if (useSiteDriveFame)
            {
                if(options.AlignToScene != "")
                {
                    throw new Exception("Deprecated - Use OrbitalAligner.");
                } else
                {
                    //Shift image origin and apply vertical offset based on places priors
                    siteDriveTransform = Matrix.CreateTranslation(options.MetersPerPixel * (-1 * colRowOffset.X + (double)width / 2.0), options.MetersPerPixel * (colRowOffset.Y - (double)height / 2.0), -1 * zOffset);
                }
            }

            foreach (Vertex v in mesh.Vertices)
            {
                v.Position = Vector3.Transform(v.Position, siteDriveTransform);
                //Invert Z
                v.Position.Z *= -1;
                //Swap X Y
                double temp = v.Position.X;
                v.Position.X = v.Position.Y;
                v.Position.Y = temp;
            }

            string outputImage = null;
            if (options.InputOrthoImage != null)
            {
                //TODO: Properly clip ortho when radius option is set
                Image ortho = Image.Load(options.InputOrthoImage);
                outputImage = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.ImageFormat);
                ortho.Save<byte>(outputImage); // TODO, add support for matching input type
            }
            mesh.HasUVs = true;
            mesh.Save(this.options.OutputPath, outputImage);
            return 0;
        }
    }
}

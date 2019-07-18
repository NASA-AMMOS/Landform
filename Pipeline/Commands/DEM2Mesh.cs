using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using CommandLine;

namespace OPS.Pipeline
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
                    MSLPlaces places = new MSLPlaces();
                    places.GetEstimatedLatLon(new SiteDrive(site, drive), out Vector2 latlon);
                    GDALDEM dem = GDALDEM.MarsDEM(options.InputDem);
                    colRowOffset = dem.LatLonToImage(new Vector3(latlon.Y, latlon.X, 0));
                    zOffset = dem.InterpolateElevationAtLatLon(latlon.X, latlon.Y);
                }
            } else
            {
                useSiteDriveFame = false;
            }
        }

        Func<Vertex, DelaunayPoint> vertToDelaunay = v =>
        {
            DelaunayPoint p = new DelaunayPoint();
            p.xy = new Vector2(v.Position.X, v.Position.Y);
            p.height = v.Position.Z;
            return p;
        };

        private const long MAX_SINGLE_CHUNK_SIZE = 100000; //If input dem width x height is larger than this value squared, chunk the input and use SparseImage w/ cache to limit memory consumption

        /// <summary>
        /// Given Image dem, find corners that are not masked out. Optionally enter top left corner and a size parameter to get corners of a subregion.
        /// May not return a full set of vertices (potentially none) if image heavily masked
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="mask"></param>
        /// <param name="minR"></param>
        /// <param name="minC"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        IEnumerable<Vector2> FindCorners(Image dem, PDSParser parser, int minR = 0, int minC = 0, int width = -1, int height = -1)
        {
            int d = 0;
            bool foundTopLeft = false;
            bool foundTopRight = false;
            bool foundBotLeft = false;
            bool foundBotRight = false;
            if (width == -1)
            {
                width = dem.Width - minC - 1;
            }
            if (height == -1)
            {
                height = dem.Height - minR - 1;
            }

            while (d < Math.Min(width, height) && (!foundTopLeft || !foundTopRight || !foundBotLeft || !foundBotRight))
            {
                int c;
                for (int r = 0; r <= d; r++)
                {
                    c = d - r;
                    if (!foundTopLeft)
                    {
                        Vector3? tl = GetXYZ(dem, null, minR + r, minC + c, parser);
                        if (tl.HasValue)
                        {
                            foundTopLeft = true;
                            yield return new Vector2(minC + c, minR + r);
                        }
                    }
                    if (!foundTopRight)
                    {
                        Vector3? tr = GetXYZ(dem, null, minR + r, minC + width - c, parser);
                        if (tr.HasValue)
                        {
                            foundTopRight = true;
                            yield return new Vector2(minC + width - c, minR + r);
                        }
                    }
                    if (!foundBotLeft)
                    {
                        Vector3? bl = GetXYZ(dem, null, minR + height - r, minC + c, parser);
                        if (bl.HasValue)
                        {
                            foundBotLeft = true;
                            yield return new Vector2(minC + c, minR + height - r);
                        }
                    }
                    if (!foundBotRight)
                    {
                        Vector3? br = GetXYZ(dem, null, minR + height - r, minC + width - c, parser);
                        if (br.HasValue)
                        {
                            foundBotRight = true;
                            yield return new Vector2(minC + width - c, minR + height - r);
                        }
                    }
                }
                ++d;
            }
        }

        /// <summary>
        /// Do bilinear interpolation with potentially null points.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="tl"></param>
        /// <param name="tr"></param>
        /// <param name="bl"></param>
        /// <param name="br"></param>
        /// <returns></returns>
        Vector3? Bilinear(double x, double y, Vector3? tl, Vector3? tr, Vector3? bl, Vector3? br)
        {
            Vector3 ret = new Vector3(0,0,0);
            double area = 0;
            if(tl.HasValue)
            {
                ret += tl.Value * x * y;
                area += x * y;
            }
            if (tr.HasValue)
            {
                ret += tr.Value * (1 - x) * y;
                area += (1 - x) * y;
            }
            if (bl.HasValue)
            {
                ret += bl.Value * x * (1 - y);
                area += x * (1 - y);
            }
            if (br.HasValue)
            {
                ret += br.Value * (1 - x) * (1 - y);
                area += (1 - x) * (1 - y);
            }
            if (area > 0)
            {
                return ret / area;
            } else
            {
                return null;
            }
        }

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
        List<Vector2> Split(List<Vector2> rowCols, double r, double c, double width, double height, double error, Image dem, Mask mask, PDSParser parser, int sampleNum, int testNum, double sampleScale, Random rand)
        {
            //Mesh the current set of vertices
            Mesh mesh = DelaunayTriangulation.Triangulate(rowCols.Select(rc => new Vertex(GetXYZ(dem,null,(int)rc.Y,(int)rc.X,parser).Value)).ToArray(), vertToDelaunay);

            //Sample
            List<Vector2> newRowCols = new List<Vector2>();
            double tested = 0;
            bool shouldSplit = false;
            for (int i = 0; i < sampleNum; i++)
            {
                int testR = (int)(r + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * height);
                int testC = (int)(c + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * width);
                Vector3? v = GetXYZ(dem, mask, testR, testC, parser);
                //if (testR >= 0 && testR < mask.Height && testC > 0 && testC < mask.Width && mask[0, testR, testC] == 1)
                if(v.HasValue)
                {
                    newRowCols.Add(new Vector2(testC, testR));
                    
                    //Test error between mesh and samples
                    if(tested < testNum && testR > r && testR < r + height && testC > c && testC < c + width)
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
                            tested = testNum;
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
            Vector3? tl = Bilinear(c - Math.Floor(c), r - Math.Floor(r),
                GetXYZ(dem, null, (int)Math.Floor(r), (int)Math.Floor(c), parser, false),
                GetXYZ(dem, null, (int)Math.Floor(r), (int)Math.Ceiling(c), parser, false),
                GetXYZ(dem, null, (int)Math.Ceiling(r), (int)Math.Floor(c), parser, false),
                GetXYZ(dem, null, (int)Math.Ceiling(r), (int)Math.Ceiling(c), parser, false));
            double r1 = Math.Min(r + height, dem.Height - 1);
            double c1 = Math.Min(c + width, dem.Width - 1);
            Vector3? br = Bilinear(c1 - Math.Floor(c1), r1 - Math.Floor(r1),
                GetXYZ(dem, null, (int)Math.Floor(r1), (int)Math.Floor(c1), parser, false),
                GetXYZ(dem, null, (int)Math.Floor(r1), (int)Math.Ceiling(c1), parser, false),
                GetXYZ(dem, null, (int)Math.Ceiling(r1), (int)Math.Floor(c1), parser, false),
                GetXYZ(dem, null, (int)Math.Ceiling(r1), (int)Math.Ceiling(c1), parser, false));
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
            List<Vector2> vIdxs1 = FindCorners(dem, parser, (int)r, (int)c, (int)((width - 1)/2), (int)((height - 1) / 2)).ToList();
            List<Vector2> vIdxs2 = FindCorners(dem, parser, (int)(r + height / 2), (int)c, (int)((width - 1) / 2), (int)((height - 1) / 2)).ToList();
            List<Vector2> vIdxs3 = FindCorners(dem, parser, (int)r, (int)(c + width / 2), (int)((width - 1) / 2), (int)((height - 1) / 2)).ToList();
            List<Vector2> vIdxs4 = FindCorners(dem, parser, (int)(r + height / 2), (int)(c + width / 2), (int)((width - 1)/2), (int)((height - 1) / 2)).ToList();
            vIdxs1.AddRange(rowCols.GetRange(0, 4));
            vIdxs2.AddRange(rowCols.GetRange(0, 4));
            vIdxs3.AddRange(rowCols.GetRange(0, 4));
            vIdxs4.AddRange(rowCols.GetRange(0, 4));

            //Partition our current set of vertices + new samples into children
            foreach (Vector2 rc in rowCols.Union(newRowCols))
            {
                Vector3 v = GetXYZ(dem, null, (int)rc.Y, (int)rc.X, parser).Value;
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
            newRowCols.AddRange(Split(vIdxs1, r, c, width/2.0, height/2.0, error, dem, mask, parser, sampleNum, testNum, sampleScale, rand));
            newRowCols.AddRange(Split(vIdxs2, r + height/2.0, c, width / 2.0, height / 2.0, error, dem, mask, parser, sampleNum, testNum, sampleScale, rand));
            newRowCols.AddRange(Split(vIdxs3, r, c + width / 2.0, width / 2.0, height / 2.0, error, dem, mask, parser, sampleNum, testNum, sampleScale, rand));
            newRowCols.AddRange(Split(vIdxs4, r + height / 2.0, c + width / 2.0, width / 2.0, height / 2.0, error, dem, mask, parser, sampleNum, testNum, sampleScale, rand));

            return newRowCols;
        }

        /// <summary>
        /// Unprojects passed in row, col in dem to return an xyz. Will return null if index is out of bounds, or point should be masked out.
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="mask"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <param name="parser"></param>
        /// <param name="filterValues"></param>
        /// <returns></returns>
        Vector3? GetXYZ(Image dem, Mask mask, int row, int col, PDSParser parser, bool filterValues=true)
        {
            if (row < 0 || row >= dem.Height || col < 0 || col >= dem.Width || dem.IsInvalid(row, col)) //respect input image mask if it has one
            {
                return null;
            }
            else if (parser == null || !parser.HasMissingConstant) // should this be ((parser == null || !parser.HasMissingConstant) && !ret.IsInvalid(row, col)
            {
                var value = dem[0, row, col];
                if (!filterValues || value >= options.DEMMinFilter && value <= options.DEMMaxFilter)
                {
                    if (mask != null)
                    {
                        if (!mask.isValid(row, col))
                        {
                            return null;
                        }
                        mask.setInvalid(row, col); //Prevent this point from being sampled again
                    }
                    Vector3 ret = dem.CameraModel.Unproject(new Vector2(row, col), -1 * value);
                    ret.X *= -1;
                    ret.Y *= -1;
                    return ret;
                }
            }
            return null;
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
                dem = new SparseImage(options.InputDem, ImageConverters.PassThrough, useCache: true, chunkSize: 1024, cacheSize: 100, diskBack: true);
                useHashForMask = true;
            } else
            {
                dem = Image.Load(options.InputDem, ImageConverters.PassThrough);
                useHashForMask = false;
            }    
            
            if(dem.CameraModel == null)
            {
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, options.MetersPerPixel);
            }

            Mesh mesh = new Mesh();
            //No decimation:
            //  Convert the entire dem to xyz's with mask
            //  Build the mesh by connecting neighboring points with a regular grid of tris
            if(options.Error == 0 && options.Radius == -1 && useSiteDriveFame)
            {
                //TODO: Refactor building full mesh to make this easy to add (likely avoid ConvertRNG)
                throw new NotImplementedException("Building full mesh in sitedrive frame not yet supported");
            }

            PDSParser parser = null;
            dem.ScaleValues(options.VerticalScale);
            if (dem.Metadata.GetType() == typeof(PDSMetadata))
            {
                parser = new PDSParser((PDSMetadata)dem.Metadata);
                Meshing.CheckType(parser, RoverProductType.Range, "ConvertRange");
                Meshing.CheckCameraCenter(parser, dem, "ConvertRNG");
                Meshing.AddMaskForMissingConstant(dem, dem, parser);
            }

            if (options.Error == 0 && options.Radius == -1 && !useSiteDriveFame)
            {
                //TODO: check on this function
                Image xyz = null;
                Image mask = new Image(1, dem.Width, dem.Height);

                if (dem.Bands == 3)
                {
                    xyz = dem;  // Unusual but handle the case where we are passed a 3 band xyz image instead of a dem
                }
                else
                {
                    xyz = new Image(3, dem.Width, dem.Height);
                    for (int row = 0; row < dem.Height; row++)
                    {
                        for (int col = 0; col < dem.Width; col++)
                        {
                            Vector3? v = GetXYZ(dem, null, row, col, parser);
                            if(v.HasValue)
                            {
                                xyz[0, row, col] = (float)v.Value.X;
                                xyz[1, row, col] = (float)v.Value.Y;
                                xyz[2, row, col] = (float)v.Value.Z;
                                mask[0, row, col] = 1;
                            } else
                            {
                                mask[0, row, col] = 0;
                            }
                        }
                    }
                }
                mesh = Meshing.BuildOrganizedMesh(xyz, mask:mask);
            } 
            //Build decimated mesh by iterative sampling:
            // Start with two tris that connect the dem corners
            // Test error and sample regions that need subdividing (currently quad scheme)
            else
            {
                List<Vector2> rowCols;
                //This mask is only used to avoid resampling the same point. Invalid point data is masked out by the GetXYZ function (computed lazily)
                Mask mask;
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
                        rowCols = FindCorners(dem, parser, baseR, baseC, pixelWidth-1, pixelHeight-1).ToList();
                        rowCols.AddRange(Split(rowCols, baseR, baseC, pixelWidth, pixelHeight, options.Error, dem, mask, parser, options.SampleNum, options.TestNum, options.SampleRegionScale, OPS.Util.NumberHelper.MakeRandomGenerator()));
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
                    rowCols = FindCorners(dem, parser).ToList();
                    rowCols.AddRange(Split(rowCols, 0, 0, dem.Width, dem.Height, options.Error, dem, mask, parser, options.SampleNum, options.TestNum, options.SampleRegionScale, OPS.Util.NumberHelper.MakeRandomGenerator()));
                }
                //Construct vertices
                var verts = rowCols.Select(rc => {
                    var v = new Vertex(GetXYZ(dem, null, (int)rc.Y, (int)rc.X, parser).Value);
                    if(useSiteDriveFame)
                    {
                        //Shift image origin
                        v.Position.X = v.Position.X + colRowOffset.Y - (double)width / 2.0;
                        v.Position.Y = v.Position.Y - colRowOffset.X + (double)height / 2.0;
                        //Apply vertical offset
                        v.Position.Z = zOffset - v.Position.Z;
                    }                
                    v.UV = dem.PixelToUV(rc);
                    return v;
                    }).ToArray();
                mesh = DelaunayTriangulation.Triangulate(verts, vertToDelaunay, invertWinding : useSiteDriveFame);
            }
            string outputImage = null;
            if (options.InputOrthoImage != null)
            {
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
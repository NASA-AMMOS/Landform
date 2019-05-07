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
        public float VerticleScale { get; set; }

        [Option(Required = false, HelpText = "Output path of mesh.  If ortho image is supplied it will be written to the same path but with a different extension.  If ommited output is written to same directory as input but with a '.mesh' appended to the filename")]
        public string OutputPath { get; set; }

        [Option(Required = false, Default = "png", HelpText = "Export format for textures (examples: jpg or png")]
        public string ImageFormat { get; set; }

        [Option(Required = false, Default = "obj", HelpText = "Export format for mesh (examples: obj or ply")]
        public string MeshFormat { get; set; }

        [Option(Required = false, Default = 0, HelpText = "Decimate (roughly) to this error threshold against original points.")]
        public float Error { get; set; }

        [Option(Required = false, Default = 30, HelpText = "Number of points to add with each split")]
        public int SampleNum { get; set; }

        [Option(Required = false, Default = 4, HelpText = "Number of points to test error against at each split")]
        public int TestNum { get; set; }

        [Option(Required = false, Default = 2.0, HelpText = "Set higher to scale the sampling region to smooth transition between high and low density")]
        public double SampleRegionScale { get; set; }

        [Option(Required = false, Default = 0, HelpText = "If specified, decimate the output mesh to the target number of faces")]
        public int TargetFaces { get; set; }

        [Option(Required = false, Default = false, HelpText = "Do not allow decimation to modify the edge")]
        public bool MaintainEdge { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Set higher to reduce the amount of decimation applied to edge vertices")]
        public float EdgeWeight { get; set; }

        [Option(Required = false, Default = -1000000, HelpText = "Dem values less than this will be ignored")]
        public float DEMMinFilter { get; set; }

        [Option(Required = false, Default = 1000000, HelpText = "Dem values larger than this will be ignored")]
        public float DEMMaxFilter { get; set; }

        // TODO: Skirt option?
    }

    public class DEM2Mesh
    {
        DEM2MeshOptions options;

        public DEM2Mesh(DEM2MeshOptions options)
        {
            this.options = options;
        }

        Func<Vertex, DelaunayPoint> vertToDelaunay = v =>
        {
            DelaunayPoint p = new DelaunayPoint();
            p.xy = new Vector2(v.Position.X, v.Position.Y);
            p.height = v.Position.Z;
            return p;
        };

        void TryAdd(List<Vertex> verts, int r, int c, Image xyz, Image mask)
        {
            r = Math.Min(r, xyz.Height - 1);
            r = Math.Max(r, 0);
            c = Math.Min(c, xyz.Width - 1);
            c = Math.Max(c, 0);
            if (mask[0, r, c] == 1)
            {
                verts.Add(new Vertex(xyz[0, r, c], xyz[1, r, c], xyz[2, r, c]));
            }
        }

        /// <summary>
        /// Given Image xyz and Image mask, find corners that are not masked out. Optionally enter top left corner and a size parameter to get corners of a subregion.
        /// May not return a full set of vertices (potentially none) if image heavily masked
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="mask"></param>
        /// <param name="minR"></param>
        /// <param name="minC"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        IEnumerable<Vertex> FindCorners(Image xyz, Image mask, int minR = 0, int minC = 0, int size = -1)
        {
            int d = 1;
            bool foundTopLeft = false;
            bool foundTopRight = false;
            bool foundBotLeft = false;
            bool foundBotRight = false;
            if(size == -1)
            {
                size = Math.Min(xyz.Width - minC, xyz.Height - minR) - 1;
            }

            while (d < size && (!foundTopLeft || !foundTopRight || !foundBotLeft || !foundBotRight)) {
                int c;
                for (int r = 0; r <= d; r++)
                {
                    c = d - r;
                    if(!foundTopLeft)
                    {
                        if(mask[0, minR + r, minC + c] == 1)
                        {
                            foundTopLeft = true;
                            Vertex v = new Vertex(xyz[0, minR + r, minC + c], xyz[1, minR + r, minC + c], xyz[2, minR + r, minC + c]);
                            v.UV = xyz.PixelToUV(new Vector2(minC + c, minR + r));
                            yield return v;
                        }
                    }
                    if(!foundTopRight)
                    {
                        if (mask[0, minR + r, minC + size - c] == 1)
                        {
                            foundTopRight = true;
                            Vertex v = new Vertex(xyz[0, minR + r, minC + size - c], xyz[1, minR + r, minC + size - c], xyz[2, minR + r, minC + size - c]);
                            v.UV = xyz.PixelToUV(new Vector2(minC + size - c, minR + r));
                            yield return v;
                        }
                    }
                    if(!foundBotLeft)
                    {
                        if (mask[0, minR + size - r, minC + c] == 1)
                        {
                            foundBotLeft = true;
                            Vertex v = new Vertex(xyz[0, minR + size - r, minC + c], xyz[1, minR + size - r, minC + c], xyz[2, minR + size - r, minC + c]);
                            v.UV = xyz.PixelToUV(new Vector2(minC + c, minR + size - r));
                            yield return v;
                        }
                    }
                    if(!foundBotRight)
                    {
                        if (mask[0, minR + size - r, minC + size - c] == 1)
                        {
                            foundBotRight = true;
                            Vertex v = new Vertex(xyz[0, minR + size - r, minC + size - c], xyz[1, minR + size - r, minC + size - c], xyz[2, minR + size - r, minC + size - c]);
                            v.UV = xyz.PixelToUV(new Vector2(minC + size - c, minR + size - r));
                            yield return v;
                        }
                    }
                }
                ++d;
            }
        }

        /// <summary>
        /// Recursively subsample regions where geometric error is too large
        /// </summary>
        /// <param name="verts"></param>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="error"></param>
        /// <param name="xyz"></param>
        /// <param name="original_mask"></param>
        /// <param name="mutable_mask"></param>
        /// <param name="size"></param>
        /// <param name="sampleNum"></param>
        /// <param name="testNum"></param>
        /// <param name="sampleScale"></param>
        /// <param name="rand"></param>
        /// <returns></returns>
        List<Vertex> split(List<Vertex> verts, double r, double c, double error, Image xyz, Image original_mask, Image mutable_mask, double size, int sampleNum, int testNum, double sampleScale, Random rand)
        {
            //Mesh the current set of vertices
            Mesh mesh = DelaunayTriangulation.Triangulate(verts, vertToDelaunay);

            //Sample
            List<Vertex> newVerts = new List<Vertex>();
            double tested = 0;
            bool shouldSplit = false;
            for (int i = 0; i < sampleNum; i++)
            {
                int testR = (int)(r + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * size);
                int testC = (int)(c + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * size);
                if (testR >= 0 && testR < mutable_mask.Height && testC > 0 && testC < mutable_mask.Width && mutable_mask[0, testR, testC] == 1)
                {
                    newVerts.Add(new Vertex(xyz[0, testR, testC], xyz[1, testR, testC], xyz[2, testR, testC]));
                    newVerts[newVerts.Count - 1].UV = xyz.PixelToUV(new Vector2(testC, testR));
                    mutable_mask[0, testR, testC] = 0;
                    //Test error between mesh and samples
                    if(tested < testNum && testR > r && testR < r + size && testC > c && testC < c + size)
                    {
                        double dist = double.MaxValue;
                        List<Triangle> tris = mesh.Triangles();
                        foreach (Triangle t in tris)
                        {
                            double tmp = t.SquaredDistance(newVerts[newVerts.Count - 1].Position);
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
                return newVerts;
            }

            //Compute new child tile bounds
            double minX = xyz.BilinearSample(0, (float)r, (float)c);
            double maxY = xyz.BilinearSample(1, (float)r, (float)c);
            double maxX = xyz.BilinearSample(0, (float)Math.Min(r + size, xyz.Height - 1), (float)Math.Min(c + size, xyz.Width - 1));
            double minY = xyz.BilinearSample(1, (float)Math.Min(r + size, xyz.Height - 1), (float)Math.Min(c + size, xyz.Width - 1));

            double midX = (minX + maxX) / 2.0;
            double midY = (minY + maxY) / 2.0;
            double umidX = (minX + maxX + (maxX - minX) * 0.1) / 2.0;
            double lmidX = (minX + maxX - (maxX - minX) * 0.1) / 2.0;
            double umidY = (minY + maxY + (maxY - minY) * 0.1) / 2.0;
            double lmidY = (minY + maxY - (maxY - minY) * 0.1) / 2.0;

            //Add boundary conditions to each tile child (try to find approximate tile corners, and include full dem corners in case of failure)
            List<Vertex> verts1 = FindCorners(xyz, original_mask, (int)r, (int)c, (int)((size - 1)/2)).ToList();
            List<Vertex> verts2 = FindCorners(xyz, original_mask, (int)(r + size / 2), (int)c, (int)((size - 1)/2)).ToList();
            List<Vertex> verts3 = FindCorners(xyz, original_mask, (int)r, (int)(c + size / 2), (int)((size - 1)/2)).ToList();
            List<Vertex> verts4 = FindCorners(xyz, original_mask, (int)(r + size / 2), (int)(c + size / 2), (int)((size - 1)/2)).ToList();
            verts1.AddRange(verts.GetRange(0, 4));
            verts2.AddRange(verts.GetRange(0, 4));
            verts3.AddRange(verts.GetRange(0, 4));
            verts4.AddRange(verts.GetRange(0, 4));

            //Partition our current set of vertices + new samples into children
            foreach (Vertex v in verts.Union(newVerts))
            {
                if(v.Position.X < umidX)
                {
                    if(v.Position.Y > lmidY)
                    {
                        verts1.Add(v);
                    }
                    if(v.Position.Y < umidY)
                    {
                        verts2.Add(v);
                    }
                }
                if(v.Position.X > lmidX)
                {
                    if(v.Position.Y > lmidY)
                    {
                        verts3.Add(v);
                    }
                    if(v.Position.Y < umidY)
                    {
                        verts4.Add(v);
                    }
                }
            }

            //Recurse on children
            newVerts.AddRange(split(verts1, r, c, error, xyz, original_mask, mutable_mask, size / 2.0, sampleNum, testNum, sampleScale, rand));
            newVerts.AddRange(split(verts2, r + size / 2.0, c, error, xyz, original_mask, mutable_mask, size / 2.0, sampleNum, testNum, sampleScale, rand));
            newVerts.AddRange(split(verts3, r, c + size / 2.0, error, xyz, original_mask, mutable_mask, size / 2.0, sampleNum, testNum, sampleScale, rand));
            newVerts.AddRange(split(verts4, r + size / 2.0, c + size / 2.0, error, xyz, original_mask, mutable_mask, size / 2.0, sampleNum, testNum, sampleScale, rand));

            return newVerts;
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
            
            Image dem = Image.Load(options.InputDem, ImageConverters.PassThrough);
            
            if(dem.CameraModel == null)
            {
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, options.MetersPerPixel);
            }
           
            Image xyz = null;
            if(dem.Bands == 3)
            {
                xyz = dem;  // Unusual but handle the case where we are passed a 3 band xyz image instead of a dem
            }
            else
            {
                dem.ScaleValues(options.VerticleScale);
                xyz = Meshing.ConvertRNG(dem, null);
            }
            Image mask = new Image(1, dem.Width, dem.Height);
            foreach (var coord in dem.Coordinates(true))
            {
                var value = dem[coord.Band, coord.Row, coord.Col];
                mask[0, coord.Row, coord.Col] = value >= options.DEMMinFilter && value <= options.DEMMaxFilter ? 1 : 0;
            }

            Mesh mesh = new Mesh();
            if (options.Error == 0)
            {
                mesh = Meshing.BuildOrganizedMesh(xyz, mask:mask);
            }
            else
            {
                List<Vertex> verts;
                verts = FindCorners(xyz, mask).ToList();
                verts.AddRange(split(verts, 0, 0, options.Error, xyz, mask, new Image(mask), Math.Min(xyz.Width, xyz.Height), options.SampleNum, options.TestNum, options.SampleRegionScale, new Random()));
                mesh = DelaunayTriangulation.Triangulate(verts, vertToDelaunay);
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
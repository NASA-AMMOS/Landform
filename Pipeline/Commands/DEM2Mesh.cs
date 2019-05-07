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

        [Option(Required = false, Default = 10, HelpText = "Decimate (roughly) to this error threshold against original points.")]
        public float Error { get; set; }

        [Option(Required = false, Default = 20, HelpText = "Number of points to add with each split")]
        public int SampleNum { get; set; }

        [Option(Required = false, Default = 3, HelpText = "Number of points to test error against at each split")]
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

        IEnumerable<Vertex> FindCorners(Image xyz, Image mask)
        {
            int d = 1;
            bool foundTopLeft = false;
            bool foundTopRight = false;
            bool foundBotLeft = false;
            bool foundBotRight = false;
            while (d < Math.Min(xyz.Width, xyz.Height) - 1 && (!foundTopLeft || !foundTopRight || !foundBotLeft || !foundBotRight)) {
                int c;
                for (int r = 0; r <= d; r++)
                {
                    c = d - r;
                    if(!foundTopLeft)
                    {
                        if(mask[0,r,c] == 1)
                        {
                            foundTopLeft = true;
                            Vertex v = new Vertex(xyz[0, r, c], xyz[1, r, c], xyz[2, r, c]);
                            v.UV = xyz.PixelToUV(new Vector2(c, r));
                            yield return new Vertex(xyz[0, r, c], xyz[1, r, c], xyz[2, r, c]);
                        }
                    }
                    if(!foundTopRight)
                    {
                        if (mask[0, r, xyz.Width - 1 - c] == 1)
                        {
                            foundTopRight = true;
                            Vertex v = new Vertex(xyz[0, r, xyz.Width - 1 - c], xyz[1, r, xyz.Width - 1 - c], xyz[2, r, xyz.Width - 1 - c]);
                            v.UV = xyz.PixelToUV(new Vector2(xyz.Width - 1 - c, r));
                            yield return v;
                        }
                    }
                    if(!foundBotLeft)
                    {
                        if (mask[0, xyz.Height - 1 - r, c] == 1)
                        {
                            foundBotLeft = true;
                            Vertex v = new Vertex(xyz[0, xyz.Height - 1 - r, c], xyz[1, xyz.Height - 1 - r, c], xyz[2, xyz.Height - 1 - r, c]);
                            v.UV = xyz.PixelToUV(new Vector2(c, xyz.Height - 1 - r));
                            yield return v;
                        }
                    }
                    if(!foundBotRight)
                    {
                        if (mask[0, xyz.Height - 1 - r, xyz.Width - 1 - c] == 1)
                        {
                            foundBotRight = true;
                            Vertex v = new Vertex(xyz[0, xyz.Height - 1 - r, xyz.Width - 1 - c], xyz[1, xyz.Height - 1 - r, xyz.Width - 1 - c], xyz[2, xyz.Height - 1 - r, xyz.Width - 1 - c]);
                            v.UV = xyz.PixelToUV(new Vector2(xyz.Width-1-c, xyz.Height-1-r));
                            yield return v;
                        }
                    }
                }
                ++d;
            }
        }

        List<Vertex> split(List<Vertex> verts, double r, double c, double error, Image xyz, Image mask, double size, int sampleNum, int testNum, double sampleScale, Random rand)
        {
            //BoundingBox box = new BoundingBox(new Vector3(xyz.BilinearSample(0,(float)r,(float)c), xyz.BilinearSample(1, (float)r, (float)c), xyz.BilinearSample(2, (float)r, (float)c)),
            //    new Vector3(xyz.BilinearSample(0, (float)(r+size), (float)(c + size)), xyz.BilinearSample(0, (float)(r + size), (float)(c + size)), xyz.BilinearSample(0, (float)(r + size), (float)(c + size))));
            //Mesh mesh = DelaunayTriangulation.Triangulate(verts.GetRange(0,4).Union(verts.Where(v => box.Contains(v.Position) == ContainmentType.Contains)), vertToDelaunay);
            Mesh mesh = DelaunayTriangulation.Triangulate(verts, vertToDelaunay);
            List<Vertex> newVerts = new List<Vertex>();

            double tested = 0;
            bool shouldSplit = false;
            for (int i = 0; i < sampleNum; i++)
            {
                int testR = (int)(r + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * size);
                int testC = (int)(c + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * size);
                if (testR >= 0 && testR < mask.Height && testC > 0 && testC < mask.Width && mask[0, testR, testC] == 1)
                {
                    newVerts.Add(new Vertex(xyz[0, testR, testC], xyz[1, testR, testC], xyz[2, testR, testC]));
                    newVerts[newVerts.Count - 1].UV = xyz.PixelToUV(new Vector2(testC, testR));
                    //verts.Add(newVerts[newVerts.Count - 1]); ///////////////////
                    mask[0, testR, testC] = 0;
                    if(tested < testNum)
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

            Triangle testTri;
            Face testFace;
            BarycentricPoint testPoint;
            Vector2 rc;
            double h1;
            double h2;
            
            /*for (int i = 0; i < testNum; i++)
            {
                testFace = mesh.Faces[(int)(rand.NextDouble() * mesh.Faces.Count)];
                testTri = new Triangle(mesh.Vertices[testFace.P0], mesh.Vertices[testFace.P1], mesh.Vertices[testFace.P2]);
                testPoint = testTri.Sample(rand);
                //TODO: bounds check?
                rc = xyz.UVToPixel(testPoint.UV);
                h1 = xyz.BilinearSample(2, (float)rc.Y, (float)rc.X);
                h2 = testPoint.Position.Z;
                if(Math.Abs(h2 - h1) > error)
                {
                    shouldSplit = true;
                    break;
                }
            }*/

            if(!shouldSplit)
            {
                return newVerts;
            }

            double minX = xyz.BilinearSample(0, (float)r, (float)c);
            double minY = xyz.BilinearSample(1, (float)r, (float)c);
            double maxX = xyz.BilinearSample(0, (float)(r + size), (float)(c + size));
            double maxY = xyz.BilinearSample(1, (float)(r + size), (float)(c + size));

            double umidX = (minX + maxX + (maxX - minX) * 0.1) / 2.0;
            double lmidX = (minX + maxX - (maxX - minX) * 0.1) / 2.0;
            double umidY = (minY + maxY + (maxY - minY) * 0.1) / 2.0;
            double lmidY = (minY + maxY + (maxY - minY) * 0.1) / 2.0;

            List<Vertex> verts1 = verts.GetRange(0, 4);
            List<Vertex> verts2 = verts.GetRange(0, 4);
            List<Vertex> verts3 = verts.GetRange(0, 4);
            List<Vertex> verts4 = verts.GetRange(0, 4);

            foreach(Vertex v in verts.Union(newVerts))
            {
                if(v.Position.X < umidX)
                {
                    if(v.Position.Y < umidY)
                    {
                        verts1.Add(v);
                    }
                    if(v.Position.Y > lmidY)
                    {
                        verts2.Add(v);
                    }
                }
                if(v.Position.X > lmidX)
                {
                    if(v.Position.Y < umidY)
                    {
                        verts3.Add(v);
                    }
                    if(v.Position.Y > lmidY)
                    {
                        verts4.Add(v);
                    }
                }
            }

            newVerts.AddRange(split(verts1, r + size / 2.0, c, error, xyz, mask, size / 2.0, sampleNum, testNum, sampleScale, rand));
            newVerts.AddRange(split(verts2, r, c, error, xyz, mask, size / 2.0, sampleNum, testNum, sampleScale, rand));
            newVerts.AddRange(split(verts3, r + size / 2.0, c + size / 2.0, error, xyz, mask, size / 2.0, sampleNum, testNum, sampleScale, rand));
            newVerts.AddRange(split(verts4, r, c + size / 2.0, error, xyz, mask, size / 2.0, sampleNum, testNum, sampleScale, rand));

            return newVerts;
        }

        public int Run()
        {
            if(!string.IsNullOrEmpty(this.options.OutputPath))
            {
                PathHelper.EnsureExists(this.options.OutputPath);
            }
            else 
            {
                this.options.OutputPath = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh.e" + options.Error + ".smooth." + options.MeshFormat);
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
            //Old version

            //var mesh = Meshing.BuildOrganizedMesh(xyz, mask: mask);
            //mesh.GenerateVertexNormals();
            //mesh.Save("C:\\Users\\conductor\\Desktop\\dems\\mesh.obj");

            /*if(targetFacesDefined || decimationRatioDefined)
            {
                var faceTarget = targetFacesDefined ? options.TargetFaces : mesh.Faces.Count;
                if(decimationRatioDefined)
                {
                    faceTarget = (int)Math.Min(MathHelper.Clamp(options.DecimationRatio, 0, 1) * mesh.Faces.Count, faceTarget);
                }
                var notToched = mesh.Corners(new Vector3(0,1,0));
                if(options.MaintainEdge)
                {
                    notToched = mesh.EdgeVertices();
                }
                //mesh = MeshLab.Decimate(mesh, faceTarget);
                //mesh.ClearUVs();
                //mesh.ResampleDecimation(MeshReconMethod.Poisson, faceTarget, mesh.Bounds(), new Vector3(0, 1, 0));
                mesh = EdgeCollapse.QuadricEdgeCollapse(mesh, faceTarget, perimeterPenaltyFactor: options.EdgeWeight, notTouched: notToched);
                // TODO: re-atlas
            }*/

            //initialization
            //int iter = 1;
            //int maxIters = 10;
            //bool converged = false;

            List<Vertex> verts;
            verts = FindCorners(xyz, mask).ToList();

            verts.AddRange(split(verts, 0, 0, options.Error, xyz, mask, Math.Min(xyz.Width, xyz.Height), options.SampleNum, options.TestNum, options.SampleRegionScale, new Random()));

            Mesh mesh = DelaunayTriangulation.Triangulate(verts, vertToDelaunay);
            //int testR;
            //int testC;
            //int tested;
            //double l;
            //double t;
            //double newBoxSize;
            //Vector3 testPoint;

            //while (iter <= maxIters && !converged)
            //{
            //    converged = true;
            //    newBoxes = new List<Box>();

            //    //TODO: Will change to avoid creating octree; sample points can be directly mapped into the tif and interpolated to check error
            //    Octree octree = new Octree(mesh.Bounds());
            //    octree.InsertList(mesh.Triangles().Select(tri => new OctreeTriangle(tri)));

            //    foreach (Box box in boxes) {
                   
            //        l = box.size * box.c;
            //        t = box.size * box.r;
            //        newBoxSize = box.size / 2.0;

            //        //Check if we need to split this box
            //        bool shouldContinue = true;
            //        tested = 0;

            //        for (int i = 0; i < this.options.SampleNum; i++)
            //        {
            //            testR = (int)(t + (options.SampleRegionScale * rand.NextDouble() - 0.5 * (options.SampleRegionScale - 1)) * box.size);
            //            testC = (int)(l + (options.SampleRegionScale * rand.NextDouble() - 0.5 * (options.SampleRegionScale - 1)) * box.size);
            //            if (testR >= 0 && testR < mask.Height && testC > 0 && testC < mask.Width && mask[0, testR, testC] == 1)
            //            {
            //                testPoint = new Vector3(xyz[0, testR, testC], xyz[1, testR, testC], xyz[2, testR, testC]);
            //                if (tested < this.options.TestNum)
            //                {
            //                    closest = (OctreeTriangle)octree.Closest(testPoint);
            //                    if (closest.SquaredDistance(testPoint) > options.Error * options.Error)
            //                    {
            //                        shouldContinue = false;
            //                    }
            //                    tested++;
            //                }
            //                TryAdd(verts, testR, testC, xyz, mask);
            //                mask[0, testR, testC] = 0;
            //            }
            //        }


            //        if(shouldContinue)
            //        {
            //            continue;
            //        }

            //        //Recurse on subregions
            //        converged = false;

            //        newBoxes.Add(new Box(newBoxSize, 2 * box.r, 2 * box.c));
            //        newBoxes.Add(new Box(newBoxSize, 2 * box.r + 1, 2 * box.c));
            //        newBoxes.Add(new Box(newBoxSize, 2 * box.r, 2 * box.c + 1));
            //        newBoxes.Add(new Box(newBoxSize, 2 * box.r + 1, 2 * box.c + 1));
            //    }
            //    iter++;
            //    boxes = newBoxes;
            //}

            string outputImage = null;
            if (options.InputOrthoImage != null)
            {
                Image ortho = Image.Load(options.InputOrthoImage);
                outputImage = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.ImageFormat);
                ortho.Save<byte>(outputImage); // TODO, add support for matching input type
            }
            mesh.Save(this.options.OutputPath, outputImage);
            return 0;
        }

    }
}
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

        [Option(Required = false, Default = 10, HelpText = "Decimate (roughly) to this error threshold against original points")]
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

    struct Box {
        public double size;
        public int r;
        public int c;

        public Box(double size, int r, int c)
        {
            this.size = size;
            this.r = r;
            this.c = c;
        }       
    }

    public class OctreeTriangle : OctreeNodeContents
    {
        Triangle tri;

        public OctreeTriangle(Triangle tri)
        {
            this.tri = tri;
        }
        public BoundingBox Bounds()
        {
            return tri.Bounds();
        }
        public bool Intersects(BoundingBox other)
        {
            return tri.Intersects(other);
        }
        public double SquaredDistance(Vector3 xyz)
        {
            return tri.SquaredDistance(xyz);
        }
    }

    public class DEM2Mesh
    {
        DEM2MeshOptions options;

        public DEM2Mesh(DEM2MeshOptions options)
        {
            this.options = options;
        }

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
                            yield return new Vertex(xyz[0, r, c], xyz[1, r, c], xyz[2, r, c]);
                        }
                    }
                    if(!foundTopRight)
                    {
                        if (mask[0, r, xyz.Width - 1 - c] == 1)
                        {
                            foundTopRight = true;
                            yield return new Vertex(xyz[0, r, xyz.Width - 1 - c], xyz[1, r, xyz.Width - 1 - c], xyz[2, r, xyz.Width - 1 - c]);
                        }
                    }
                    if(!foundBotLeft)
                    {
                        if (mask[0, xyz.Height - 1 - r, c] == 1)
                        {
                            foundBotLeft = true;
                            yield return new Vertex(xyz[0, xyz.Height - 1 - r, c], xyz[1, xyz.Height - 1 - r, c], xyz[2, xyz.Height - 1 - r, c]);
                        }
                    }
                    if(!foundBotRight)
                    {
                        if (mask[0, xyz.Height - 1 - r, xyz.Width - 1 - c] == 1)
                        {
                            foundBotRight = true;
                            yield return new Vertex(xyz[0, xyz.Height - 1 - r, xyz.Width - 1 - c], xyz[1, xyz.Height - 1 - r, xyz.Width - 1 - c], xyz[2, xyz.Height - 1 - r, xyz.Width - 1 - c]);
                        }
                    }
                }
                ++d;
            }
        }

        public int Run()
        {
            Random rand = new Random();

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
            int iter = 1;
            int maxIters = 10;
            bool converged = false;

            List<Box> boxes = new List<Box>();
            boxes.Add(new Box(Math.Min(xyz.Width, xyz.Height), 0, 0));
            List<Box> newBoxes;

            List<Vertex> verts;
            verts = FindCorners(xyz, mask).ToList();
            
            Func<Vertex, DelaunayPoint> vertToDelaunay = v =>
            {
                DelaunayPoint p = new DelaunayPoint();
                p.xy = new Vector2(v.Position.X, v.Position.Y);
                p.height = v.Position.Z;
                return p;
            };

            Mesh mesh = new Mesh();
            int testR;
            int testC;
            int tested;
            double l;
            double t;
            double newBoxSize;
            Vector3 testPoint;
            OctreeTriangle closest;

            while (iter <= maxIters && !converged)
            {
                converged = true;
                newBoxes = new List<Box>();

                //TODO: Will change to avoid creating octree; sample points can be directly mapped into the tif and interpolated to check error
                mesh = DelaunayTriangulation.Triangulate(verts, vertToDelaunay);
                Octree octree = new Octree(mesh.Bounds());
                octree.InsertList(mesh.Triangles().Select(tri => new OctreeTriangle(tri)));

                foreach (Box box in boxes) {
                   
                    l = box.size * box.c;
                    t = box.size * box.r;
                    newBoxSize = box.size / 2.0;

                    //Check if we need to split this box
                    bool shouldContinue = true;
                    tested = 0;

                    for (int i = 0; i < this.options.SampleNum; i++)
                    {
                        testR = (int)(t + (options.SampleRegionScale * rand.NextDouble() - 0.5 * (options.SampleRegionScale - 1)) * box.size);
                        testC = (int)(l + (options.SampleRegionScale * rand.NextDouble() - 0.5 * (options.SampleRegionScale - 1)) * box.size);
                        if (testR >= 0 && testR < mask.Height && testC > 0 && testC < mask.Width && mask[0, testR, testC] == 1)
                        {
                            testPoint = new Vector3(xyz[0, testR, testC], xyz[1, testR, testC], xyz[2, testR, testC]);
                            if (tested < this.options.TestNum)
                            {
                                closest = (OctreeTriangle)octree.Closest(testPoint);
                                if (closest.SquaredDistance(testPoint) > options.Error * options.Error)
                                {
                                    shouldContinue = false;
                                }
                                tested++;
                            }
                            TryAdd(verts, testR, testC, xyz, mask);
                            mask[0, testR, testC] = 0;
                        }
                    }


                    if(shouldContinue)
                    {
                        continue;
                    }

                    //Recurse on subregions
                    converged = false;

                    newBoxes.Add(new Box(newBoxSize, 2 * box.r, 2 * box.c));
                    newBoxes.Add(new Box(newBoxSize, 2 * box.r + 1, 2 * box.c));
                    newBoxes.Add(new Box(newBoxSize, 2 * box.r, 2 * box.c + 1));
                    newBoxes.Add(new Box(newBoxSize, 2 * box.r + 1, 2 * box.c + 1));
                }
                iter++;
                boxes = newBoxes;
            }

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
using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.MathExtensions;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{

    public class VertexWithRoughness : Vertex
    {
        public double RMS;
        public double AverageDistance;
        public double Variance;
        public double Range;
        public double DistanceFromCenter;


        public VertexWithRoughness()
        {

        }



        /// <summary>
        /// Copy constructor.  Note that you should almost always use Vertex.Clone
        /// instead so that methods work with types that extend Vertex with additional properties
        /// </summary>
        /// <param name="other"></param>
        public VertexWithRoughness(VertexWithRoughness other)
        {
            this.Position = other.Position;
            this.Normal = other.Normal;
            this.Color = other.Color;
            this.UV = other.UV;
            this.RMS = other.RMS;
            this.AverageDistance = other.AverageDistance;
            this.Variance = other.Variance;
            this.Range = other.Range;
            this.DistanceFromCenter = other.DistanceFromCenter;
        }

        public override object Clone()
        {
            return new VertexWithRoughness(this);
        }

    }


    class PlaneFit
    {
        public Vector3 Centroid;
        public Vector3 Normal;

        public PlaneFit(Vector3 c, Vector3 n)
        {
            Centroid = c;
            Normal = n;
        }

        public PlaneFit(IEnumerable<Vector3> points)
        {
            var r = FitPlane(points);
            if (r == null)
            {
                return;
            }
            this.Centroid = r.Centroid;
            this.Normal = r.Normal;
        }

        static PlaneFit FitPlane(IEnumerable<Vector3> points)
        {

            if (points.Count() < 3)
            {
                return null; // At least three points required
            }

            var sum = new Vector3();
            foreach (var p in points)
            {
                sum += p;
            }
            var centroid = sum * (1.0 / points.Count());

            // Calc full 3x3 covariance matrix, excluding symmetries:
            var xx = 0.0; var xy = 0.0; var xz = 0.0;
            var yy = 0.0; var yz = 0.0; var zz = 0.0;

            foreach (var p in points)
            {
                var r = p - centroid;
                xx += r.X * r.X;
                xy += r.X * r.Y;
                xz += r.X * r.Z;
                yy += r.Y * r.Y;
                yz += r.Y * r.Z;
                zz += r.Z * r.Z;
            }

            var det_x = yy * zz - yz * yz;
            var det_y = xx * zz - xz * xz;
            var det_z = xx * yy - xy * xy;

            var det_max = Math.Max(det_x, Math.Max(det_y, det_z));
            if (det_max <= 0.0)
            {
                return null; // The points don't span a plane
            }

            // Pick path with best conditioning:
            var dir = Vector3.Zero;
            if (det_max == det_x)
            {
                dir = new Vector3(det_x, xz * yz - xy * zz, xy * yz - xz * yy);

            }
            else if (det_max == det_y)
            {
                dir = new Vector3(xz * yz - xy * zz, det_y, xy * xz - yz * xx);
            }
            else
            {
                dir = new Vector3(xy * yz - xz * yy, xy * xz - yz * xx, det_z);
            }

            return new PlaneFit(centroid, Vector3.Normalize(dir));

        }
    }

    class PatchPoint
    {
        public Vector3 Position;
        public Vector3 NormalProjectedPosition;
        public Vector3 PlaneProjectedPoint;

        public double DistanceFromCenter;

        public PatchPoint(Vector3 position, Patch patch)
        {
            this.Position = position;
            this.DistanceFromCenter = patch.DistanceFromCenter(position); 
            this.NormalProjectedPosition = patch.Center + patch.Normal * DistanceFromCenter;
            this.PlaneProjectedPoint = Position - patch.Normal * DistanceFromCenter;
        }
    }

    class Patch
    {
        public Vertex SampleVertex;
        public Vector3 Center;
        public Vector3 Normal;

        List<PatchPoint> points = new List<PatchPoint>();

        public Patch(Vertex sampleVertex, IEnumerable<Vertex> verts, bool useSampleNormal = false)
        {
            this.SampleVertex = sampleVertex;
            if (!useSampleNormal)
            {
                var plane = new PlaneFit(verts.Select(v => v.Position));
                this.Center = plane.Centroid;
                this.Normal = plane.Normal;
            }
            else
            {
                Center = Vector3.Zero;
                foreach (var v in verts)
                {
                    Center += v.Position;
                }
                Center /= verts.Count();
                Normal = sampleVertex.Normal;
            }
            foreach (var v in verts)
            {
                AddPoint(v.Position);
            }
        }

        public double DistanceFromCenter(Vector3 p)
        {
            var planeToPoint = p - this.Center;
            // See projections: https://math.oregonstate.edu/home/programs/undergrad/CalculusQuestStudyGuides/vcalc/dotprod/dotprod.html
            return Vector3.Dot(planeToPoint, Normal) / Normal.Length();
        }

        public void AddPoint(Vector3 point)
        {
            points.Add(new PatchPoint(point, this));
        }

        public VertexWithRoughness Roughness()
        {
            if (points.Count == 0)
            {
                return new VertexWithRoughness();
            }
            var result = new VertexWithRoughness();
            result.Position = SampleVertex.Position;
            result.Normal = SampleVertex.Normal;
            result.UV = SampleVertex.UV;
            result.Color = SampleVertex.Color;
            var distancesFromCenter = points.Select(p => p.DistanceFromCenter).ToArray();
            if (distancesFromCenter.Length != 0)
            {
                result.Range = distancesFromCenter.Max() - distancesFromCenter.Min();
                var absDifferencesFromAverage = distancesFromCenter.Select(x => Math.Abs(x)).ToArray();
                result.RMS = MathE.RMS(absDifferencesFromAverage);
                result.AverageDistance = MathE.Average(absDifferencesFromAverage);
                result.Variance = MathE.Variance(distancesFromCenter);
                result.DistanceFromCenter = Math.Abs(DistanceFromCenter(SampleVertex.Position));
            }
            return result;
        }

        public Mesh DebugMesh()
        {
            Mesh patch = new Mesh(hasNormals: true, hasColors: true);
            foreach (var p in this.points)
            {
                patch.Vertices.Add(new Vertex(p.Position, this.Normal, new Vector4(1, 0, 1, 1)));
                patch.Vertices.Add(new Vertex(p.NormalProjectedPosition, this.Normal, new Vector4(1, 0, 0, 1)));
                patch.Vertices.Add(new Vertex(p.PlaneProjectedPoint, this.Normal, new Vector4(0, 1, 0, 1)));
            }
            return patch;
        }
    }

    public class PointCloudRoughness
    {

        MeshOperator meshOperator;
        Mesh sampleCloud;

        public PointCloudRoughness(Mesh pointCloud)
        {
            Init(pointCloud, pointCloud);
        }

        /// <summary>
        /// The data cloud contains points from which roughness will be calculated
        /// The sample cloud contains points at which positions roughness will be calculated
        /// These can be the same or different
        /// </summary>
        /// <param name="sampleCloud"></param>
        /// <param name="dataCloud"></param>
        public PointCloudRoughness(Mesh sampleCloud, Mesh dataCloud)
        {
            Init(sampleCloud, dataCloud);
        }

        private void Init(Mesh sampleCloud, Mesh dataCloud)
        {
            if (!dataCloud.HasNormals)
            {
                throw new Exception("Normals are required to calculate roughness");
            }
            meshOperator = new MeshOperator(dataCloud, buildFaceTree: false, buildVertexTree: true, buildUVFaceTree: false);
            this.sampleCloud = sampleCloud;
        }

        public Mesh CalculateRoughness(double distance, ProgressReporter<int> pr = null)
        {

            Mesh result = new Mesh(sampleCloud);
            const int block = 5000;
            int numBlocks = (result.Vertices.Count / block) + 1;
            int completedBlocks = 0;
            CoreLimitedParallel.For(0, numBlocks, k =>
            {
                int start = k * block;
                int end = Math.Min(result.Vertices.Count - 1, start + block);
                for (int i = start; i <= end; i++)
                {
                    result.Vertices[i] = CalculateRoughness(sampleCloud.Vertices[i], distance);
                }
                if(pr != null)
                {
                    lock (pr)
                    {
                        completedBlocks++;
                        pr.Update(completedBlocks * 100 / numBlocks);
                    }
                }
            });
            return result;
        }

        public VertexWithRoughness CalculateRoughness(Vertex v, double distance, string debugPatchPath = null)
        {
            var nn = meshOperator.NearestVerticesStrict(v.Position, distance);
            var p = new Patch(v, nn);
            if (debugPatchPath != null)
            {
                p.DebugMesh().Save(debugPatchPath);
            }           
            return p.Roughness();
        }

        public RunningAverage EstimatedPointsPerPatch(double distance, int samples = 1000)
        {
            var avg = new RunningAverage();
            Random r = new Random(17);
            for(int i = 0; i < samples; i++)
            {
                var index =  r.Next(0, sampleCloud.Vertices.Count - 1);
                var nn = meshOperator.NearestVerticesStrict(sampleCloud.Vertices[index].Position, distance);
                avg.Push(nn.Count());
            }
            return avg;
        }

        /// <summary>
        //  public double RMS;
        //  public double AverageDistance;
        //  public double Variance;
        //  public double Range;
        /// </summary>
        public class RoughnessPlyWriter : PLYMaximumCompatibilityWriter
        {

            /// <summary>
            /// Scale value to use for all points in the mesh
            /// </summary>
            public RoughnessPlyWriter(bool writeXYZValuesAsFloat) : base(writeXYZValuesAsFloat) { }

            protected override void WriteVertexStructureHeader(Mesh m, StreamWriter sw)
            {

                base.WriteVertexStructureHeader(m, sw);
                sw.WriteLine("property " + NumberFormat + " roughness_rms");
                sw.WriteLine("property " + NumberFormat + " average_distance");
                sw.WriteLine("property " + NumberFormat + " variance");
                sw.WriteLine("property " + NumberFormat + " range");
                sw.WriteLine("property " + NumberFormat + " distance_from_center");
            }

            public override void WriteVertex(Mesh m, Vertex v, Stream s)
            {
                base.WriteVertex(m, v, s);
                VertexWithRoughness rv = (VertexWithRoughness)v;
                if (writeXYZValuesAsFloat)
                {
                    WriteFloatValue((float)rv.RMS, s);
                    WriteFloatValue((float)rv.AverageDistance, s);
                    WriteFloatValue((float)rv.Variance, s);
                    WriteFloatValue((float)rv.Range, s);
                    WriteFloatValue((float)rv.DistanceFromCenter, s);
                }
                else
                {
                    WriteDoubleValue(rv.RMS, s);
                    WriteDoubleValue(rv.AverageDistance, s);
                    WriteDoubleValue(rv.Variance, s);
                    WriteDoubleValue(rv.Range, s);
                    WriteDoubleValue(rv.DistanceFromCenter, s);
                }
            }           
        }
    }
}

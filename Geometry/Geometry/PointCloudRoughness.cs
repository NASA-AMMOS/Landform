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


        public VertexWithRoughness()
        {

        }

        public VertexWithRoughness(List<double> lengthsAlongNormal)
        { 
            // compute the average
            var sum = lengthsAlongNormal.Sum();
            var n = lengthsAlongNormal.Count;
            if(n == 0)
            {
                return;
            }
            var avg = sum / n;
            var differencesFromAverage = lengthsAlongNormal.Select(x => x - avg).ToArray();
            this.RMS = MathE.RMS(differencesFromAverage);
            this.AverageDistance = MathE.Average(differencesFromAverage);
            this.Variance = MathE.Variance(differencesFromAverage);
            this.Range = differencesFromAverage.Max() - differencesFromAverage.Min();
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
        }

        public override object Clone()
        {
            return new VertexWithRoughness(this);
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
            VertexWithRoughness[] roughness = new VertexWithRoughness[sampleCloud.Vertices.Count];
            
            const int block = 5000;
            int numBlocks = (result.Vertices.Count / block) + 1;
            int completedBlocks = 0;
            Parallel.For(0, numBlocks, k =>
            {
                int start = k * block;
                int end = Math.Min(result.Vertices.Count - 1, start + block);
                for (int i = start; i <= end; i++)
                {
                    result.Vertices[i] = CalculateRoughness(sampleCloud.Vertices[i].Position, distance);
                    result.Vertices[i].Normal = sampleCloud.Vertices[i].Normal;
                    result.Vertices[i].UV = sampleCloud.Vertices[i].UV;
                    result.Vertices[i].Color = sampleCloud.Vertices[i].Color;
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
                    
        class PatchPoint
        {
            public Vector3 Position;
            //public Vector3 PlaneProjectedPosition;
            public Vector3 NormalProjectedPosition;
            public double LengthAlongNormal;

            public PatchPoint(Vector3 position, Patch patch)
            {
                this.Position = position;
                this.LengthAlongNormal = patch.LengthAlongNormal(this.Position);
                this.NormalProjectedPosition = patch.Center + patch.Normal * (this.LengthAlongNormal - patch.LengthAlongNormal(patch.Center));
            }
        }

        class Patch
        {
            public Vector3 Center;
            public Vector3 Normal;

            List<PatchPoint> points = new List<PatchPoint>();

            public Patch(Vector3 patchCenter, Vector3 patchNormal)
            {
                Center = patchCenter;
                Normal = patchNormal;
            }

            public Patch(Vector3 patchCenter, IEnumerable<Vertex> verts)
            {
                Center = patchCenter;
                Normal = Vector3.Zero;
                foreach (var v in verts)
                {
                    Normal += v.Normal;
                }
                Normal /= verts.Count();
                Normal.Normalize();
                foreach(var v in verts)
                {
                    AddPoint(v.Position);
                }
            }
            
            public double LengthAlongNormal(Vector3 p)
            {
                // See projections: https://math.oregonstate.edu/home/programs/undergrad/CalculusQuestStudyGuides/vcalc/dotprod/dotprod.html
                return Vector3.Dot(p, Normal) / Normal.Length();
            }

            public void AddPoint(Vector3 point)
            {
                points.Add(new PatchPoint(point, this));
            }

            public VertexWithRoughness Roughness()
            {
                if(points.Count == 0)
                {
                    return new VertexWithRoughness();
                }
                var result = new VertexWithRoughness(points.Select(p => p.LengthAlongNormal).ToList());
                result.Position = Center;
                return result;
            }

            public Mesh DebugMesh()
            {                
                Mesh patch = new Mesh(hasNormals: true, hasColors: true);
                foreach(var p in this.points)
                {
                    patch.Vertices.Add(new Vertex(p.Position, this.Normal, new Vector4(0, 0, 1, 1)));
                    patch.Vertices.Add(new Vertex(p.NormalProjectedPosition, this.Normal, new Vector4(1, 0, 0, 1)));
                }
                return patch;
            }

        }       


        public VertexWithRoughness CalculateRoughness(Vector3 position, double distance, string debugPatchPath = null)
        {
            var nn = meshOperator.NearestVerticesStrict(position, distance);
            var p = new Patch(position, nn);
            if (debugPatchPath != null)
            {
                p.DebugMesh().Save(debugPatchPath);
            }
            return p.Roughness();
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
                }
                else
                {
                    WriteDoubleValue(rv.RMS, s);
                    WriteDoubleValue(rv.AverageDistance, s);
                    WriteDoubleValue(rv.Variance, s);
                    WriteDoubleValue(rv.Range, s);
                }
            }           
        }
    }
}

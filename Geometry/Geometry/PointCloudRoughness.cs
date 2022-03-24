using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;
using JPLOPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JPLOPS.Geometry
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

        public Patch(Vertex sampleVertex, List<Vertex> verts, bool useSampleNormal = false)
        {
            this.SampleVertex = sampleVertex;
            if (!useSampleNormal)
            {
                var plane = new PlaneFit(verts);
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
                Center /= verts.Count;
                Normal = sampleVertex.Normal;
            }
            foreach (var v in verts)
            {
                points.Add(new PatchPoint(v.Position, this));
            }
        }

        public double DistanceFromCenter(Vector3 p)
        {
            var planeToPoint = p - this.Center;
            // See projections: https://math.oregonstate.edu/home/programs/undergrad/CalculusQuestStudyGuides/vcalc/dotprod/dotprod.html
            return Vector3.Dot(planeToPoint, Normal) / Normal.Length();
        }

        public VertexWithRoughness Roughness()
        {
            ///This method is an inner loop method so it avoids linq queries such as ToArray() Select() Max() and Min()
            if (points.Count == 0)
            {                
                return new VertexWithRoughness();
            }
            var result = new VertexWithRoughness();
            result.Position = SampleVertex.Position;
            result.Normal = SampleVertex.Normal;
            result.UV = SampleVertex.UV;
            result.Color = SampleVertex.Color;
            double[] distancesFromCenter = new double[points.Count];
            var max = double.MinValue;
            var min = double.MaxValue;
            for(int i = 0; i < distancesFromCenter.Length; i++)
            {
                var d = points[i].DistanceFromCenter;
                distancesFromCenter[i] = d;
                max = Math.Max(max, d);
                min = Math.Min(min, d);

            }
            //var distancesFromCenter = points.Select(p => p.DistanceFromCenter).ToArray();
            if (distancesFromCenter.Length != 0)
            {
                // Compute all values that require signed distances
                result.Range = max - min;
                result.Variance = MathE.SampleVariance(distancesFromCenter);
                // Convert distances to absolute value and compute unsigned distance values
                for(int i = 0; i < distancesFromCenter.Length; i++)
                {
                    distancesFromCenter[i] = Math.Abs(distancesFromCenter[i]);
                }
                result.RMS = MathE.RMS(distancesFromCenter);
                result.AverageDistance = MathE.Average(distancesFromCenter);
                // Attempt to match the cloud compare roughness criteria
                // https://www.cloudcompare.org/doc/wiki/index.php?title=Roughness
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
            Random r = NumberHelper.MakeRandomGenerator();
            for(int i = 0; i < samples; i++)
            {
                var index =  r.Next(0, sampleCloud.Vertices.Count - 1);
                var nn = meshOperator.NearestVerticesStrict(sampleCloud.Vertices[index].Position, distance);
                avg.Push(nn.Count());
            }
            return avg;
        }

        public class RoughnessPLYWriter : PLYMaximumCompatibilityWriter
        {
            protected override void WriteVertexStructureHeader(Mesh m, StreamWriter sw)
            {
                base.WriteVertexStructureHeader(m, sw);
                string dt = writeValueAsFloat ? "float" : "double";
                sw.WriteLine($"property {dt} roughness_rms");
                sw.WriteLine($"property {dt} average_distance");
                sw.WriteLine($"property {dt} variance");
                sw.WriteLine($"property {dt} range");
                sw.WriteLine($"property {dt} distance_from_center");
            }

            public override void WriteVertex(Mesh m, Vertex v, Stream s)
            {
                base.WriteVertex(m, v, s);
                VertexWithRoughness rv = (VertexWithRoughness)v;
                if (writeValueAsFloat)
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

using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.MathExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public class ConvexHull
    {
        readonly Mesh Mesh;
        public List<Vertex> Vertices
        {
            get { return Mesh.Vertices; }
        }

        readonly List<Plane> Planes;

        /// <summary>
        /// Construct from a list of 3D points.
        /// </summary>
        public ConvexHull(IList<double[]> points)
        {
            var hull = MIConvexHull.ConvexHull.Create(points);
            var faces = hull.Faces.ToArray();
            Mesh = new Mesh(true, false, false, faces.Length * 3);
            Planes = new List<Plane>(faces.Length);
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                var normal = Vector3.Normalize(new Vector3(f.Normal));
                var pt0 = new Vector3(f.Vertices[0].Position);
                Planes.Add(new Plane(normal, -normal.Dot(pt0)));

                int baseIdx = Mesh.Vertices.Count;
                for (int j = 0; j < f.Vertices.Length; j++)
                {
                    int idx = Mesh.Vertices.Count;
                    Mesh.Vertices.Add(new Vertex(
                        new Vector3(f.Vertices[j].Position),
                        normal,
                        Vector4.Zero,
                        Vector2.Zero
                        ));
                }
                Mesh.Faces.Add(new Face(Enumerable.Range(baseIdx, f.Vertices.Length).ToArray()));
            }
        }
        public ConvexHull(IEnumerable<Vector3> points)
            : this(points.Select(pt => pt.ToDoubleArray()).ToList()) { }
        public ConvexHull(Mesh mesh)
            : this(mesh.Vertices.Select(vert => vert.Position)) { }
        public ConvexHull(ConvexHull other)
        {
            Mesh = new Mesh(other.Mesh);
            Planes = new List<Plane>(other.Planes);
        }
        public static ConvexHull Union(params ConvexHull[] hulls)
        {
            return new ConvexHull(hulls.SelectMany(h => h.Vertices.Select(vtx => vtx.Position)));
        }

        public static ConvexHull FromImage(Image image, double nearClip = 0.1, double farClip = 20)
        {
            return FromParams(image.CameraModel, image.Width, image.Height, nearClip, farClip);
        }

        public static ConvexHull FromParams(CameraModel camera, int width, int height, double nearClip=0.1, double farClip=20)
        {
            // Get points - just corners for linear models, denser otherwise
            int subdiv = 2;
            if (!camera.Linear) subdiv = 5;

            List<Vector3> pts = new List<Vector3>();
            for (int i = 0; i < subdiv; i++)
            {
                double x = (width - 1.0) * (i / (subdiv - 1.0));
                for (int j = 0; j < subdiv; j++)
                {
                    double y = (height - 1.0) * (j / (subdiv - 1.0));
                    for (int k = 0; k < subdiv; k++)
                    {
                        Ray ray = camera.Unproject(new Vector2(x, y));
                        
                        Plane nearClipPlane = new Plane(-camera.ImagePlaneNormal, Vector3.Dot(camera.ImagePlaneNormal, ray.Position) + nearClip);
                        Plane farClipPlane = new Plane(-camera.ImagePlaneNormal, Vector3.Dot(camera.ImagePlaneNormal, ray.Position) + farClip);

                        double rayDistNear = ray.Intersects(nearClipPlane).Value;
                        double rayDistFar = ray.Intersects(farClipPlane).Value;
                        double rayDist = MathHelper.Lerp(rayDistNear, rayDistFar, k / (double)(subdiv - 1));

                        pts.Add(ray.Position + rayDist * ray.Direction);
                    }
                }
            }

            return new ConvexHull(pts);
        }

        /// <summary>
        /// Return true if this convex hull intersects another.
        /// </summary>
        public bool Intersects(ConvexHull other)
        {
            return GJKIntersection.Intersects(Mesh, other.Mesh);
        }

        /// <summary>
        /// Return true if <paramref name="ray"/> intersects this hull betwen <paramref name="minT"/> and <paramref name="maxT"/>.
        /// </summary>
        public bool Intersects(Ray ray, double minT = 0, double maxT = double.PositiveInfinity)
        {
            foreach (var plane in Planes)
            {
                double? t = ray.Intersects(plane);
                if (!t.HasValue)
                {
                    // If the ray is parallel to this plane, check if it is
                    // entirely above it.
                    double planeDist = plane.DotCoordinate(ray.Position);
                    if (planeDist > 0)
                    {
                        return false;
                    }
                }
                else
                {
                    bool inward = ray.Direction.Dot(plane.Normal) < 0;
                    if (inward && t.Value > minT) minT = t.Value;
                    if (!inward && t.Value < maxT) maxT = t.Value;
                }

                if (minT > maxT) return false;
            }
            return true;
        }

        /// <summary>
        /// Return True if <paramref name="pt"/> is entirely within this hull.
        /// </summary>
        public bool Contains(Vector3 pt)
        {
            foreach (var plane in Planes)
            {
                if (plane.DotCoordinate(pt) > 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Compute the Minkowski sum of two convex hulls.
        /// </summary>
        public static ConvexHull MinkowskiSum(ConvexHull one, ConvexHull two)
        {
            int numPts = one.Vertices.Count * two.Vertices.Count;
            List<Vector3> pts = new List<Vector3>(numPts);
            for (int i = 0; i < one.Vertices.Count; i++)
            {
                var ptOne = one.Vertices[i].Position;
                for (int j = 0; j < two.Vertices.Count; j++)
                {
                    var ptTwo = one.Vertices[j].Position;
                    pts.Add(ptOne + ptTwo);
                }
            }
            return new ConvexHull(pts);
        }
        
        /// <summary>
        /// Transform this hull by a given matrix.
        /// </summary>
        public void Transform(Matrix mat)
        {
            Mesh.Transform(mat);
            for (int i = 0; i < Planes.Count; i++)
            {
                Planes[i] = Plane.Transform(Planes[i], mat);
            }
        }
        
        /// <summary>
        /// Return a copy of this hull transformed by a matrix.
        /// </summary>
        public static ConvexHull Transformed(ConvexHull hull, Matrix mat)
        {
            ConvexHull res = new ConvexHull(hull);
            res.Transform(mat);
            return res;
        }


        /// <summary>
        /// Return a copy of this hull transformed by an uncertain transform.
        /// </summary>
        public static ConvexHull Transformed(ConvexHull hull, UncertainRigidTransform transform, double sigma=3.0, int numSamples=10)
        {
            // If input transform is actually just a matrix, fall back to other overload for performance
            if (!transform.Uncertain) return Transformed(hull, transform.Mean);

            List<double[]> pts = new List<double[]>();
            foreach (var vtx in hull.Mesh.Vertices)
            {
                var pt = vtx.Position;
                GaussianND transformed = transform.TransformPoint(pt);
                var cov = transformed.Covariance;

                foreach (var ptp in UnscentedTransform.SigmaPoints(transformed))
                {
                    pts.Add(ptp.ToArray());
                }
            }
            return new ConvexHull(pts);
        }
    }
}

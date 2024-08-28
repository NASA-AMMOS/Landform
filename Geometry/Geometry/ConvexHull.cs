using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using MIConvexHull;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    public class ConvexHull
    {
        public const double DEF_NEAR_CLIP = 0.1, DEF_FAR_CLIP = 20;

        public Mesh Mesh { get; private set; }

        public List<Plane> Planes { get; private set; }

        public List<Vertex> Vertices { get { return Mesh.Vertices; } }

        public bool IsEmpty { get { return Mesh.Vertices.Count == 0; } }

        /// <summary>
        /// Construct from a list of 3D points.
        /// </summary>
        private ConvexHull(IList<double[]> points)
        {
            //only keep unique points
            points = new HashSet<Vector3>(points.Select(p => new Vector3(p))).Select(p => p.ToDoubleArray()).ToList();

            if (points.Count == 0)
            {
                //empty
                Mesh = new Mesh();
                Planes = new List<Plane>();
            }
            else if (points.Count < 3)
            {
                var fallback = Fallback(points.Select(p => new Vector3(p)));
                Planes = fallback.Planes;
                Mesh = fallback.Mesh;
            }
            else if (points.Count == 3)
            {
                // a single triangle will fail in the convex hull library, build a mesh of the two windings
                // of the same triangle

                int numFaces = 2;                
                Mesh = new Mesh(true, false, false, numFaces * 3);

                int numPlanes = 2 + 3; //one for each side of the triangle, 3 for the edges
                Planes = new List<Plane>(numPlanes);

                SingleTriangleHull(points);
            }
            else
            {
                var result = MIConvexHull.ConvexHull.Create(points);
                if (result.Outcome != ConvexHullCreationResultOutcome.Success)
                {
                    throw new Exception($"MIConvexHull failed ({result.Outcome}): {result.ErrorMessage}");
                }
                var faces = result.Result.Faces.ToArray();
                Mesh = new Mesh(hasNormals: true);
                Planes = new List<Plane>(faces.Length);
                Mesh.Vertices.Capacity = faces.Sum(f => f.Vertices.Length);
                Mesh.Faces.Capacity = faces.Sum(f => (f.Vertices.Length - 2));
                for (int i = 0; i < faces.Length; i++)
                {
                    var f = faces[i];
                    var n = Vector3.Normalize(new Vector3(f.Normal));

                    Planes.Add(new Plane(n, -n.Dot(new Vector3(f.Vertices[0].Position))));

                    int k = Mesh.Vertices.Count;
                    for (int j = 0; j < f.Vertices.Length; j++)
                    {
                        Mesh.Vertices.Add(new Vertex(new Vector3(f.Vertices[j].Position), n));
                    }

                    //create single triangle for 3 vertex face
                    //or triangle fan for face with more than 3 vertices
                    for (int t = 0; t < f.Vertices.Length - 2; t++)
                    {
                        int a = k;
                        int b = k + 1 + t;
                        int c = k + 2 + t;
                        if (!Triangle.ComputeNormal(Mesh.Vertices[a].Position,
                                                    Mesh.Vertices[b].Position,
                                                    Mesh.Vertices[c].Position, out Vector3 fn) ||
                            n.Dot(fn) >= 0)
                        {
                            Mesh.Faces.Add(new Face(a, b, c));
                        }
                        else
                        {
                            Mesh.Faces.Add(new Face(c, b, a));
                        }
                    }
                }

                Mesh.Clean();

                Planes = new HashSet<Plane>(Planes).ToList(); //only keep unique planes
            }
        }

        private ConvexHull(IEnumerable<Vector3> points) : this(points.Select(pt => pt.ToDoubleArray()).ToList()) { }

        private ConvexHull(Mesh mesh) : this(mesh.Vertices.Select(vert => vert.Position)) { }

        private ConvexHull(Mesh mesh, List<Plane> planes)
        {
            this.Mesh = mesh;
            this.Planes = planes;
        }

        public ConvexHull(ConvexHull other)
        {
            Mesh = new Mesh(other.Mesh);
            Planes = new List<Plane>(other.Planes);
        }

        public ConvexHull()
        {
            Mesh = new Mesh();
            Planes = new List<Plane>();
        }

        public static ConvexHull Create(BoundingBox box)
        {
            return new ConvexHull(box.ToMesh(), box.FacePlanes());
        }

        /// <summary>
        /// MIConvexHull throws exception if we ask it to create a 3D hull but the input points are degenerate.
        /// Degeneracies include: all input points coplanar, or fewer than 3 input points.
        /// We handle the specific case of exactly 3 non-coincident input points above.
        /// But we don't currently have a nice implementation to handle the following cases:
        /// * fewer than 3 input points
        /// * 3 input points with any 2 points coincident
        /// * more than 3 input points but all points coplanar (including 3 or fewer unique points due to coincidences)
        /// For now this function gets a hull for all cases by falling back to a mesh bounding box with a min size.
        /// That's conservative and probably good enough for what we're doing right now.
        /// </summary>
        public static ConvexHull Create(Mesh mesh)
        {
            try
            {
                return mesh.Vertices.Count > 0 ? new ConvexHull(mesh) : new ConvexHull();
            }
            catch
            {
                return Fallback(mesh.Vertices.Select(v => v.Position));
            }
        }

        public static ConvexHull Create(IEnumerable<Vector3> pts)
        {
            try
            {
                return pts.Count() > 0 ? new ConvexHull(pts) : new ConvexHull();
            }
            catch
            {
                return Fallback(pts);
            }
        }

        private static ConvexHull Fallback(IEnumerable<Vector3> pts)
        {
            return Create(BoundingBoxExtensions.CreateFromPoints(pts, minSize: 1e-6));
        }

        public static ConvexHull Union(params ConvexHull[] hulls)
        {
            return ConvexHull.Create(hulls.SelectMany(h => h.Vertices.Select(vtx => vtx.Position)));
        }

        public static ConvexHull FromImage(Image image, double nearClip = DEF_NEAR_CLIP, double farClip = DEF_FAR_CLIP,
                                           bool forceLinear = false)
        {
            return FromParams(image.CameraModel, image.Width, image.Height, nearClip, farClip, forceLinear);
        }

        public static ConvexHull FromParams(CameraModel camera, double width, double height,
                                            double nearClip = DEF_NEAR_CLIP, double farClip = DEF_FAR_CLIP,
                                            bool forceLinear = false)
        {
            // Get points - just corners for linear models, denser otherwise
            int subdiv = 2;
            if (!forceLinear && !camera.Linear)
            {
                subdiv = 5;
            }

            List<Vector3> pts = new List<Vector3>();
            for (int i = 0; i < subdiv; i++)
            {
                double x = (width - 1.0) * (i / (subdiv - 1.0));
                for (int j = 0; j < subdiv; j++)
                {
                    double y = (height - 1.0) * (j / (subdiv - 1.0));
                    Ray ray = camera.Unproject(new Vector2(x, y));

                    pts.Add(ray.Position + nearClip * ray.Direction); //camera pupil can vary per-pixel in CAHVORE
                    pts.Add(ray.Position + farClip * ray.Direction); 
                }
            }

            return ConvexHull.Create(pts);
        }

        public static ConvexHull FromConvexMesh(Mesh mesh)
        {
            var planes = new HashSet<Plane>();
            foreach (var f in mesh.Faces)
            {
                var n = mesh.HasNormals ? mesh.Vertices[f.P0].Normal : Vector3.Zero;
                if ((mesh.HasNormals && n == mesh.Vertices[f.P1].Normal && n == mesh.Vertices[f.P2].Normal) ||
                    Triangle.ComputeNormal(mesh.Vertices[f.P0].Position,
                                           mesh.Vertices[f.P1].Position,
                                           mesh.Vertices[f.P2].Position, out n)) //not degenerate, always normalized
                {
                    planes.Add(new Plane(n, -(n.Dot(mesh.Vertices[f.P0].Position))));
                }
            }
            return new ConvexHull(mesh, planes.ToList());
        }

        /// <summary>
        /// Return true if this convex hull intersects another.
        /// </summary>
        public bool Intersects(ConvexHull other)
        {
            return !IsEmpty && !other.IsEmpty && GJKIntersection.Intersects(Mesh, other.Mesh);
        }

        public bool Intersects(Triangle tri)
        {
            return !IsEmpty && GJKIntersection.Intersects(Mesh, new Mesh((new[] { tri }).ToList()));
        }

        public bool Intersects(BoundingBox box)
        {
            return !IsEmpty && GJKIntersection.Intersects(Mesh, box.ToMesh());
        }

        /// <summary>
        /// Return true if <paramref name="ray"/> intersects this hull betwen <paramref name="minT"/> and <paramref name="maxT"/>.
        /// </summary>
        public bool Intersects(Ray ray, double minT = 0, double maxT = double.PositiveInfinity)
        {
            if (IsEmpty)
            {
                return false;
            }

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
        public bool Contains(Vector3 pt, double epsilon=0)
        {
            if (IsEmpty)
            {
                return false;
            }

            foreach (var plane in Planes)
            {
                if (plane.DotCoordinate(pt) > epsilon) return false;
            }

            return true;
        }

        /// <summary>
        /// Compute the Minkowski sum of two convex hulls.
        /// If either input is empty then the result will (correctly) be empty.
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
        public static ConvexHull Transformed(ConvexHull hull, UncertainRigidTransform transform,
                                             double sigma = 3.0, int numSamples = 10)
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

        private void SingleTriangleHull(IList<double[]> points)
        {
            if (points.Count != 3)
                throw new ArgumentException("three points expected for triangle");

            Vector3 pt0 = new Vector3(points[0]);
            Vector3 pt1 = new Vector3(points[1]);
            Vector3 pt2 = new Vector3(points[2]);

            if (pt0 == pt1 || pt1 == pt2 || pt2 == pt0)
                throw new ArgumentException("invalid triangle, coincident points");

            Vector3 edge0 = pt1 - pt0;
            Vector3 edge1 = pt2 - pt0;
            Vector3 edge2 = pt2 - pt1;

            //face normals/planes
            Vector3 normal0 = Vector3.Normalize(Vector3.Cross(edge1, edge0)); //LH cross product
            Vector3 normal1 = normal0 * -1;
            Planes.Add(new Plane(normal0, -normal0.Dot(pt0)));
            Planes.Add(new Plane(normal1, -normal1.Dot(pt0)));

            //edge normals/planes
            Vector3 edge0Normal = Vector3.Cross(edge0, normal1);
            Vector3 edge1Normal = Vector3.Cross(normal1, edge1);
            Vector3 edge2Normal = Vector3.Cross(edge2, normal1);
            Planes.Add(new Plane(edge0Normal, -edge0Normal.Dot(pt0)));
            Planes.Add(new Plane(edge1Normal, -edge1Normal.Dot(pt0)));
            Planes.Add(new Plane(edge2Normal, -edge2Normal.Dot(pt2)));
            
            Mesh.Vertices.Add(new Vertex(pt0, normal0, Vector4.Zero, Vector2.Zero));
            Mesh.Vertices.Add(new Vertex(pt1, normal0, Vector4.Zero, Vector2.Zero));
            Mesh.Vertices.Add(new Vertex(pt2, normal0, Vector4.Zero, Vector2.Zero));
            Mesh.Vertices.Add(new Vertex(pt0, normal1, Vector4.Zero, Vector2.Zero));
            Mesh.Vertices.Add(new Vertex(pt1, normal1, Vector4.Zero, Vector2.Zero));
            Mesh.Vertices.Add(new Vertex(pt2, normal1, Vector4.Zero, Vector2.Zero));

            Mesh.Faces.Add(new Face(0, 1, 2));
            Mesh.Faces.Add(new Face(3, 5, 4));
        }
    }
}

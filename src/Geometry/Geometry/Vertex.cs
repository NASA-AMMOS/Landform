using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
{
    public class Vertex : ICloneable
    {
        /// <summary>
        /// Required property of all Vertex objects
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// Optional property
        /// </summary>
        public Vector3 Normal;
        /// <summary>
        /// Optional property
        /// </summary>
        public Vector2 UV;
        /// <summary>
        /// Optional property
        /// </summary>
        public Vector4 Color;
        
        public Vertex() { }

        public Vertex(Vector3 postion)
        {
            this.Position = postion;
        }

        public Vertex(double x, double y, double z)
        {
            this.Position = new Vector3(x, y, z);
        }

        public Vertex(double x, double y, double z, double nx, double ny, double nz)
        {
            this.Position = new Vector3(x, y, z);
            this.Normal = new Vector3(nx, ny, nz);
        }

        public Vertex(double x, double y, double z,
                      double nx, double ny, double nz,
                      double u, double v,
                      double r, double g, double b, double a)
        {
            this.Position = new Vector3(x, y, z);
            this.Normal = new Vector3(nx, ny, nz);
            this.UV = new Vector2(u, v);
            this.Color = new Vector4(r, g, b, a);
        }

        public Vertex(Vector3 position, Vector3 normal)
        {
            this.Position = position;
            this.Normal = normal;
        }

        public Vertex(Vector3 position, Vector3 normal, Vector4? color)
        {
            this.Position = position;
            this.Normal = normal;
            if (color.HasValue)
            {
                this.Color = color.Value;
            }
        }

        public Vertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv)
        {
            this.Position = position;
            this.Normal = normal;
            this.Color = color;
            this.UV = uv;
        }

        /// <summary>
        /// Copy constructor.  Note that you should almost always use Vertex.Clone
        /// instead so that methods work with types that extend Vertex with additional properties
        /// </summary>
        /// <param name="other"></param>
        public Vertex(Vertex other)
        {
            this.Position = other.Position;
            this.Normal = other.Normal;
            this.Color = other.Color;
            this.UV = other.UV;
        }


        /// <summary>
        /// Returns true if the vertices are equivlenet
        /// </summary>
        /// <param name="v"></param>
        /// <param name="eps"></param>
        /// <returns></returns>
        public virtual bool AlmostEqual(Vertex v, double eps = MathE.EPSILON)
        {
            return  Vector3.AlmostEqual(this.Position, v.Position, eps) &&
                    Vector3.AlmostEqual(this.Normal, v.Normal, eps) &&
                    Vector2.AlmostEqual(this.UV, v.UV, eps) &&
                    Vector4.AlmostEqual(this.Color, v.Color, eps);
        }

        public override bool Equals(System.Object obj)
        {
            return Equals(obj as Vertex);
        }

        public bool Equals(Vertex v)
        {
            // For Equals implementation see https://msdn.microsoft.com/en-us/library/dd183755.aspx 
            // If parameter is null, return false.
            if (Object.ReferenceEquals(v, null))
            {
                return false;
            }

            // Optimization for a common success case.
            if (Object.ReferenceEquals(this, v))
            {
                return true;
            }

            // If run-time types are not exactly the same, return false.
            if (this.GetType() != v.GetType())
            {
                return false;
            }

            // Return true if the fields match.
            // Note that the base class is not invoked because it is
            // System.Object, which defines Equals as reference equality.
            return (Position == v.Position) && (Normal == v.Normal) && (Color == v.Color) && (UV == v.UV);
        }

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + Position.X.GetHashCode();
            hash = hash * 23 + Position.Y.GetHashCode();
            hash = hash * 23 + Position.Z.GetHashCode();
            hash = hash * 23 + UV.X.GetHashCode();
            hash = hash * 23 + UV.Y.GetHashCode();
            hash = hash * 23 + Normal.X.GetHashCode();
            hash = hash * 23 + Normal.Y.GetHashCode();
            hash = hash * 23 + Normal.Z.GetHashCode();
            hash = hash * 23 + Color.X.GetHashCode();
            hash = hash * 23 + Color.Y.GetHashCode();
            hash = hash * 23 + Color.Z.GetHashCode();
            hash = hash * 23 + Color.W.GetHashCode();
            return hash;
        }

        public virtual object Clone()
        {
            return new Vertex(this);
        }

        /// <summary>
        /// Lerp between vertices, lerps all attributes (position, normal, uv, and color)
        /// </summary>
        /// <param name="a">start</param>
        /// <param name="b">end</param>
        /// <param name="t">amount of lerp 0-1</param>
        /// <returns></returns>
        public static Vertex Lerp(Vertex a, Vertex b, double t)
        {
            Vertex result = new Vertex();
            result.Position = Vector3.Lerp(a.Position, b.Position, t);
            result.Normal = Vector3.Lerp(a.Normal, b.Normal, t);
            result.UV = Vector2.Lerp(a.UV, b.UV, t);
            result.Color = Vector4.Lerp(a.Color, b.Color, t);
            return result;
        }

        /// <summary>
        /// Generate a Bounding box of zero size centered on this vertex's position
        /// </summary>
        /// <returns></returns>
        public BoundingBox Bounds()
        {
            return new BoundingBox(this.Position, this.Position);
        }

        /// <summary>
        /// Get a bounding box of zero size representing this vertex's uv
        /// </summary>
        /// <returns></returns>
        public BoundingBox UVBounds()
        {
            var uv3 = new Vector3(UV.X, UV.Y, 0);
            return new BoundingBox(uv3, uv3);
        }

        public class Comparer : IEqualityComparer<Vertex>
        {
            bool matchPositions, matchNormals, matchUVs, matchColors;

            public Comparer(bool matchPositions = true, bool matchNormals = true, bool matchUVs = true,
                            bool matchColors = true)
            {
                if (!matchPositions && !matchNormals && !matchUVs && !matchColors)
                {
                    throw new ArgumentException("nothing to compare");
                }
                this.matchPositions = matchPositions;
                this.matchNormals = matchNormals;
                this.matchUVs = matchUVs;
                this.matchColors = matchColors;
            }

            public bool Equals(Vertex a, Vertex b)
            {
                return ((!matchPositions || a.Position == b.Position) &&
                        (!matchNormals || a.Normal == b.Normal) &&
                        (!matchUVs || a.UV == b.UV) &&
                        (!matchColors || a.Color == b.Color));
            }

            public int GetHashCode(Vertex v)
            {
                int hash = 17;
                if (matchPositions)
                {
                    hash = hash * 23 + v.Position.X.GetHashCode();
                    hash = hash * 23 + v.Position.Y.GetHashCode();
                    hash = hash * 23 + v.Position.Z.GetHashCode();
                }
                if (matchNormals)
                {
                    hash = hash * 23 + v.Normal.X.GetHashCode();
                    hash = hash * 23 + v.Normal.Y.GetHashCode();
                    hash = hash * 23 + v.Normal.Z.GetHashCode();
                }
                if (matchUVs)
                {
                    hash = hash * 23 + v.UV.X.GetHashCode();
                    hash = hash * 23 + v.UV.Y.GetHashCode();
                }
                if (matchColors)
                {
                    hash = hash * 23 + v.Color.X.GetHashCode();
                    hash = hash * 23 + v.Color.Y.GetHashCode();
                    hash = hash * 23 + v.Color.Z.GetHashCode();
                }
                return hash;
            }
        }
    }
}

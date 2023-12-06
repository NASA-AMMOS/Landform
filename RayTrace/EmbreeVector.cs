using Embree;
using Microsoft.Xna.Framework;

namespace JPLOPS.RayTrace
{
    /// <summary>
    /// Floating point vector class for interop with embree
    /// </summary>
    internal class EmbreeVector : IEmbreeVector, IEmbreePoint
    {
        private readonly float x;
        private readonly float y;
        private readonly float z;

        /// <summary>
        /// Gets the vector's x-coordinate.
        /// </summary>
        public float X { get { return x; } }

        /// <summary>
        /// Gets the vector's y-coordinate.
        /// </summary>
        public float Y { get { return y; } }

        /// <summary>
        /// Gets the vector's z-coordinate.
        /// </summary>
        public float Z { get { return z; } }

        /// <summary>
        /// Constructs a new three-dimensional vector.
        /// </summary>
        public EmbreeVector(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>
        /// Constructs a new three-dimensional vector.
        /// </summary>
        public EmbreeVector(double x, double y, double z)
        {
            this.x = (float)x;
            this.y = (float)y;
            this.z = (float)z;
        }

        /// <summary>
        /// Constructs a new three-dimensional vector
        /// </summary>
        public EmbreeVector(Vector3 v)
        {
            this.x = (float)v.X;
            this.y = (float)v.Y;
            this.z = (float)v.Z;
        }

    }
}

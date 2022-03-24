using Embree;
using Microsoft.Xna.Framework;

namespace JPLOPS.RayTrace
{
    /// <summary>
    /// An immutable 3x4 transformation matrix.
    /// </summary>
    /// <remarks>
    /// This matrix is laid out in column major order.
    /// </remarks>
    internal class EmbreeMatrix : IEmbreeMatrix
    {
        private readonly EmbreeVector u;
        private readonly EmbreeVector v;
        private readonly EmbreeVector w;
        private readonly EmbreeVector t;

        public IEmbreeVector U { get { return u; } }
        public IEmbreeVector V { get { return v; } }
        public IEmbreeVector W { get { return w; } }
        public IEmbreeVector T { get { return t; } }

        public EmbreeMatrix(Matrix m)
        {
            u = new EmbreeVector(m.M11, m.M12, m.M13);
            v = new EmbreeVector(m.M21, m.M22, m.M23);
            w = new EmbreeVector(m.M31, m.M32, m.M33);
            t = new EmbreeVector(m.M41, m.M42, m.M43);
        }
    }
}

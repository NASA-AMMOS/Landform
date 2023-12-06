using Embree;
using Microsoft.Xna.Framework;

namespace JPLOPS.RayTrace
{
    /// <summary>
    /// Floating point ray for interop with Embree
    /// </summary>
    internal class EmbreeRay : IEmbreeRay
    {
        private readonly IEmbreePoint origin;
        private readonly IEmbreeVector direction;

        public IEmbreeVector Direction { get { return direction; } }

        public IEmbreePoint Origin { get { return origin; } }

        public EmbreeRay(Ray ray)
        {
            this.origin = new EmbreeVector(ray.Position);
            this.direction = new EmbreeVector(ray.Direction);
        }
    }
}

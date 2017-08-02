using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.RayTrace
{
    /// <summary>
    /// Represents the data returned on a ray collision
    /// </summary>
    public class HitData
    {
        /// <summary>
        /// Distance from origin to intersection
        /// </summary>
        public readonly double Distance;

        /// <summary>
        /// Point of intersection
        /// </summary>
        public readonly Vector3 Position;

        /// <summary>
        /// Normal at intersection as defined by vertices not normals on the mesh
        /// Normal is in world coordinates
        /// </summary>
        public readonly Vector3 Normal;

        /// <summary>
        /// UV at intersection point.  This is set only if the collision mesh had UVs
        /// </summary>
        public readonly Vector2? UV;

        /// <summary>
        /// Mesh that was hit
        /// </summary>
        public readonly Mesh mesh;

        /// <summary>
        /// Texture that was hit, may be null if mesh was added without a texture
        /// </summary>
        public readonly Image Texture;

        public HitData(Vector3 position, Vector3 normal, Vector2? uv, Mesh mesh, Image texture, double distance)
        {
            this.Distance = distance;
            this.Position = position;
            this.Normal = normal;
            this.UV = uv;
            this.mesh = mesh;
            this.Texture = texture;
        }
    }

}

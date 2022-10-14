using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace JPLOPS.RayTrace
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
        /// Normal of face intersected as defined by vertex position and winding order
        /// Normal is normalized and in world coordinates
        /// </summary>
        public readonly Vector3 FaceNormal;

        /// <summary>
        /// Normal at intersection point as interpolated between vertex normals
        /// Null if the mesh hit does not specify vertex normals
        /// Normal is in world coordinates, and is normalized only if the vertex normals were
        /// </summary>
        public readonly Vector3? PointNormal;

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

        public HitData(Vector3 position, Vector3 faceNormal, Vector3? pointNormal,
                       Vector2? uv, Mesh mesh, Image texture, double distance)
        {
            this.Distance = distance;
            this.Position = position;
            this.FaceNormal = faceNormal;
            this.PointNormal = pointNormal;
            this.UV = uv;
            this.mesh = mesh;
            this.Texture = texture;
        }
    }

}

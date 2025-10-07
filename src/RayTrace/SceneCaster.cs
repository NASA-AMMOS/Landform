using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TinyEmbree;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.MathExtensions;

namespace JPLOPS.RayTrace
{
    /// <summary>
    /// Class for executing raycasts using TinyEmbree raycast engine
    /// </summary>
    public class SceneCaster
    {
        private readonly Raytracer raytracer;
        private readonly Dictionary<TriangleMesh, MeshData> meshData;
        private bool sceneBuilt = false;

        private struct MeshData
        {
            public Mesh Mesh;
            public Image Texture;
            public Matrix NormalToWorld;
        }

        /// <summary>
        /// Create a new scene
        /// </summary>
        public SceneCaster()
        {
            raytracer = new Raytracer();
            meshData = new Dictionary<TriangleMesh, MeshData>();
        }

        /// <summary>
        /// Create and build a new scene for one mesh.
        /// </summary>
        public SceneCaster(JPLOPS.Geometry.Mesh mesh, Image texture, Matrix transform) : this()
        {
            AddMesh(mesh, texture, transform);
            Build();
        }

        /// <summary>
        /// Create and build a new scene for one mesh.
        /// </summary>
        public SceneCaster(JPLOPS.Geometry.Mesh mesh, Image texture = null) : this(mesh, texture, Matrix.Identity) { }

        /// <summary>
        /// Add a mesh to the scene.  Note that meshes are stored by reference and any modification to the mesh between
        /// this call and calls to Raycast will result in undetermined behaviour.  You should finish making all raycasts
        /// before mutating the mesh.
        /// </summary>
        /// <param name="mesh">Mesh to add.  If this mesh has UVs then so will HitData objects.</param>
        /// <param name="texture">Optional texture, if null HitData objects will not have a texture.</param>
        /// <param name="transform">This mesh's transform in the scene</param>
        public void AddMesh(JPLOPS.Geometry.Mesh mesh, Image texture, Matrix transform)
        {
            if (sceneBuilt)
            {
                throw new Exception("cannot add mesh after scene has been built");
            }

            // Transform vertices to world space for TinyEmbree
            var worldVertices = new System.Numerics.Vector3[mesh.Vertices.Count];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                worldVertices[i] = Vector3.Transform(mesh.Vertices[i].Position, transform).ToNumerics();
            }

            // Convert face indices
            var indices = new int[mesh.Faces.Count * 3];
            int idx = 0;
            foreach (var face in mesh.Faces)
            {
                indices[idx++] = face.P0;
                indices[idx++] = face.P1;
                indices[idx++] = face.P2;
            }

            var triMesh = new TriangleMesh(worldVertices, indices);
            raytracer.AddMesh(triMesh);

            var md = new MeshData
            {
                Mesh = mesh,
                Texture = texture,
                NormalToWorld = Matrix.Transpose(Matrix.Invert(transform))
            };
            meshData.Add(triMesh, md);
        }

        public void AddMesh(JPLOPS.Geometry.Mesh mesh, Image texture = null)
        {
            AddMesh(mesh, texture, Matrix.Identity);
        }

        /// <summary>
        /// Builds the scene.  Call build after all Meshes have been added but before raycasting
        /// </summary>
        public void Build()
        {
            if (!sceneBuilt)
            {
                raytracer.CommitScene();
                sceneBuilt = true;
            }
            else
            {
                throw new Exception("Scene has already been built");
            }
        }

        private static TinyEmbree.Ray TinyRay(Microsoft.Xna.Framework.Ray ray, double near) {
            return new TinyEmbree.Ray
            {
                Origin = ray.Position.ToNumerics(),
                Direction = ray.Direction.ToNumerics(),
                MinDistance = (float)near
            };
        }

        /// <summary>
        /// Raycast a single ray
        /// Returns null if no intersection
        /// NOTE: this will return both frontface and backface hits
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="near"></param>
        /// <param name="far"></param>
        /// <returns></returns>
        public HitData Raycast(Microsoft.Xna.Framework.Ray ray, double near = 0, double far = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }

            var hit = raytracer.Trace(TinyRay(ray, near));
            
            if (!hit || hit.Distance > far)
            {
                return null;
            }

            var md = meshData[hit.Mesh];
            Mesh mesh = md.Mesh;
            var f = mesh.Faces[(int)hit.PrimId];

            float u = hit.BarycentricCoords.X;
            float v = hit.BarycentricCoords.Y;

            Vector2? uv = null;
            if (mesh.HasUVs)
            {
                var tri = new Geometry.Triangle(mesh.Vertices[f.P0], mesh.Vertices[f.P1], mesh.Vertices[f.P2]);
                var bp = new Geometry.BarycentricPoint(1.0 - u - v, u, v, tri);
                uv = bp.UV;
            }

            Vector3? interpNorm = null;
            if (mesh.HasNormals)
            {
                var n0 = mesh.Vertices[f.P0].Normal;
                var n1 = mesh.Vertices[f.P1].Normal;
                var n2 = mesh.Vertices[f.P2].Normal;
                interpNorm = (1.0 - u - v) * n0 + u * n1 + v * n2;
                interpNorm = Vector3.TransformNormal(interpNorm.Value, md.NormalToWorld);
            }

            return new HitData(hit.Position.ToXna(), hit.Normal.ToXna(),
                               interpNorm, uv, mesh, md.Texture, hit.Distance);
        }

        /// NOTE: this will return both frontface and backface hits
        public Vector3? RaycastPosition(Microsoft.Xna.Framework.Ray ray, double near = 0,
                                        double far = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }

            var hit = raytracer.Trace(TinyRay(ray, near));
            
            if (!hit || hit.Distance > far)
            {
                return null;
            }

            return hit.Position.ToXna();
        }

        /// NOTE: this will return both frontface and backface hits
        public double? RaycastDistance(Microsoft.Xna.Framework.Ray ray, double near = 0,
                                       double far = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }

            var hit = raytracer.Trace(TinyRay(ray, near));
            
            if (!hit || hit.Distance > far)
            {
                return null;
            }

            return hit.Distance;
        }

        /// <summary>
        /// Check to see if there is anything along the rays path within distance
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public bool Occludes(Microsoft.Xna.Framework.Ray ray, double distance = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }

            return raytracer.IsOccluded(new ShadowRay(TinyRay(ray, 0), (float)distance));
        }

        ~SceneCaster()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                raytracer?.Dispose();
            }
        }
    }
}

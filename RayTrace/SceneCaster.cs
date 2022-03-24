using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Embree;
using JPLOPS.Imaging;

namespace JPLOPS.RayTrace
{
    /// <summary>
    /// Class for executing raycasts using Embree raycast engine
    /// </summary>
    public class SceneCaster
    {
        const SceneFlags SCENE_FLAGS =
            SceneFlags.Static | SceneFlags.Coherent | SceneFlags.Incoherent | SceneFlags.Robust;
        const TraversalFlags TRAVERSAL_FLAGS = TraversalFlags.Single;

        private readonly Device device;
        private readonly Scene<Model> scene;
        private bool sceneBuilt = false;

        /// <summary>
        /// Create a new scene
        /// </summary>
        public SceneCaster()
        {
            device = new Device();
            scene = new Scene<Model>(device, SCENE_FLAGS, TRAVERSAL_FLAGS);
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
            var model = new Model(device, mesh, texture, transform, SCENE_FLAGS, TRAVERSAL_FLAGS);
            scene.Add(model);
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
                scene.Commit();
                sceneBuilt = true;
            }
            else
            {
                throw new Exception("Scene has already been built");
            }
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
        public HitData Raycast(Ray ray, double near = 0, double far = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }
            var packet = scene.Intersects(new EmbreeRay(ray), (float)near, (float)far);
            Intersection<Model> hit = packet.ToIntersection<Model>(scene);
            return HitToHitData(ray, hit);
        }

        /// NOTE: this will return both frontface and backface hits
        public Vector3? RaycastPosition(Ray ray, double near = 0, double far = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }
            var packet = scene.Intersects(new EmbreeRay(ray), (float)near, (float)far);
            Intersection<Model> hit = packet.ToIntersection<Model>(scene);

            if (hit.HasHit)
            {
                return ray.Position + ray.Direction * hit.Distance;
            }
            else
            {
                return null;
            }
        }

        /// NOTE: this will return both frontface and backface hits
        public double? RaycastDistance(Ray ray, double near = 0, double far = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }
            var packet = scene.Intersects(new EmbreeRay(ray), (float)near, (float)far);
            Intersection<Model> hit = packet.ToIntersection<Model>(scene);

            if (hit.HasHit)
            {
                return hit.Distance;
            }
            else
            {
                return null;
            }
        }

        /// NOTE: this will return both frontface and backface hits
        public HitData[] Raycast4(Ray[] rays, double near = 0, double far = double.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }

            if (rays.Length != 4)
            {
                throw new Exception("Raycast4 expecting 4 rays");
            }

            var embreeRays = rays.Select(r => new EmbreeRay(r)).ToArray();
            var packet4 = scene.Intersects4(embreeRays, (float)near, (float)far);
            Intersection<Model>[] hits = packet4.ToIntersection<Model>(scene);

            HitData[] results = new HitData[4];
            for (int idx = 0; idx < 4; idx++)
            {
                results[idx] = HitToHitData(rays[idx], hits[idx]);
            }

            return results;
        }
        /// <summary>
        /// Compute hit data for a ray intersection
        /// Null if there was no hit
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="hit"></param>
        /// <returns></returns>
        static HitData HitToHitData(Ray ray, Intersection<Model> hit)
        {
            if (hit.HasHit)
            {
                var position = ray.Position + ray.Direction * hit.Distance;

                // Negate the normal direction coming out of embree.  Its poorly documented in the images on this page
                // https://www.embree.org/api.html#rtchit
                // but it looks like they use a different winding order than we assume for our normals
                // what is documented is that it's not normalized
                var faceNormal = -new Vector3(hit.NX, hit.NY, hit.NZ);
                if (faceNormal.LengthSquared() > 0)
                {
                    faceNormal = hit.Instance.NormalToWorldSpace(Vector3.Normalize(faceNormal));
                }

                var mesh = hit.Instance.Mesh;
                var f = mesh.Faces[(int)hit.Primitive];

                Vector2? uv = null;
                Vector3? interpNorm = null;

                // If this mesh has uvs compute the uv coordinates as per documentation
                // https://embree.github.io/api.html
                float u = hit.U;
                float v = hit.V;
                if (mesh.HasUVs)
                {
                    var tri = new Geometry.Triangle(mesh.Vertices[f.P0], mesh.Vertices[f.P1], mesh.Vertices[f.P2]);
                    var bp = new Geometry.BarycentricPoint(1.0 - u - v, u, v, tri);
                    uv = bp.UV;
                }
                if (mesh.HasNormals)
                {
                    var n0 = mesh.Vertices[f.P0].Normal;
                    var n1 = mesh.Vertices[f.P1].Normal;
                    var n2 = mesh.Vertices[f.P2].Normal;
                    interpNorm = (1.0 - u - v) * n0 + u * n1 + v * n2;
                    interpNorm = hit.Instance.NormalToWorldSpace(interpNorm.Value);
                }
                return new HitData(position, faceNormal, interpNorm, uv, mesh, hit.Instance.Texture, hit.Distance);
            }
            return null;
        }

        /// <summary>
        /// Check to see if there is anything along the rays path within distance
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public bool Occludes(Ray ray, double distance = double.PositiveInfinity)
        {
            return scene.Occludes(new EmbreeRay(ray), 0, (float)distance);
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
                foreach (var model in scene)
                {
                    model.Dispose();
                }
                scene.Dispose();
                device.Dispose();
            }
        }
    }
}

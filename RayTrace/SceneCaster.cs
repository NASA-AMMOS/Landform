using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Embree;
using System.Collections.Concurrent;
using OPS.Imaging;
using OPS.Util;

namespace OPS.RayTrace
{
    /// <summary>
    /// Class for executing raycasts using Embree raycast engine
    /// </summary>
    public class SceneCaster
    {
        private readonly Scene<Model> scene;
        const SceneFlags SCENE_FLAGS = SceneFlags.Static | SceneFlags.Coherent | SceneFlags.Incoherent | SceneFlags.Robust;
        const TraversalFlags TRAVERSAL_FLAGS = TraversalFlags.Single;
        bool sceneBuilt = false;
        Device device;

        /// <summary>
        /// Create a new scene
        /// </summary>
        public SceneCaster()
        {
            device = new Device();
            scene = new Scene<Model>(device, SCENE_FLAGS, TRAVERSAL_FLAGS);
        }

        /// <summary>
        /// Add a mesh to the scene.  Note that meshes are stored by reference and any modification to the mesh between this call
        /// and calls to Raycast will result in undetermined behaviour.  You should finish making all raycasts before mutating the mesh.
        /// </summary>
        /// <param name="mesh">Mesh to add.  If this mesh has UVs then so will HitData objects retured by collisions</param>
        /// <param name="texture">Optional texture, if null hit objects returned by collisions with this mesh will not have a texture</param>
        /// <param name="transform">This meshes transform in the scene</param>
        public void AddMesh(OPS.Geometry.Mesh mesh, Image texture, Matrix transform)
        {
            if(sceneBuilt)
            {
                throw new Exception("Cannot add mesh to a renderer after it its scene has been built");
            }
            var model = new Model(device, mesh, texture, transform, SCENE_FLAGS, TRAVERSAL_FLAGS);
            scene.Add(model);
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
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="near"></param>
        /// <param name="far"></param>
        /// <returns></returns>
        public HitData Raycast(Ray ray, float near = 0, float far = float.PositiveInfinity)
        {
            if (!sceneBuilt)
            {
                throw new Exception("Must call Build on scene before raycasting");
            }
            var packet = scene.Intersects(new EmbreeRay(ray), near, far);
            Intersection<Model> hit = packet.ToIntersection<Model>(scene);
            return HitToHitData(ray, hit);
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
                // Negate the normal direction coming out of embree.  Its poorly documented in the images on this page
                // https://embree.github.io/api.html but it looks like they use a different winding order than we assume for our normals
                var modelSpaceNormal = - new Vector3(hit.NX, hit.NY, hit.NZ);
                var worldSpaceNormal = hit.Instance.NormalToWorldSpace(modelSpaceNormal);
                var position = ray.Position + ray.Direction * hit.Distance;
                var mesh = hit.Instance.Mesh;
                // If this mesh has uvs compute the uv coordinates as per documentation
                // https://embree.github.io/api.html
                float u = hit.U;
                float v = hit.V;
                var f = mesh.Faces[(int)hit.Primitive];
                Vector2? uv = null;
                if (mesh.HasUVs)
                {                    
                    var t0 = mesh.Vertices[f.P0].UV;
                    var t1 = mesh.Vertices[f.P1].UV;
                    var t2 = mesh.Vertices[f.P2].UV;
                    uv = (1.0 - u - v) * t0 + u * t1 + v * t2;                    
                }
                Vector3? meshNorm = null;
                if(mesh.HasNormals)
                {
                    var n0 = mesh.Vertices[f.P0].Normal;
                    var n1 = mesh.Vertices[f.P1].Normal;
                    var n2 = mesh.Vertices[f.P2].Normal;
                    meshNorm = (1.0 - u - v) * n0 + u * n1 + v * n2;
                    meshNorm = hit.Instance.NormalToWorldSpace(meshNorm.Value);
                }
                return new HitData(position, worldSpaceNormal, meshNorm, uv, mesh, hit.Instance.Texture, hit.Distance);
            }
            return null;
        }

        /// <summary>
        /// Check to see if there is anything along the rays path within distance
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public bool Occludes(Ray ray, float distance = float.PositiveInfinity)
        {
            return scene.Occludes(new EmbreeRay(ray), 0, distance);
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

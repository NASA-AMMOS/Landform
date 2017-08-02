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
        const TraversalFlags TRAVERSAL_FLAGS = TraversalFlags.Single | TraversalFlags.Packet4;
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
        /// Add a mesh to the scene
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
            var packet = scene.Intersects(new EmbreeRay(ray), near, far);
            Intersection<Model> hit = packet.ToIntersection<Model>(scene);
            return HitToHitData(ray, hit);
        }

        /// <summary>
        /// Raycast multipe rays in parallel and take advantage of multi-ray packet acceleration
        /// Null elements in the array correspond to areas with no intersection
        /// </summary>
        /// <param name="rays"></param>
        /// <param name="near"></param>
        /// <param name="far"></param>
        /// <returns></returns>
        public HitData[] Raycast(Ray[] rays, float near=0, float far = float.PositiveInfinity)
        {
            HitData[] results = new HitData[rays.Count()];
            int packetSize = 4;            
            Parallel.For(0, rays.Length / packetSize, i =>
            {
                var curRays = new EmbreeRay[packetSize];
                for (int j = 0; j < packetSize; j++)
                {
                    curRays[j] = new EmbreeRay(rays[i * packetSize + j]);
                }
                var packet = scene.Intersects4(curRays, near, far);
                var hits = packet.ToIntersection<Model>(scene);
                for (int j = 0; j < packetSize; j++)
                {
                    results[i * packetSize + j] = HitToHitData(rays[i * packetSize + j], hits[j]);
                }
            });
            // Now process the remaining rays
            for (int i = (rays.Length / packetSize) * packetSize; i < rays.Length; i++)
            {
                results[i] = Raycast(rays[i], near, far);
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
                var modelSpaceNormal = new Vector3(hit.NX, hit.NY, hit.NZ);
                var worldSpaceNormal = hit.Instance.NormalToWorldSpace(modelSpaceNormal);
                var position = ray.Position + ray.Direction * hit.Distance;
                var mesh = hit.Instance.Mesh;
                Vector2? uv = null;
                // If this mesh has uvs compute the uv coordinates as per documentation
                // https://embree.github.io/api.html
                if (mesh.HasUVs)
                {
                    float u = hit.U;
                    float v = hit.V;
                    var f = mesh.Faces[(int)hit.Primitive];
                    var t0 = mesh.Vertices[f.P0].UV;
                    var t1 = mesh.Vertices[f.P1].UV;
                    var t2 = mesh.Vertices[f.P2].UV;
                    uv = (1.0 - u - v) * t0 + u * t1 + v * t2;
                }
                return new HitData(position, worldSpaceNormal, uv, mesh, hit.Instance.Texture, hit.Distance);
            }
            return null;
        }

        /// <summary>
        /// Check to see if there is anything along the rays path within distance
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public bool Occludes(Ray ray, float distance)
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

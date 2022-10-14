using System;
using System.Collections.Generic;
using Embree;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;

namespace JPLOPS.RayTrace
{
    /// <summary>
    /// Class representing a model that can be raycasted against
    /// </summary>
    internal class Model : IInstance, IDisposable
    {
        private readonly Embree.Geometry geometry;
        private EmbreeMatrix transform;
        private Matrix inverseTranspose; // needed for normal correction 
        private readonly JPLOPS.Geometry.Mesh mesh;
        private readonly Image texture;
        
        /// <summary>
        /// Gets the wrapped Geometry collection.
        /// </summary>
        public Embree.Geometry Geometry { get { return geometry; } }

        /// <summary>
        /// Gets or sets whether this model is enabled.
        /// </summary>
        public Boolean Enabled { get; set; }

        /// <summary>
        /// Return the mesh associated with this model
        /// </summary>
        public JPLOPS.Geometry.Mesh Mesh { get { return mesh; } }

        /// <summary>
        /// Returns the texture associated with this model if there is one
        /// </summary>
        public Image Texture { get { return texture; } }

        /// <summary>
        /// Returns the tranform matrix of this object in the scene
        /// </summary>
        public IEmbreeMatrix Transform { get { return transform; } }

        /// <summary>
        /// Create a model for raycasting
        /// </summary>
        /// <param name="mesh">Mesh to use for raycast</param>
        /// <param name="texture">Optional texture argument, can be null</param>
        /// <param name="transform">Transform of this mesh in the scene</param>
        /// <param name="sceneFlags"></param>
        /// <param name="traversalFlags"></param>
        public Model(Device device, JPLOPS.Geometry.Mesh mesh, Image texture, Matrix transform, SceneFlags sceneFlags,
                     TraversalFlags traversalFlags)
        {
            this.Enabled = true;
            this.geometry = new Embree.Geometry(device, sceneFlags, traversalFlags);
            this.transform = new EmbreeMatrix(transform);
            this.inverseTranspose = Matrix.Transpose(Matrix.Invert(transform));
            this.mesh = mesh;
            this.texture = texture;

            // Add mesh to this models geometry
            List<int> indices = new List<int>(mesh.Faces.Count * 3);
            foreach (var f in mesh.Faces)
            {
                indices.Add(f.P0);
                indices.Add(f.P1);
                indices.Add(f.P2);
            }
            List<IEmbreePoint> points = new List<IEmbreePoint>(mesh.Vertices.Count);
            foreach (var v in mesh.Vertices)
            {
                points.Add(new EmbreeVector(v.Position));
            }
            TriangleMesh tm = new TriangleMesh(device, indices, points);
            geometry.Add(tm);
        }



        /// <summary>
        /// transforms an Embree.NET normal in object space to a world space normal vector.
        /// NOTE Embree.NET normal may not be normalized and might be zero length
        /// </summary>
        public Vector3 NormalToWorldSpace(Vector3 modelSpaceNormal)
        {
            return Vector3.TransformNormal(modelSpaceNormal, this.inverseTranspose);
        }

        ~Model()
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
                geometry.Dispose();
            }
        }
    }
}

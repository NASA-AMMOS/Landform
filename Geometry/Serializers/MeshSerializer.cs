using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Interface for a mesh serializer
    /// </summary>
    public abstract class MeshSerializer
    {
        public abstract string GetExtension();

        /// <summary>
        /// Save a mesh to disk
        /// </summary>
        /// <param name="m"></param>
        /// <param name="filename"></param>
        /// <param name="imageFilename"></param>
        public abstract void Save(Mesh m, string filename, string imageFilename);

        /// <summary>
        /// Load a mesh from disk
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public abstract Mesh Load(string filename);

        /// <summary>
        /// Register this serializer's extension with the MeshSerializers class
        /// </summary>
        public void Register()
        {
            MeshSerializers.Register(GetExtension(), this);
        }
    }
}

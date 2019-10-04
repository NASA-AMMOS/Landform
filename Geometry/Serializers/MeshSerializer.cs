using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;

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
        /// will return all the mesh level of details found in the file
        /// default implementation assumes LODs not supported and returns
        /// the normal load. serializers that do support this should 
        /// override this function with their specific implementations
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>finest LOD is expected as first element, descending qualities follow in order</returns>
        public virtual List<Mesh> LoadAllLODs(string filename)
        {
            return new List<Mesh> { Load(filename) };
        }

        /// <summary>
        /// Register this serializer's extension with the MeshSerializers class
        /// </summary>
        public void Register(MeshSerializers map)
        {
            map.Register(GetExtension(), this);
        }

        public void Register()
        {
            Register(MeshSerializers.Instance);
        }

        /// <summary>
        /// does a serializer support multiple levels of detail within a single file
        /// </summary>
        public virtual bool SupportsLODs()
        {
            return false;
        }
    }
}

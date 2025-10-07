using System.Collections.Generic;
using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Interface for a mesh serializer
    /// </summary>
    public abstract class MeshSerializer
    {
        public static ILogger Logger;

        public abstract string GetExtension();

        /// <summary>
        /// Save a mesh to disk
        /// </summary>
        public abstract void Save(Mesh m, string filename, string imageFilename);

        /// <summary>
        /// Load a mesh from disk
        /// </summary>
        public abstract Mesh Load(string filename);

        /// <summary>
        /// Load a mesh from disk including parsing any referenced texture filename
        /// </summary>
        public virtual Mesh Load(string filename, out string imageFilename, bool onlyGetImageFilename = false)
        {
            imageFilename = null;
            return onlyGetImageFilename ? null : Load(filename);
        }

        /// <summary>
        /// Load all the mesh level of details found in the file in order starting with finest
        /// </summary>
        public virtual List<Mesh> LoadAllLODs(string filename)
        {
            return new List<Mesh> { Load(filename) };
        }

        /// <summary>
        /// Load all the mesh level of details found in the file in order starting with finest
        /// include parsing any referenced texture filename, null if not implemented or not found in file
        /// </summary>
        public virtual List<Mesh> LoadAllLODs(string filename, out string imageFilename,
                                              bool onlyGetImageFilename = false)
        {
            imageFilename = null;
            return onlyGetImageFilename ? null : LoadAllLODs(filename);
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

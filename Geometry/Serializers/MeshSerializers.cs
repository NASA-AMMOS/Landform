using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Stores a mapping of supported mesh extensions to serializers
    /// </summary>
    class MeshSerializers
    {
        static Dictionary<string, MeshSerializer> serializers;

        static MeshSerializers()
        {
            serializers = new Dictionary<string, MeshSerializer>();
            new OBJSerializer().Register();
            new PLYSerializer().Register();
            new GLTFSerializer().Register();
        }

        /// <summary>
        /// Registers a handler for a file extension
        /// Extensions should be lower case and include the .
        /// </summary>
        /// <param name="ext"></param>
        /// <param name="serializer"></param>
        public static void Register(string ext, MeshSerializer serializer)
        {
            if(!serializers.ContainsKey(ext))
            {
                serializers.Add(ext, serializer);
            }
        }

        /// <summary>
        /// Gets a serializer for a given extension (including the .)
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public static MeshSerializer GetSerializer(string extension)
        {
            if(!serializers.ContainsKey(extension))
            {
                return null;
            }
            return serializers[extension];
        }


    }
}

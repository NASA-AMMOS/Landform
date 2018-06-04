using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    static class ImageSerializers
    {
        static Dictionary<string, ImageSerializer> serializers;

        static ImageSerializers()
        {
            serializers = new Dictionary<string, ImageSerializer>();
            new PDSSeralizer().Register();
            new DDSSerializer().Register();
            new GDALSeralizer().Register();
            new FITSSerializer().Register();
        }

        /// <summary>
        /// Registers a handler for a file extension
        /// Extensions should be lower case and include the .
        /// </summary>
        /// <param name="ext"></param>
        /// <param name="serializer"></param>
        public static void Register(string[] extensions, ImageSerializer serializer)
        {
            foreach (var ext in extensions)
            {
                var lowerExt = ext.ToLower();
                if (!serializers.ContainsKey(lowerExt))
                {
                    serializers.Add(lowerExt, serializer);
                }
            }
        }

        /// <summary>
        /// Gets a serializer for a given extension (including the .)
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public static ImageSerializer GetSerializer(string extension)
        {
            extension = extension.ToLower();
            if (!serializers.ContainsKey(extension))
            {
                return null;
            }
            return serializers[extension];
        }
    }
}

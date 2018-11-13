using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Util
{
    public abstract class SerializerMap<T>
    {
        private Dictionary<string, T> serializers = new Dictionary<string, T>();

        protected abstract void RegisterSerializers();

        public SerializerMap()
        {
            RegisterSerializers();
        }

        /// <summary>
        /// Registers a handler for a file extension
        /// Extensions should be lower case and include the .
        /// </summary>
        /// <param name="ext"></param>
        /// <param name="serializer"></param>
        public void Register(string ext, T serializer)
        {
            ext = ext.ToLower();
            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }
            if (!serializers.ContainsKey(ext))
            {
                serializers.Add(ext, serializer);
            }
        }

        public void Register(string[] exts, T serializer)
        {
            foreach (var ext in exts)
            {
                Register(ext, serializer);
            }
        }

        /// <summary>
        /// Gets a serializer for a given extension (including the .)
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public T GetSerializer(string ext)
        {
            ext = ext.ToLower();
            if (!serializers.ContainsKey(ext))
            {
                return default(T);
            }
            return serializers[ext];
        }

        public bool SupportsFormat(string format)
        {
            format = format.ToLower();
            return serializers.ContainsKey("." + format) || serializers.ContainsKey(format);
        }

        public string[] SupportedFormats()
        {
            string[] ret = serializers.Keys.ToArray();
            for (int i = 0; i < ret.Length; i++)
            {
                if (ret[i].StartsWith("."))
                {
                    ret[i] = ret[i].Substring(1);
                }
            }
            return ret;
        }
    }
}

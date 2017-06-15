using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public static class ImageManager
    {
        static Dictionary<ImageRef, Image> loaded = new Dictionary<ImageRef, Image>();
        static Dictionary<ImageRef, PDSMetadata> metadata = new Dictionary<ImageRef, PDSMetadata>();

        public static Image Get(ImageRef image)
        {
            if (!loaded.ContainsKey(image))
            {
                loaded[image] = Image.Load(image.path);
                if (metadata.ContainsKey(image)) metadata.Remove(image);
            }
            return loaded[image];
        }
        public static PDSMetadata GetPDSMetadata(ImageRef image)
        {
            if (loaded.ContainsKey(image)) return (PDSMetadata)loaded[image].Metadata;
            if (!metadata.ContainsKey(image)) metadata[image] = new PDSMetadata(image.path);
            return metadata[image];
        }
    }

}

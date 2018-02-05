using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Reference to an on-disk image by file path.
    /// </summary>
    public class DiskImageRef : ImageRef
    {
        public readonly string Path;
        public DiskImageRef(string path)
        {
            Path = path;
        }

        internal Image image;
        public override Image Image
        {
            get
            {
                if (image == null)
                {
                    image = Image.Load(Path);
                }
                return image;
            }
        }

        public override ImageMetadata Metadata
        {
            get
            {
                // TODO: Possible lazy loading of metadata
                return Image.Metadata;
            }
        }

        public override string DisplayName
        {
            get
            {
                return System.IO.Path.GetFileNameWithoutExtension(Path);
            }
        }

        /// <summary>
        /// Unload the image from memory.
        /// </summary>
        public void Unload()
        {
            image = null;
        }

        public override string ToString()
        {
            return Path;
        }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            else if (obj is string) return ((string)obj) == Path;
            else if (!(obj is DiskImageRef)) return false;
            return ((DiskImageRef)obj).Path == Path;
        }

        public override int GetHashCode()
        {
            return Path.GetHashCode();
        }
    }
}

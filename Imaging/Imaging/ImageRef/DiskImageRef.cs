using System;
using System.IO;
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
        private readonly int PathHashCode;
        private Image image;

        public DiskImageRef(string pathOrUrl)
        {
            if (pathOrUrl.ToLower().StartsWith("file://"))
            {
                Url = pathOrUrl;
                Path = pathOrUrl.Substring(7);
            }
            else if (!pathOrUrl.Contains("://"))
            {
                Url = "file://" + pathOrUrl;
                Path = pathOrUrl;
            }
            else
            {
                throw new Exception("unhandled protocol: " + pathOrUrl);
            }
            PathHashCode = Path.GetHashCode();
        }

        public override Image Load(IImageConverter converter = null)
        {
            if (image == null)
            {
                image = converter != null ? Image.Load(Path, converter) : Image.Load(Path);
            }
            return image;
        }

        public override string GetFile()
        {
            return Path;
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
            return PathHashCode;
        }
    }
}

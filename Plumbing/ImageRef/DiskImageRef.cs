using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    /// <summary>
    /// Reference to an on-disk image by file path.
    /// </summary>
    public class DiskImageRef : ImageRef
    {
        public readonly string Path;
        internal readonly int PathHashCode;
        public DiskImageRef(string path)
        {
            Url = "file://" + path;
            Path = path;
            PathHashCode = Path.GetHashCode();
        }

        internal Image image;
        public override Image Load(PipelineCore pipeline)
        {
            if (image == null)
            {
                image = Image.Load(Path);
            }
            return image;
        }

        public override Image Load(PipelineCore pipeline, IImageConverter imageConverter)
        {
            if (image == null)
            {
                image = Image.Load(Path, imageConverter);
            }
            return image;
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

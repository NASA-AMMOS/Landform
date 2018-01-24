using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Reference to a transient image in memory.
    /// </summary>
    public class TransientImageRef : ImageRef
    {
        public TransientImageRef(Image image)
        {
            this.image = image;
        }

        readonly Image image;
        public override Image Image
        {
            get
            {
                return image;
            }
        }

        public override ImageMetadata Metadata
        {
            get
            {
                return image.Metadata;
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ImageRef)) return false;
            return ((ImageRef)obj).Image == Image;
        }

        public override int GetHashCode()
        {
            return Image.GetHashCode();
        }
    }
}

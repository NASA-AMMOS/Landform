using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    /// <summary>
    /// Reference to a transient image in memory.
    /// </summary>
    public class TransientImageRef : ImageRef
    {
        private Image image;

        public TransientImageRef(Image image)
        {
            this.image = image;
        }

        public override Image Load(PipelineCore pipeline, IImageConverter converter = null)
        {
            return image;
        }

        public override string DisplayName
        {
            get
            {
                return "Transient-" + GetHashCode().ToString("X");
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is TransientImageRef)) return false;
            return ((TransientImageRef)obj).image == image;
        }

        public override int GetHashCode()
        {
            return image.GetHashCode();
        }
    }
}

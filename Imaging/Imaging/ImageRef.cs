using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public abstract class ImageRef
    {
        public abstract Image Image { get; }
        public abstract ImageMetadata Metadata { get; }
    }
}

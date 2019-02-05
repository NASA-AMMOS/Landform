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
        public string Url { get; protected set; }
        public abstract string DisplayName { get; }
        public abstract Image Load(IImageConverter imageConverter = null);
        public abstract string GetFile();
    }
}

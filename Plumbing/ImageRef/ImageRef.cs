using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public abstract class ImageRef
    {
        public string Url { get; protected set; }
        public abstract string DisplayName { get; }
        public abstract Image Load(PipelineCore pipeline);
        public abstract Image Load(PipelineCore pipeline, IImageConverter imageConverter);
    }
}

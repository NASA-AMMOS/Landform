using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class ImageRef
    {
        public string path;

        public Image Resolve()
        {
            return Image.Load(path);
        }

        public override bool Equals(object obj)
        {
            var ir = obj as ImageRef;
            if (ir == null) return false;
            return ir.path == path;
        }

        public override int GetHashCode()
        {
            return path.GetHashCode();
        }

        public override string ToString()
        {
            return path;
        }
    }
}

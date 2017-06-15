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
        public readonly string path;

        public ImageRef(string path)
        {
            this.path = path;
            this.image = null;
        }

        public ImageRef(Image image)
        {
            this.path = null;
            this.image = image;
        }

        Image image;
        public Image Image
        {
            get
            {
                if (image == null) Load();
                return image;
            }
        }
        public ImageMetadata Metadata
        {
            get
            {
                return Image.Metadata;
            }
        }

        public void Unload()
        {
            image = null;
        }
        
        private void Load(bool reload = false)
        {
            if (image != null && !reload) return;

            if (path == null)
            {
                throw new Exception("Attempt to load image with null path");
            }

            image = Image.Load(path);
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

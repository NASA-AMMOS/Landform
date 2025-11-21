using JPLOPS.Imaging;
using JPLOPS.Util;
using System.IO;

namespace JPLOPS.Pipeline
{
    public class PngDataProduct : DataProduct
    {
        public PngDataProduct() { }

        public PngDataProduct(Image image)
        {
            Image = image;
        }

        public Image Image;

        //TODO: add Image serialization APIs that read/write streams
        //for now use temp files

        public override void Deserialize(byte[] data)
        {
            TemporaryFile.GetAndDelete(".png", (fn) =>
            {
                File.WriteAllBytes(fn, data);
                Image = Image.Load(fn);
            });
        }

        public override byte[] Serialize()
        {
            byte[] res = null;
            TemporaryFile.GetAndDelete(".png", (fn) =>
            {
                Image.Save<byte>(fn);
                res = File.ReadAllBytes(fn);
            });
            return res;
        }
    }
}

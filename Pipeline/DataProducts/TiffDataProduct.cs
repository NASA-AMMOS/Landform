using JPLOPS.Imaging;
using JPLOPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPLOPS.Pipeline
{
    public class TiffDataProduct : DataProduct
    {
        public Image Image;
        public bool FloatingPoint;
        public bool Compress;

        public TiffDataProduct()
        {
            this.FloatingPoint = true;
            this.Compress = true;
        }

        public TiffDataProduct(bool floatingPoint, bool compress)
        {
            this.FloatingPoint = floatingPoint;
            this.Compress = compress;
        }

        public TiffDataProduct(Image image, bool floatingPoint = true, bool compress = true)
        {
            this.FloatingPoint = floatingPoint;
            this.Compress = compress;
            this.Image = image;
        }

        //TODO: add Image serialization APIs that read/write streams
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/392
        //for now use temp files

        public override void Deserialize(byte[] data)
        {
            TemporaryFile.GetAndDelete(".tif", (fn) =>
            {
                File.WriteAllBytes(fn, data);
                Image = Image.Load(fn);
            });
        }

        public override byte[] Serialize()
        {
            byte[] res = null;
            TemporaryFile.GetAndDelete(".tif", (fn) =>
            {
                var compressionType =
                Compress ? GDALTIFFWriteOptions.CompressionType.DEFLATE : GDALTIFFWriteOptions.CompressionType.NONE;
                var serializer = new GDALSerializer(new GDALTIFFWriteOptions(compressionType));
                if (FloatingPoint)
                {
                    serializer.Write<float>(fn, Image);
                }
                else
                {
                    serializer.Write<byte>(fn, Image);
                }
                res = File.ReadAllBytes(fn);
            });
            return res;
        }
    }
}

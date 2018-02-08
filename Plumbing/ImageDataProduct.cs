using OPS.Imaging;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public class ImageDataProduct : DataProduct
    {
        public string Extension { get; set; }
        public Type StorageType { get; set; }

        public ImageDataProduct(Image image, string extension, Type storageType)
        {
            Image = image;
            Extension = extension;
            StorageType = storageType;
        }

        public Image Image;
        public override void Deserialize(byte[] data)
        {
            TemporaryFile.GetAndDelete("tif", (fn) =>
            {
                File.WriteAllBytes(fn, data);
                Image = Image.Load(fn);
            });
        }

        public override byte[] Serialize()
        {
            byte[] res = null;
            TemporaryFile.GetAndDelete("tif", (fn) =>
            {
                if (StorageType == typeof(byte))
                {
                    Image.Save<byte>(fn);
                }
                else if (StorageType == typeof(short))
                {
                    Image.Save<short>(fn);
                }
                if (StorageType == typeof(sbyte))
                {
                    Image.Save<sbyte>(fn);
                }
                else if (StorageType == typeof(ushort))
                {
                    Image.Save<ushort>(fn);
                }
                else if (StorageType == typeof(float))
                {
                    Image.Save<float>(fn);
                }
                else if (StorageType == typeof(double))
                {
                    Image.Save<double>(fn);
                }
                else
                {
                    throw new NotImplementedException();
                }
                res = File.ReadAllBytes(fn);
            });
            return res;
        }
    }
}

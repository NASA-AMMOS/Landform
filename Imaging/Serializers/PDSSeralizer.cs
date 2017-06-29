using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Reads PDSImages.  
    /// </summary>
    public class PDSSeralizer : IImageSeralizer
    {

        /// <summary>
        /// Read a pds image
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="converter"></param>
        /// <param name="fillValue">
        /// If defined use these values to define the mask for the returned image.
        /// Array length must equal the number of bands in the input image.  Comparision is 
        /// done before pixel values are converted.
        /// </param>
        /// <returns></returns>
        public Image Read(string filename, IImageConverter converter, float[] fillValue = null)
        {
            PDSMetadata metadata = new PDSMetadata(filename);
            Image img = new Image(metadata.Bands, metadata.Width, metadata.Height);
            img.Metadata = metadata;
            img.CameraModel = metadata.CameraModel;

            if (fillValue != null)
            {
                if (fillValue.Length != img.Bands)
                {
                    throw new ImageSerializationException("Fill value length must match image bounds");
                }
            }

            using (FileStream fs = File.OpenRead(filename))
            {
                fs.Seek(metadata.DataOffset, SeekOrigin.Begin);

                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (metadata.SampleType == typeof(ushort))
                    {
                        for (int b = 0; b < img.Bands; b++)
                        {
                            for (int i = 0; i < img.Width * img.Height; i++)
                            {
                                ushort v = br.ReadUInt16();
                                v = ReverseBytes16(v);
                                img.Data[b][i] = v;
                            }
                        }
                        CreateMaskFromFillValues(img, fillValue);
                        return converter.Convert<ushort>(img);
                    }
                    else if (metadata.SampleType == typeof(float))
                    {
                        for (int b = 0; b < img.Bands; b++)
                        {
                            for (int i = 0; i < img.Width * img.Height; i++)
                            {
                                img.Data[b][i] = BitConverter.ToSingle(BitConverter.GetBytes(ReverseBytes32(br.ReadUInt32())), 0);
                            }
                        }
                        CreateMaskFromFillValues(img, fillValue);
                        return converter.Convert<float>(img);
                    }
                    else if (metadata.SampleType == typeof(byte))
                    {
                        // This check has been added because the navcam MXY rover mask files have a bit mask
                        // greater than the bit depth of 8 that they are in.  This is a bug in the format and has been reported to MIPL.
                        if (metadata.BitMask > byte.MaxValue)
                        {
                            metadata.BitMask = byte.MaxValue;
                        }
                        if (metadata.BitMask != byte.MaxValue)
                        {
                            throw new ImageSerializationException("PDS image unexpected bit mask");
                        }
                        for (int b = 0; b < img.Bands; b++)
                        {
                            for (int i = 0; i < img.Width * img.Height; i++)
                            {
                                img.Data[b][i] = br.ReadByte();
                            }
                        }
                        CreateMaskFromFillValues(img, fillValue);
                        return converter.Convert<byte>(img);
                    }
                    else
                    {
                        throw new ImageSerializationException("PDSImage sample type not supported");
                    }
                }
            }            

        }

        void CreateMaskFromFillValues(Image img, float[] fillValue)
        {
            if (fillValue != null)
            {
                if (fillValue.Length != img.Bands)
                {
                    throw new ImageSerializationException("Fill value length must match image bounds");
                }
                img.CreateMask(fillValue);
            }
        }

        /// <summary>
        /// Writing of pds images is not supported
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filename"></param>
        /// <param name="image"></param>
        /// <param name="converter"></param>
        /// <param name="fillValue"></param>
        public void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            throw new NotImplementedException();
        }
        
        public static uint ReverseBytes32(uint value)
        {
            return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 |
                   (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
        }

        public static ushort ReverseBytes16(ushort value)
        {
            return (ushort)((value & 0xFFU) << 8 | (value & 0xFF00U) >> 8);
        }
    }
}

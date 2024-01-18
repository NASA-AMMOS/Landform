using System;
using System.IO;
using System.Text;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// Reads PDSImages.  
    /// </summary>
    public class PDSSerializer : ImageSerializer
    {
        /// <summary>
        /// Hook to formulate the path to a PDS data file given path to a PDS LBL file and the DataPath from it.
        /// Default implementation returns lblPath if DataPath is null (monolithic IMG file)
        /// else appends DataPath to directory containing the LBL file.
        /// </summary>
        public static Func<string, string, string> DataPath =
            (lblPath, dataPath) => dataPath != null ? Path.Combine(Path.GetDirectoryName(lblPath), dataPath) : lblPath;

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
        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null, bool useFillValueFromFile = false)
        {
            if (useFillValueFromFile == true)
            {
                throw new NotImplementedException("add support for detecting file based invalid pixels");
            }

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

            string fileToRead = DataPath(filename, metadata.DataPath);

            using (FileStream fs = File.OpenRead(fileToRead))
            {
                fs.Seek(metadata.DataOffset, SeekOrigin.Begin);

                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (metadata.SampleType == typeof(ushort))
                    {
                        for (int b = 0; b < img.Bands; b++)
                        {
                            float[] bandData = img.GetBandData(b);
                            for (int i = 0; i < img.Width * img.Height; i++)
                            {
                                ushort v = br.ReadUInt16();
                                if (metadata.BigEndian)
                                {
                                    v = ReverseBytes16(v);
                                }
                                bandData[i] = v;
                            }
                        }
                        CreateMaskFromFillValues(img, fillValue);
                        return converter.Convert<ushort>(img);
                    }
                    else if (metadata.SampleType == typeof(short))
                    {
                        for (int b = 0; b < img.Bands; b++)
                        {
                            float[] bandData = img.GetBandData(b);
                            for (int i = 0; i < img.Width * img.Height; i++)
                            {
                                short v = br.ReadInt16();
                                if (metadata.BigEndian)
                                {
                                    v = ReverseBytes16(v);
                                }
                                bandData[i] = v;
                            }
                        }
                        CreateMaskFromFillValues(img, fillValue);
                        return converter.Convert<short>(img);
                    }
                    else if (metadata.SampleType == typeof(float))
                    {
                        for (int b = 0; b < img.Bands; b++)
                        {
                            float[] bandData = img.GetBandData(b);
                            for (int i = 0; i < img.Width * img.Height; i++)
                            {
                                UInt32 v = br.ReadUInt32();
                                if(metadata.BigEndian)
                                {
                                    v = ReverseBytes32(v);
                                }
                                bandData[i] = BitConverter.ToSingle(BitConverter.GetBytes(v), 0);
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
                            float[] bandData = img.GetBandData(b);
                            for (int i = 0; i < img.Width * img.Height; i++)
                            {
                                bandData[i] = br.ReadByte();
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
        /// Method for writing very basic PDSImages with minimal header information
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filename"></param>
        /// <param name="image"></param>
        /// <param name="converter"></param>
        /// <param name="fillValue"></param>
        public override void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            string template =
@"ODL_VERSION_ID = ODL3
RECORD_BYTES = {0}
^IMAGE = 2
OBJECT = IMAGE
INTERCHANGE_FORMAT = BINARY
LINES = {1}
LINE_SAMPLES = {2}
SAMPLE_TYPE = {3}
SAMPLE_BITS = {4}
BANDS = {5}
BAND_STORAGE_TYPE = BAND_SEQUENTIAL
END_OBJECT = IMAGE
END
";
            string type = null;
            int bits = 0;
            if (typeof(T) == typeof(byte))
            {
                type = "MSB_UNSIGNED_INTEGER";
                bits = 8;
            }
            else if (typeof(T) == typeof(ushort))
            {
                type = "MSB_UNSIGNED_INTEGER";
                bits = 16;
            }
            else if (typeof(T) == typeof(float))
            {
                type = "IEEE_REAL";
                bits = 32;
            }
            else
            {
                throw new ImageSerializationException("Unsuppprted type");
            }
            int headerSize = 2048;
            string header = string.Format(template, headerSize, image.Height, image.Width, type, bits, image.Bands);
            if (header.Length > headerSize)
            {
                throw new ImageSerializationException("Header larger than expected");
            }
            StringBuilder sb = new StringBuilder(header);
            while (sb.Length < headerSize)
            {
                sb.Append(" ");
            }
            header = sb.ToString();
            WriteRaw<T>(filename, header, image, converter, true, fillValue);
        }

        /// <summary>
        /// Write a file using the provided label string as the header
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filename"></param>
        /// <param name="label"></param>
        /// <param name="image"></param>
        /// <param name="converter"></param>
        /// <param name="bigEndian"></param>
        /// <param name="fillValue"></param>
        public static void WriteRaw<T>(string filename, string label, Image image, IImageConverter converter, bool bigEndian, float[] fillValue = null)
        {
            Image convertedImage = converter.Convert<T>(image);
            if (fillValue != null && convertedImage.HasMask)
            {
                convertedImage.SetValuesForMaskedData(fillValue);
            }
            using (FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                byte[] headerBytes = Encoding.ASCII.GetBytes(label);
                fs.Write(headerBytes, 0, headerBytes.Length);

                for (int b = 0; b < convertedImage.Bands; b++)
                {
                    for (int r = 0; r < convertedImage.Height; r++)
                    {
                        for (int c = 0; c < convertedImage.Width; c++)
                        {
                            if (typeof(T) == typeof(byte))
                            {
                                fs.WriteByte((byte)convertedImage[b, r, c]);

                            }
                            else if (typeof(T) == typeof(ushort))
                            {
                                ushort value = (ushort)convertedImage[b, r, c];
                                if (bigEndian)
                                {
                                    value = ReverseBytes16(value);
                                }
                                byte[] output = BitConverter.GetBytes(value);
                                fs.Write(output, 0, output.Length);
                            }
                            else if (typeof(T) == typeof(float))
                            {
                                UInt32 value = BitConverter.ToUInt32(BitConverter.GetBytes(convertedImage[b, r, c]), 0);
                                if (bigEndian)
                                {
                                    value = ReverseBytes32(value);
                                }
                                byte[] output = BitConverter.GetBytes(value);
                                fs.Write(output, 0, output.Length);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Read the label of a file as a string
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static string ReadLabel(string filename)
        {
            var metadata = new PDSMetadata(filename);
            return File.ReadAllText(filename).Substring(0, (int)metadata.DataOffset);
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

        public static short ReverseBytes16(short value)
        {
            return (short)((value & 0xFFU) << 8 | (value & 0xFF00U) >> 8);
        }

        public override string[] GetExtensions()
        {
            return new string[] { ".img", ".vic" , ".lbl"};
        }

        public override IImageConverter DefaultReadConverter()
        {
            return ImageConverters.PDSBitMaskValueRangeToNormalizedImage;
        }

        public override IImageConverter DefaultWriteConverter()
        {
            return ImageConverters.NormalizedImageToValueRange;
        }
    }
}

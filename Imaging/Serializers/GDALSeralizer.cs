using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using OSGeo.GDAL;
using System.IO;
using OPS.MathExtensions;

namespace OPS.Imaging
{    
    /// <summary>
    /// Reads all image types supported by GDAL
    /// </summary>
    public class GDALSeralizer : ImageSerializer
    {

        public GDALWriteOptions WriteOptions
        {
            get; set;
        }

        static object gdalLockObj = new object();
        static Dictionary<string, Tuple<string, bool>> extensionToGdalDriver;
        static Dictionary<Type, DataType> systemTypeToGdalType;

        static GDALSeralizer()
        {
            lock(gdalLockObj)
            {
                GdalConfiguration.ConfigureGdal();
                GdalConfiguration.ConfigureOgr();
                // Specify mapping from extension to gdal driver type
                // and whether or not the file needs to be written using
                // CreateCopy from memory.
                // Lost more file types available if built with gdal
                // http://www.gdal.org/formats_list.html
                extensionToGdalDriver = new Dictionary<string, Tuple<string, bool>>();
                extensionToGdalDriver.Add(".tif", new Tuple<string, bool>("GTIFF", false));
                extensionToGdalDriver.Add(".tiff", new Tuple<string, bool>("GTIFF", false));
                extensionToGdalDriver.Add(".jpg", new Tuple<string, bool>("JPEG", true));
                extensionToGdalDriver.Add(".bmp", new Tuple<string, bool>("BMP", true));
                extensionToGdalDriver.Add(".png", new Tuple<string, bool>("PNG", true));
                extensionToGdalDriver.Add(".jp2", new Tuple<string, bool>("JP2OpenJPEG", true));
                extensionToGdalDriver.Add(".j2k", new Tuple<string, bool>("JP2OpenJPEG", true));
                // Native to gdal type conversion
                systemTypeToGdalType = new Dictionary<Type, DataType>();
                systemTypeToGdalType.Add(typeof(byte), DataType.GDT_Byte);
                systemTypeToGdalType.Add(typeof(float), DataType.GDT_Float32);
                systemTypeToGdalType.Add(typeof(double), DataType.GDT_Float64);
                systemTypeToGdalType.Add(typeof(short), DataType.GDT_Int16);
                systemTypeToGdalType.Add(typeof(int), DataType.GDT_Int32);
                systemTypeToGdalType.Add(typeof(ushort), DataType.GDT_UInt16);
                systemTypeToGdalType.Add(typeof(uint), DataType.GDT_UInt32);
            }
        }

        public GDALSeralizer(GDALWriteOptions options = null)
        {
            if(options == null)
            {
                options = new GDALWriteOptions();
            }
            WriteOptions = options;
        }

        /// <summary>
        /// Read an image using the gdal library
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="converter">converts between values stored in the image file and the expected value space of the caller</param>
        /// <param name="fillValue">
        /// Specifies optional per-band values to be used to identify fill values.  If these are defined
        /// pixels matching these values will be marked as masked in the returned image.  Fill values
        /// represent the pre-converted values as they are stored in the image.
        /// </param>
        /// <returns></returns>
        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null)
        {            
            lock (gdalLockObj)
            {
                using (Dataset dataset = Gdal.Open(filename, Access.GA_ReadOnly))
                {
                    Image img = new Image(dataset.RasterCount, dataset.RasterXSize, dataset.RasterYSize);
                                          
                    for (int b = 0; b < img.Bands; b++)
                    {
                        using (Band band = dataset.GetRasterBand(b + 1))
                        {
                            object bandData = img.Data[b];
                            if (band.DataType == DataType.GDT_Byte)
                            {
                                byte[] buffer = new byte[img.Width * img.Height];
                                band.ReadRaster(0, 0, img.Width, img.Height, buffer, img.Width, img.Height, 0, 0);
                                for(int i = 0; i < buffer.Length; i++)
                                {
                                    img.Data[b][i] = buffer[i];
                                }
                            }
                            else if (band.DataType == DataType.GDT_Float32)
                            {
                                band.ReadRaster(0, 0, img.Width, img.Height, img.Data[b], img.Width, img.Height, 0, 0);
                            }
                            else if (band.DataType == DataType.GDT_Float64)
                            {
                                double[] buffer = new double[img.Width * img.Height];
                                band.ReadRaster(0, 0, img.Width, img.Height, buffer, img.Width, img.Height, 0, 0);
                                for (int i = 0; i < buffer.Length; i++)
                                {
                                    img.Data[b][i] = (float)buffer[i];
                                }
                            }
                            else if (band.DataType == DataType.GDT_Int16)
                            {
                                short[] buffer = new short[img.Width * img.Height];
                                band.ReadRaster(0, 0, img.Width, img.Height, buffer, img.Width, img.Height, 0, 0);
                                for (int i = 0; i < buffer.Length; i++)
                                {
                                    img.Data[b][i] = buffer[i];
                                }
                            }
                            else if (band.DataType == DataType.GDT_Int32 || band.DataType == DataType.GDT_UInt16 || band.DataType == DataType.GDT_UInt32)
                            {
                                int[] buffer = new int[img.Width * img.Height]; ;
                                band.ReadRaster(0, 0, img.Width, img.Height, buffer, img.Width, img.Height, 0, 0);
                                for (int i = 0; i < buffer.Length; i++)
                                {
                                    img.Data[b][i] = buffer[i];
                                }
                            }
                            else
                            {
                                throw new ImageSerializationException("Unsupported type in image file");
                            }
                        }
                    }
                    if(fillValue != null)
                    {
                        if(fillValue.Length != img.Bands)
                        {
                            throw new ImageSerializationException("Fill value length must match image bounds");
                        }
                        img.CreateMask(fillValue);
                    }
                    using (Band band = dataset.GetRasterBand(1))
                    {
                        if (band.DataType == DataType.GDT_Byte)
                        {
                            return converter.Convert<byte>(img);
                        }
                        else if (band.DataType == DataType.GDT_Float32 || band.DataType == DataType.GDT_Float64)
                        {
                            return converter.Convert<float>(img);
                        }
                        else if (band.DataType == DataType.GDT_Int16)
                        {
                            return converter.Convert<Int16>(img);
                        }
                        else if (band.DataType == DataType.GDT_Int32)
                        {
                            return converter.Convert<Int32>(img);
                        }
                        else if (band.DataType == DataType.GDT_UInt16)
                        {
                            return converter.Convert<UInt16>(img);
                        }
                        else if (band.DataType == DataType.GDT_UInt32)
                        {
                            return converter.Convert<UInt32>(img);
                        }
                        else
                        {
                            throw new ImageSerializationException("Unsupported type in image file");
                        }
                    }                    
                }
            }
        }

        /// <summary>
        /// Writes an image using the gdal library
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filename"></param>
        /// <param name="image"></param>
        /// <param name="converter">converts between values stored in the image file and the expected value space of the caller</param>
        /// <param name="fillValue">
        /// If specified (and if the image defines a mask) these values will be written anywhere that the image mask is true.
        /// These values are written out as is and are not modified by the converter.
        /// </param>
        public override void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            if (File.Exists(filename))
            {
                File.Delete(filename);
            }

            string fileExt = Path.GetExtension(filename).ToLower();
            if (!extensionToGdalDriver.ContainsKey(fileExt))
            {
                throw new ImageSerializationException("Unsupported file extension");
            }
            // Get the gdal driver settings for this extension
            Tuple<string, bool> driverSettings = extensionToGdalDriver[fileExt];
            if (driverSettings.Item1 == "JPEG" && image.Bands > 3)
            {
                // GDAL will try to write a 4 band image out to JPG, but the results are color shifted blech
                throw new ImageSerializationException("JPEG not supported with more than 3 bands");
            }
            if ((driverSettings.Item1 == "JPEG" || driverSettings.Item1 == "BMP") && typeof(T) != typeof(byte))
            {
                // Not sure if gdal JPEG only supports bytes 
                throw new ImageSerializationException("Image format only supportes byte type");
            }
            // Some file types don't support Create so we need to use CreateCopy instead
            // To do this we will first write the rasters to memory using the MEM driver
            string driverName = driverSettings.Item2 ? "MEM" : driverSettings.Item1;
            string[] driverOptions = driverSettings.Item2 ? null : WriteOptions.OptionString;
            Driver driver = Gdal.GetDriverByName(driverName);

            Image convertedImage = converter.Convert<T>(image);

            lock (gdalLockObj)
            {
                using (Dataset dataset = driver.Create(filename, convertedImage.Width, convertedImage.Height, convertedImage.Bands, systemTypeToGdalType[typeof(T)], driverOptions))
                {
                    if (fillValue != null && convertedImage.HasMask)
                    {
                        convertedImage.SetValuesForMaskedData(fillValue);                       
                    }
                    for (int b = 0; b < convertedImage.Bands; b++)
                    {
                        using (Band band = dataset.GetRasterBand(b + 1))
                        {
                            if (typeof(T) == typeof(byte))
                            {
                                byte[] buffer = new byte[convertedImage.Width*convertedImage.Height];
                                for (int i = 0; i < buffer.Length; i++)
                                {                                  
                                    buffer[i] = (byte)convertedImage.Data[b][i];
                                }
                                band.WriteRaster(0, 0, convertedImage.Width, convertedImage.Height, buffer, convertedImage.Width, convertedImage.Height, 0, 0);
                            }
                            else if (typeof(T) == typeof(float))
                            {
                                band.WriteRaster(0, 0, convertedImage.Width, convertedImage.Height, convertedImage.Data[b], convertedImage.Width, convertedImage.Height, 0, 0);
                            }
                            else if (typeof(T) == typeof(double))
                            {
                                double[] buffer = new double[convertedImage.Width * convertedImage.Height];
                                for (int i = 0; i < buffer.Length; i++)
                                {
                                    buffer[i] = (double)convertedImage.Data[b][i];
                                }
                                band.WriteRaster(0, 0, convertedImage.Width, convertedImage.Height, buffer, convertedImage.Width, convertedImage.Height, 0, 0);
                            }
                            else if (typeof(T) == typeof(short))
                            {
                                short[] buffer = new short[convertedImage.Width * convertedImage.Height];
                                for (int i = 0; i < buffer.Length; i++)
                                {
                                    buffer[i] = (short)convertedImage.Data[b][i];
                                }
                                band.WriteRaster(0, 0, convertedImage.Width, convertedImage.Height, buffer, convertedImage.Width, convertedImage.Height, 0, 0);
                            }
                            else if (typeof(T) == typeof(ushort))
                            {
                                int[] buffer = new int[convertedImage.Width * convertedImage.Height];
                                for (int i = 0; i < buffer.Length; i++)
                                {                                                                        
                                    buffer[i] = (int)convertedImage.Data[b][i];
                                }
                                band.WriteRaster(0, 0, convertedImage.Width, convertedImage.Height, buffer, convertedImage.Width, convertedImage.Height, 0, 0);
                            }
                            else if (typeof(T) == typeof(int))
                            {
                                int[] buffer = new int[convertedImage.Width * convertedImage.Height];
                                for (int i = 0; i < buffer.Length; i++)
                                {
                                    // We need to cast to long and then clamp to the int value range in this case becuase floating point precision 
                                    // can't distinguish between int.MaxValue and int.MaxValue+1 resulting in wrap arround errors
                                    buffer[i] = (int)MathExtensions.MathE.Clamp((long)convertedImage.Data[b][i], (long)int.MinValue, (long)int.MaxValue);
                                }
                                band.WriteRaster(0, 0, convertedImage.Width, convertedImage.Height, buffer, convertedImage.Width, convertedImage.Height, 0, 0);
                            }
                            // uint not supported 
                            else
                            {
                                throw new ImageSerializationException("Datatype not supported in image write");
                            }
                        }
                    }
                    // If we wrote this raster in memory first
                    if (driverSettings.Item2)
                    {
                        Driver actualDriver = Gdal.GetDriverByName(driverSettings.Item1);
                        using (Dataset actualDataset = actualDriver.CreateCopy(filename, dataset, 1, WriteOptions.OptionString, null, null))
                        {
                        }
                    }
                }

            }
        }


        public override string[] GetExtensions()
        {
            return extensionToGdalDriver.Keys.ToArray();
        }

        public override IImageConverter DefaultReadConverter()
        {
            return ImageConverters.ValueRangeToNormalizedImage;
        }

        public override IImageConverter DefaultWriteConverter()
        {
            return ImageConverters.NormalizedImageToValueRange;
        }
    }
}

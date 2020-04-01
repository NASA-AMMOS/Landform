using System;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public static class Colorize
    {
        /// <summary>
        /// converts the floating point values in the source image to colorized values. the previewBucketdistances
        /// are the boundaries for the colors in colorsLowToHigh. There should be one more color than distance to catch
        /// the distances that are larger than the final bucket cutoff
        /// </summary>
        /// <param name="colorCutoffValues">the floating point values that represent the upper bound of that color (eg. cutoffvalue 0.2, all values less than 0.2 get that value.</param>
        /// <param name="colorsLowToHigh">colors intended to be paired with colorCutoffValues. each color in R, G, B order, range 0 to 1. Should be 1 more color than cutoff values as the upper-end catchall color (greater than the last color cutoff value)</param>
        /// <param name="bgColor">color in R, G, B order, range 0 to 1</param>
        /// <returns>3 band colorized image</returns>
        public static Image ColorizeScalarImage(this Image img, float[] colorCutoffValues, float[][] colorsLowToHigh,
                                                float[] bgColor)
        {
            if (img.Bands != 1)
            {
                throw new InvalidDataException("expecting a single band image to be colorized");
            }

            Image result = img.Instantiate(3, img.Width, img.Height);
            if (img.HasMask)
            {
                result.CreateMask(true);
            }

            for (int idxRow = 0; idxRow < img.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < img.Width; idxCol++)
                {
                    if (img.HasMask && !img.IsValid(idxRow, idxCol))
                    {
                        result.SetBandValues(idxRow, idxCol, bgColor);
                        continue;
                    }

                    float val = img[0, idxRow, idxCol];
                    float[] color = colorsLowToHigh.Last(); //catchall for values > final cuttoff.
                    for (int idxColor = 0; idxColor < colorCutoffValues.Length; idxColor++)
                    {
                        if (val < colorCutoffValues[idxColor])
                        {
                            color = colorsLowToHigh[idxColor];
                            break;
                        }
                    }

                    result.SetBandValues(idxRow, idxCol, color);

                    if (img.HasMask)
                    {
                        result.SetMaskValue(idxRow, idxCol, false);
                    }
                }
            }

            return result;
        }

        public static float[] MonoToColor(float mono)
        {
            return new float[3] { mono, mono, mono };
        }

        public enum LuminanceMode { AVERAGE, MAX, ITU_BT709, RED, GREEN, BLUE };
        public const LuminanceMode DEF_LUMINANCE_MODE = LuminanceMode.ITU_BT709;

        public static float ColorToMono(float r, float g, float b, LuminanceMode mode = DEF_LUMINANCE_MODE)
        {
            switch (mode)
            {
                case LuminanceMode.AVERAGE: return (r + g + b) / 3;
                case LuminanceMode.MAX: return Math.Max(r, Math.Max(g,  b));
                case LuminanceMode.ITU_BT709: return  0.2126f * r + 0.7152f * g + 0.0722f * b;
                case LuminanceMode.RED: return r;
                case LuminanceMode.GREEN: return g;
                case LuminanceMode.BLUE: return b;
                default: throw new ArgumentException("unhandled mode: " + mode);
            }
        }

        /// <summary>
        /// bilinearly sample the image and return a 3 channel color
        /// srcpixel: col, row
        /// </summary>
        public static float[] SampleAsColor(this Image img, Vector2 srcPixel)
        {
            if (img.Bands == 3)
            {
                float[] samples = new float[3];
                for (int idxBand = 0; idxBand < img.Bands; idxBand++)
                {
                    samples[idxBand] = img.BilinearSample(idxBand, (float)srcPixel.Y, (float)srcPixel.X);
                }
                return samples;
            }
            else if (img.Bands == 1)
            {
                return MonoToColor(img.BilinearSample(0, (float)srcPixel.Y, (float)srcPixel.X));
            }
            else
            {
                throw new NotImplementedException("need 1 or 3 bands source to convert to 3 band color");
            }
        }

        /// <summary>
        /// bilinearly sample the image and return a single channel color
        /// </summary>
        public static float SampleAsMono(this Image img, Vector2 srcPixel, LuminanceMode mode = DEF_LUMINANCE_MODE)
        {
            if (img.Bands == 3)
            {
                float[] samples = new float[3];
                for (int idxBand = 0; idxBand < img.Bands; idxBand++)
                {
                    samples[idxBand] = img.BilinearSample(idxBand, (float)srcPixel.Y, (float)srcPixel.X);
                }
                return ColorToMono(samples[0], samples[1], samples[2], mode); //NOTE: implies RGB ordering
            }
            else if (img.Bands == 1)
            {
                return img.BilinearSample(0, (float)srcPixel.Y, (float)srcPixel.X);
            }
            else
            {
                throw new NotImplementedException("need 1 or 3 bands source to convert to mono");
            }
        }

        /// <summary>
        /// fill destination with samples from source texture (eg. replicate a single band to 3 if needed)
        /// </summary>
        public static void SetAsColor(this Image img, float[] samples, int destRow, int destCol)
        {
            if (img.Bands != 3)
            {
                throw new NotImplementedException("set as color requires a 3 band destination");
            }

            if (samples.Length == 3)
            {
                for (int idxBand = 0; idxBand < img.Bands; idxBand++)
                {
                    img[idxBand, destRow, destCol] = samples[idxBand];
                }
            }
            else if (samples.Length == 1)
            {
                for (int idxBand = 0; idxBand < img.Bands; idxBand++)
                {
                    img[idxBand, destRow, destCol] = samples[0];
                }
            }
            else
            {
                throw new NotImplementedException("need 1 or 3 bands to convert to 3 band color");
            }
        }

        /// <summary>
        /// fill destination with samples from source texture (eg. replicate a single band to 3 if needed)
        /// </summary>
        public static void SetAsMono(this Image img, float[] samples, int destRow, int destCol,
                                     LuminanceMode mode = DEF_LUMINANCE_MODE)
        {
            if (img.Bands != 1)
            {
                throw new NotImplementedException("set as mono requires a single band destination");
            }

            if (samples.Length == 3)
            {
                //implies RGB ordering on samples
                img[0, destRow, destCol] = ColorToMono(samples[0], samples[1], samples[2], mode);
            }
            else if (samples.Length == 1)
            {
                img[0, destRow, destCol] = samples[0];
            }
            else
            {
                throw new NotImplementedException("need 1 or 3 bands to convert to a single band color");
            }
        }
    }
}


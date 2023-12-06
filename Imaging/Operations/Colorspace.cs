using System;
using System.Collections.Generic;
using System.Linq;
using ColorMine.ColorSpaces;
using JPLOPS.Util;
using JPLOPS.MathExtensions;

namespace JPLOPS.Imaging
{
    public static class Colorspace
    {
        /// <summary>
        /// Convert an image in RGB color space to LAB color space.
        /// </summary>
        /// <param name="img">Image to convert. Must contain exactly three bands.</param>
        /// <returns>A new version of the image in LAB color space.</returns>
        public static Image RGBToLAB(this Image img, bool logLuminance = false)
        {
            if (img.Bands != 3)
            {
                throw new ArgumentException("RGB image must have 3 bands");
            }
            Image result = new Image(3, img.Width, img.Height);
            for (int r = 0; r < img.Height; ++r)
            {
                for (int c = 0; c < img.Width; ++c)
                {
                    Rgb rgb = new Rgb { R = 255 * img[0, r, c], G = 255 * img[1, r, c], B = 255 * img[2, r, c] };
                    Lab lab = rgb.To<Lab>();
                    result[0, r, c] = logLuminance ? (float)Math.Log(lab.L + 1) : (float)lab.L;
                    result[1, r, c] = (float)lab.A;
                    result[2, r, c] = (float)lab.B;
                }
            }
            return result;
        }

        /// <summary>
        /// Convert an image in LAB color space to RGB color space.
        /// </summary>
        /// <param name="img">Image to convert. Must contain exactly three bands.</param>
        /// <returns>A new version of the image in RGB color space.</returns>
        public static Image LABToRGB(this Image img, bool logLuminance = false)
        {
            if (img.Bands != 3)
            {
                throw new ArgumentException("LAB image must have 3 bands");
            }
            Image result = new Image(3, img.Width, img.Height);
            for (int r = 0; r < img.Height; ++r)
            {
                for (int c = 0; c < img.Width; ++c)
                {
                    float luminance = logLuminance ? (float)Math.Exp(img[0, r, c]) - 1 : img[0, r, c];
                    Lab lab = new Lab { L = luminance, A = img[1, r, c], B = img[2, r, c] };
                    Rgb rgb = lab.To<Rgb>(); 
                    result[0, r, c] = (float)MathE.Clamp01(rgb.R / 255);
                    result[1, r, c] = (float)MathE.Clamp01(rgb.G / 255);
                    result[2, r, c] = (float)MathE.Clamp01(rgb.B / 255);
                }
            }
            return result;
        }

        //luminance range: [0,100]
        //chrominance range : [-128,128]
        public static float[] RGBToLAB(float[] rgb, float range = 1)
        {
            Rgb c = new Rgb { R = 255 * (rgb[0] / range), G = 255 * (rgb[1] / range), B = 255 * (rgb[2] / range)};
            Lab lab = c.To<Lab>();
            return new float[3] { (float)lab.L, (float)lab.A, (float)lab.B };
        }

        public static float[] LABToRGB(float[] lab)
        {
            Lab c = new Lab { L = lab[0], A = lab[1], B = lab[2] };
            Rgb rgb = c.To<Rgb>();
            return new float[3] { (float)MathE.Clamp01(rgb.R / 255),
                                  (float)MathE.Clamp01(rgb.G / 255),
                                  (float)MathE.Clamp01(rgb.B / 255) };
        }

        /// <summary>
        /// Convert an image in RGB color space to HSV color space.
        /// </summary>
        /// <param name="img">Image to convert. Must contain exactly three bands.</param>
        /// <returns>A new version of the image in HSV color space.</returns>
        public static Image RGBToHSV(this Image img)
        {
            if (img.Bands != 3)
            {
                throw new ArgumentException("RGB image must have 3 bands");
            }
            Image result = new Image(3, img.Width, img.Height);
            for (int r = 0; r < img.Height; ++r)
            {
                for (int c = 0; c < img.Width; ++c)
                {
                    Rgb rgb = new Rgb { R = 255 * img[0, r, c], G = 255 * img[1, r, c], B = 255 * img[2, r, c] };
                    Hsv hsv = rgb.To<Hsv>();
                    result[0, r, c] = (float)hsv.H;
                    result[1, r, c] = (float)hsv.S;
                    result[2, r, c] = (float)hsv.V;
                }
            }
            return result;
        }

        /// <summary>
        /// Convert an image in HSV color space to RGB color space.
        /// </summary>
        /// <param name="img">Image to convert. Must contain exactly three bands.</param>
        /// <returns>A new version of the image in RGB color space.</returns>
        public static Image HSVToRGB(this Image img)
        {
            if (img.Bands != 3)
            {
                throw new ArgumentException("HSV image must have 3 bands");
            }
            Image result = new Image(3, img.Width, img.Height);
            for (int r = 0; r < img.Height; ++r)
            {
                for (int c = 0; c < img.Width; ++c)
                {
                    Hsv hsv = new Hsv { H = img[0, r, c], S = img[1, r, c], V = img[2, r, c] };
                    Rgb rgb = hsv.To<Rgb>();
                    result[0, r, c] = (float)MathE.Clamp01(rgb.R / 255);
                    result[1, r, c] = (float)MathE.Clamp01(rgb.G / 255);
                    result[2, r, c] = (float)MathE.Clamp01(rgb.B / 255);
                }
            }
            return result;
        }

        //hue range: [0,360]
        //saturation range: [0,1]
        //value range: [0,1]
        public static float[] RGBToHSV(float[] rgb, float range = 1)
        {
            Rgb c = new Rgb { R = 255 * (rgb[0] / range), G = 255 * (rgb[1] / range), B = 255 * (rgb[2] / range)};
            Hsv hsv = c.To<Hsv>();
            return new float[3] { (float)hsv.H, (float)hsv.S, (float)hsv.V };
        }

        public static float[] HSVToRGB(float[] hsv)
        {
            Hsv c = new Hsv { H = hsv[0], S = hsv[1], V = hsv[2] };
            Rgb rgb = c.To<Rgb>();
            return new float[3] { (float)MathE.Clamp01(rgb.R / 255),
                                  (float)MathE.Clamp01(rgb.G / 255),
                                  (float)MathE.Clamp01(rgb.B / 255) };
        }

        //http://entropymine.com/imageworsener/srgbformula
        public static float ApplySRGBGamma(float l)
        {
            return (float)MathE.Clamp01(l < 0.0031308 ? (l * 12.92) : (1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055));
        }

        //this works on any number of bands
        public static float[] LinearRGBToSRGB(float[] lrgb)
        {
            float[] srgb = new float[lrgb.Length];
            for (int i = 0; i < lrgb.Length; i++)
            {
                srgb[i] = ApplySRGBGamma(lrgb[i]);
            }
            return srgb;
        }

        //http://entropymine.com/imageworsener/srgbformula
        public static float UnapplySRGBGamma(float s)
        {
            return (float)MathE.Clamp01(s < 0.04045 ? (s / 12.92) : Math.Pow((s + 0.055) / 1.055, 2.4));
        }

        //this works on any number of bands
        public static float[] SRGBToLinearRGB(float[] srgb)
        {
            float[] lrgb = new float[srgb.Length];
            for (int i = 0; i < srgb.Length; i++)
            {
                lrgb[i] = UnapplySRGBGamma(srgb[i]);
            }
            return lrgb;
        }

        //this works on any number of bands
        public static Image LinearRGBToSRGB(this Image img)
        {
            Image result = new Image(img.Bands, img.Width, img.Height);
            for (int b = 0; b < img.Bands; b++)
            {
                for (int r = 0; r < img.Height; ++r)
                {
                    for (int c = 0; c < img.Width; ++c)
                    {
                        result[b, r, c] = ApplySRGBGamma(img[b, r, c]);
                    }
                }
            }
            return result;
        }

        //this works on any number of bands
        public static Image SRGBToLinearRGB(this Image img)
        {
            Image result = new Image(img.Bands, img.Width, img.Height);
            for (int b = 0; b < img.Bands; b++)
            {
                for (int r = 0; r < img.Height; ++r)
                {
                    for (int c = 0; c < img.Width; ++c)
                    {
                        result[b, r, c] = UnapplySRGBGamma(img[b, r, c]);
                    }
                }
            }
            return result;
        }

        public static double GetLuminanceRange()
        {
            return (new Rgb() { R = 255, G = 255, B = 255 }).To<Lab>().L; //typically defined as 100
        }

        public static bool DefaultHueFilter(double s, double v)
        {
            return s > 0.3 && v > 0.3;
        }

        public static int ComputeStats(this Image img, out double luminanceMin, out double luminanceMax,
                                       out double luminanceAvg, out double luminanceSTD, out double luminanceMed,
                                       out double luminanceMAD, out double hueAvg, out double hueMed,
                                       bool useMask = true, Func<double, double, bool> hueFilter = null)
        {
            hueFilter = hueFilter ?? DefaultHueFilter;

            double luminanceRange = GetLuminanceRange();

            var lumas = new List<double>();
            var hues = new List<double>();
            for (int r = 0; r < img.Height; ++r)
            {
                for (int c = 0; c < img.Width; ++c)
                {
                    if (useMask && !img.IsValid(r, c))
                    {
                        continue;
                    }
                    if (img.Bands > 2)
                    {
                        Rgb rgb = new Rgb { R = 255 * img[0, r, c], G = 255 * img[1, r, c], B = 255 * img[2, r, c] };
                        lumas.Add(rgb.To<Lab>().L);
                        var hsv = rgb.To<Hsv>();
                        if (hueFilter(hsv.S, hsv.V))
                        {
                            hues.Add(hsv.H);
                        }
                    }
                    else
                    {
                        lumas.Add(img[0, r, c] * luminanceRange); //[0,1] => [0,100]
                    }
                }
            }

            luminanceMin = double.PositiveInfinity;
            luminanceMax = double.NegativeInfinity;
            luminanceAvg = luminanceSTD = luminanceMed = luminanceMAD = -1;
            if (lumas.Count > 0)
            {
                lumas.Sort();
                luminanceMed = lumas[lumas.Count / 2];
                luminanceAvg = lumas.Sum() / lumas.Count;
                luminanceSTD = luminanceMAD = 0;
                var absDiffs = new List<double>();
                foreach (var luminance in lumas)
                {
                    luminanceMin = Math.Min(luminance, luminanceMin);
                    luminanceMax = Math.Max(luminance, luminanceMax);
                    luminanceSTD += (luminance - luminanceAvg) * (luminance - luminanceAvg);
                    absDiffs.Add(Math.Abs(luminance - luminanceMed));
                }
                luminanceSTD = Math.Sqrt(luminanceSTD / lumas.Count);
                absDiffs.Sort();
                luminanceMAD = absDiffs[absDiffs.Count / 2];
            }

            hueAvg = hueMed = -1;
            if (hues.Count > 0)
            {
                hues.Sort();
                hueMed = hues[hues.Count / 2];
                hueAvg = hues.Sum() / hues.Count;
            }

            return lumas.Count;
        }

        public static void DumpStats(this Image img, Action<string> info, bool useMask = true)
        {
            int numValid = img.ComputeStats(out double luminanceMin, out double luminanceMax,
                                            out double luminanceAvg, out double luminanceSTD, out double luminanceMed,
                                            out double luminanceMAD, out double hueAvg, out double hueMed, useMask);
            int np = img.Width * img.Height;
            info($"{img.Bands} bands, {img.Width}x{img.Height}, {Fmt.KMG(numValid)}/{Fmt.KMG(np)} valid pixels");
            info($"min L(AB) luminance {luminanceMin:f3}, max {luminanceMax:f3}, avg {luminanceAvg:f3}, " +
                 $"std {luminanceSTD:f3}, median {luminanceMed:f3}, MAD {luminanceMAD:f3}");
            info($"average H(SV) hue {hueAvg:f3}, median {hueMed:f3}");
        }

        public static Image AdjustLuminance(this Image img, Func<double, double> adjust, bool isRGBOrMono = true)
        {
            double luminanceRange = GetLuminanceRange();
            for (int r = 0; r < img.Height; ++r)
            {
                for (int c = 0; c < img.Width; ++c)
                {
                    if (img.Bands > 2 && isRGBOrMono)
                    {
                        Rgb rgb = new Rgb { R = 255 * img[0, r, c], G = 255 * img[1, r, c], B = 255 * img[2, r, c] };
                        Lab lab = rgb.To<Lab>();
                        lab.L = MathE.Clamp(adjust(lab.L), 0, luminanceRange);
                        Rgb rgbAdj = lab.To<Rgb>(); 
                        img[0, r, c] = (float)MathE.Clamp01(rgbAdj.R / 255);
                        img[1, r, c] = (float)MathE.Clamp01(rgbAdj.G / 255);
                        img[2, r, c] = (float)MathE.Clamp01(rgbAdj.B / 255);
                    }
                    else if (isRGBOrMono) //single band mono
                    {
                        img[0, r, c] = (float)MathE.Clamp01(adjust(img[0, r, c] * luminanceRange) / luminanceRange);
                    }
                    else //already LAB (typically 3 but maybe just 1 band)
                    {
                        img[0, r, c] = (float)MathE.Clamp(adjust(img[0, r, c]), 0, luminanceRange);
                    }
                }
            }
            return img;
        }

        public static Image AdjustLuminance(this Image img, double adjust, bool isRGBOrMono = true)
        {
            return adjust != 0 ? img.AdjustLuminance(l => l + adjust, isRGBOrMono) : img;
        }

        public static Image AdjustLuminanceRange(this Image img, double fromMin, double fromMax,
                                                 double toMin, double toMax, double weight = 1, bool isRGBOrMono = true)
        {
            weight = MathE.Clamp01(weight);
            toMin = MathE.Lerp(fromMin, toMin, weight);
            toMax = MathE.Lerp(fromMax, toMax, weight);
            double fromRange = fromMax - fromMin;
            double toRange = toMax - toMin;
            fromRange = Math.Abs(fromRange) > MathE.EPSILON ? fromRange : 1;
            Func<double, double> adjust = l => toMin + ((l - fromMin) / fromRange) * toRange;
            return weight > 0 ? img.AdjustLuminance(adjust, isRGBOrMono) : img;
        }

        public static Image AdjustLuminanceDistribution(this Image img, double fromMedian, double fromMAD,
                                                        double toMedian, double toMAD, double weight = 1,
                                                        bool isRGBOrMono = true)
        {
            weight = MathE.Clamp01(weight);
            toMedian = MathE.Lerp(fromMedian, toMedian, weight);
            toMAD = MathE.Lerp(fromMAD, toMAD, weight);
            fromMAD = Math.Abs(fromMAD) > MathE.EPSILON ? fromMAD : 1;
            Func<double, double> adjust = l => toMedian + ((l - fromMedian) / fromMAD) * toMAD;
            return weight > 0 ? img.AdjustLuminance(adjust, isRGBOrMono) : img;
        }

        public static float[][] RandomHues(int num, float saturation = 1, float value = 1)
        {
            var ret = new float[num][];
            for (int i = 0; i < num; i++)
            {
                float hue = i * 360.0f / num;
                ret[i] = HSVToRGB(new float[] { hue, saturation, value });
            }
            return NumberHelper.Shuffle(ret);
        }
    }
}


using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using ColorMine.ColorSpaces;

namespace OPS.Imaging
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
                    result[0, r, c] = (float)(Math.Max(Math.Min(255, rgb.R), 0) / 255);
                    result[1, r, c] = (float)(Math.Max(Math.Min(255, rgb.G), 0) / 255);
                    result[2, r, c] = (float)(Math.Max(Math.Min(255, rgb.B), 0) / 255);
                }
            }
            return result;
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
                    result[0, r, c] = (float)(Math.Max(Math.Min(255, rgb.R), 0) / 255);
                    result[1, r, c] = (float)(Math.Max(Math.Min(255, rgb.G), 0) / 255);
                    result[2, r, c] = (float)(Math.Max(Math.Min(255, rgb.B), 0) / 255);
                }
            }
            return result;
        }
    }
}


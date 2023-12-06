using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.ImageFeatures
{
    public class PCAUtil
    {
        /// <summary>
        /// Normalizes a vector.
        /// </summary>
        /// <param name="vector">Normalized vector.</param>
        public static float[] NormalizeVector(float[] vector)
        {
            float total = 0;
            float[] res = new float[vector.Length];

            for (int i = 0; i < vector.Length; i++)
            {
                total += Math.Abs(vector[i]);
            }

            if (total == 0)
            {
                return vector;
            }

            total /= vector.Length;

            for (int i = 0; i < vector.Length; i++)
            {
                res[i] = vector[i] / total;// / 100f; // change constant value ?????
            }

            return res;
        }

        /// <summary>
        /// Calculates list of gradients from a list of keypoints.
        /// </summary>
        /// <param name="keypoints">Input keypoints.</param>
        /// <returns>List of concatenated horizontal and vertical gradients.</returns>`
        public static float[][] GetGradients(List<PCASIFTFeature> keypoints)
        {
            float[][] result = new float[keypoints.Count()][];

            for (int i = 0; i < keypoints.Count(); i++)
            {
                int patchsize = keypoints[i].Patch.Width;
                int gsize = (patchsize - 2) * (patchsize - 2) * 2;
                float[] vec = new float[gsize];
                int count = 0;
                float x1, x2, y1, y2, gx, gy;
                PCASIFTFeature key = keypoints[i];

                float[,,] data = key.Patch.Data;
                for (int y = 1; y < patchsize - 1; y++)
                {
                    for (int x = 1; x < patchsize - 1; x++)
                    {
                        x1 = data[y, x + 1, 0];
                        x2 = data[y, x - 1, 0];
                        y1 = data[y + 1, x, 0];
                        y2 = data[y - 1, x, 0];

                        gx = x1 - x2;
                        gy = y1 - y2;

                        vec[count] = gx;
                        vec[count + 1] = gy;

                        count += 2;
                    }
                }
                vec = NormalizeVector(vec);
                result[i] = vec;
            }

            return result;
        }

        /// <summary>
        /// Given an image and pixel location, calculates approximate intensity using bilinear interpolation.
        /// </summary>
        /// <param name="image"></param>
        /// <param name="col"></param>
        /// <param name="row"></param>
        /// <returns></returns>
        public static float GetPixelBilinearInterpolation(float[,,] data, double col, double row, int height, int width)
        {
            int irow, icol;
            float rfrac, cfrac;
            float row1 = 0, row2 = 0;

            irow = (int)row;
            icol = (int)col;

            if (irow < 0 || irow >= height || icol < 0 || icol >= width) { return 0; }

            row = Math.Min(row, height - 1);
            col = Math.Min(col, width - 1);

            rfrac = (float)(1.0 - (row - irow));
            cfrac = (float)(1.0 - (col - icol));

            if (cfrac < 1)
            {
                row1 = cfrac * data[irow, icol, 0] + (1.0f - cfrac) * data[irow, icol + 1, 0];
            }
            else
            {
                row1 = data[irow, icol, 0];
            }

            if (rfrac < 1)
            {
                if (cfrac < 1)
                {
                    row2 = cfrac * data[irow + 1, icol, 0] + (1.0f - cfrac) * data[irow + 1, icol + 1, 0];
                }
                else
                {
                    row2 = data[irow + 1, icol, 0];
                }
            }
            return rfrac * row1 + (1f - rfrac) * row2;
        }

        /// <summary>
        /// Scales and blurs input image to make base image for Gaussian pyramid.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <returns>Base image for Gaussian pyramid.</returns>
        public static Image<Gray, float> ScaleInitImage(Image<Gray, float> image)
        {
            Image<Gray, float> dst;
            Image<Gray, float> img = image.Clone().Resize(2, Inter.Area);
            dst = new Image<Gray, float>(img.Width, img.Height);
            float sigma = (float)Math.Sqrt(PCAConstants.SIGMA * PCAConstants.SIGMA - 4 * PCAConstants.INIT_SIGMA * PCAConstants.INIT_SIGMA);
            int kernelDim = (int)Math.Max(3, 2 * 4 * sigma + 1f);
            kernelDim = kernelDim % 2 == 0 ? kernelDim + 1 : kernelDim;
            return img.SmoothGaussian(kernelDim, kernelDim, PCAConstants.SIGMA, PCAConstants.SIGMA);
        }

        /// <summary>
        /// Computes a Gaussian pyramid for a specific octave.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <returns>List of scales for the octave.</returns>
        public static List<Image<Gray, float>> BuildGaussianScales(Image<Gray, float> image)
        {
            List<Image<Gray, float>> GScales = new List<Image<Gray, float>>();
            double k = Math.Pow(2, 1.0 / ((float)PCAConstants.SCALES_PER_OCTAVE));

            GScales.Add(image.Clone());

            for (int i = 1; i < PCAConstants.SCALES_PER_OCTAVE + 3; i++)
            {
                Image<Gray, float> dst = new Image<Gray, float>(image.Width, image.Height);

                double sigma1 = Math.Pow(k, i - 1) * PCAConstants.SIGMA;
                double sigma2 = Math.Pow(k, i) * PCAConstants.SIGMA;
                double sigma = Math.Sqrt(sigma2 * sigma2 - sigma1 * sigma1);
                int kernelDim = (int)Math.Max(3, 2 * 4 * sigma + 1f);

                kernelDim = kernelDim % 2 == 0 ? kernelDim + 1 : kernelDim;
                dst = GScales[GScales.Count - 1].SmoothGaussian(kernelDim, kernelDim, sigma1, sigma2);
                GScales.Add(dst);
            }
            return GScales;
        }

        /// <summary>
        /// Computes a Gaussian pyramid for a specific image.
        /// </summary>
        /// <param name="image"></param>
        /// <returns>List of scales for each octave, as a list.</returns>
        public static List<List<Image<Gray, float>>> BuildGaussianOctaves(Image<Gray, float> image) // not void, find right type
        {
            List<List<Image<Gray, float>>> octaves = new List<List<Image<Gray, float>>>();
            int dim = Math.Min(image.Height, image.Width);
            int numoctaves = (int)(Math.Log(dim) / Math.Log(2.0)) - 2;// ????????
            if (dim < 1000) numoctaves += 1;

            numoctaves = Math.Min(numoctaves, PCAConstants.MAX_OCTAVES);

            Image<Gray, float> imageCopy = image.Clone();

            for (int i = 0; i < numoctaves; i++)
            {
                // Build Gaussian scales
                List<Image<Gray, float>> scales = BuildGaussianScales(imageCopy);
                octaves.Add(scales);

                // Halve the image 
                Image<Gray, float> halvedImageCopy = scales[PCAConstants.SCALES_PER_OCTAVE].Clone().Resize(0.5, Inter.Area);
                imageCopy = halvedImageCopy;
            }

            return octaves;
        }

        /// <summary>
        /// Updates the fields of given keypoints such that patches may be computed.
        /// </summary>
        /// <param name="keypoints">Input keypoints.</param>
        public static void UpdateKeypoints(IEnumerable<PCASIFTFeature> keypoints)
        {
            float log2 = (float)Math.Log(2);
            foreach (var k in keypoints)
            {
                double tmp = Math.Log((double)k.GScale / PCAConstants.SIGMA) / log2 + 1.0;
                k.Octave = (int)tmp;
                k.FScale = (float)((tmp - k.Octave) * PCAConstants.SCALES_PER_OCTAVE);
                k.IScale = (int)Math.Round(k.FScale);

                if (k.IScale == 0 && k.Octave > 0)
                {
                    k.IScale = PCAConstants.SCALES_PER_OCTAVE;
                    k.Octave -= 1;
                    k.FScale += PCAConstants.SCALES_PER_OCTAVE;
                }

                k.SX = (float)(k.Location.X / Math.Pow(2.0, k.Octave));
                k.SY = (float)(k.Location.Y / Math.Pow(2.0, k.Octave));

                //k.Location.X *= 2;
                //k.Location.Y *= 2; // This doesn't need to change.
                k.SX *= 2;
                k.SY *= 2;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Microsoft.Xna.Framework;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Util;
using OPS.Imaging;
using OPS.Imaging.Emgu;

namespace OPS.Alignment
{
    // As described in ASIFT: A NEW FRAMEWORK FOR FULLY AFFINE INVARIANT IMAGE COMPARISON
    // http://www.cmap.polytechnique.fr/~yu/publications/ASIFT_SIIMS_final.pdf

    public class ASIFTDetector : IFeatureDetector
    {
        public ASIFTDetector(int maxSimulatedDimension=512)
        {
            this.MaxSimulatedDimension = maxSimulatedDimension;
        }
        public int MaxSimulatedDimension;

        public IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            var emguImg = image.ToEmguGrayscale();
            var emguMask = (mask != null) ? mask.ToEmguGrayscale() : null;
            foreach (var feat in Detect(emguImg, emguMask))
            {
                yield return feat;
            }
        }

        /// <summary>
        /// Apply a simulated affine deformation to an input image with mask.
        /// </summary>
        /// <param name="tilt">Simulated camera tilt</param>
        /// <param name="phi">Simulated camera roll</param>
        /// <param name="image">Image to deform</param>
        /// <param name="mask">Mask with 0 for invalid pixels</param>
        /// <param name="outImage">Output image</param>
        /// <param name="outMask">Output mask image</param>
        /// <param name="A">Will be filled with the affine matrix used</param>
        static void AffineSkew(double tilt, double phi, Image<Gray, byte> image, Image<Gray, byte> mask, out Image<Gray, byte> outImage, out Image<Gray, byte> outMask, out Matrix<float> A)
        {
            int width = image.Width;
            int height = image.Height;
            if (mask == null)
            {
                mask = new Image<Gray, byte>(width, height);
                mask.SetValue(255);
            }

            A = new Matrix<float>(new float[,] { { 1, 0, 0 }, { 0, 1, 0 } });
            bool anyChange = false;
            if (Math.Abs(phi) > 1e-6)
            {
                anyChange = true;
                double sin = Math.Sin(phi),
                       cos = Math.Cos(phi);
                Vector2[] corners = new Vector2[] { new Vector2(0, 0), new Vector2(width, 0), new Vector2(width, height), new Vector2(0, height) };
                Vector2[] newCorners = corners.Select(c => Rotate(c, phi)).ToArray();
                double x0 = newCorners.Select(c => c.X).Min(),
                       y0 = newCorners.Select(c => c.Y).Min(),
                       x1 = newCorners.Select(c => c.X).Max(),
                       y1 = newCorners.Select(c => c.Y).Max();
                width = (int)Math.Ceiling(x1 - x0);
                height = (int)Math.Ceiling(y1 - y0);
                A = new Matrix<float>(new float[,] { { (float)cos, (float)-sin, (float)-x0 }, { (float)sin, (float)cos, (float)-y0 } });
                image = image.WarpAffine(A.Mat, width, height, Inter.Linear, Warp.Default, BorderType.Constant, new Gray(0));
            }
            if (Math.Abs(tilt) > 1e-6)
            {
                anyChange = true;
                double sigma = 0.8 * Math.Sqrt(tilt * tilt - 1);
                image = image.SmoothGaussian(0, 0, sigma, 0.01);
                image = image.Resize((int)(width / tilt), height, Inter.Nearest, false);
                A[0, 0] /= (float)tilt;
                A[0, 1] /= (float)tilt;
                A[0, 2] /= (float)tilt;
            }
            if (anyChange)
            {
                width = image.Width;
                height = image.Height;
                mask = mask.WarpAffine(A.Mat, width, height, Inter.Nearest, Warp.Default, BorderType.Constant, new Gray(0));
            }
            outImage = image;
            outMask = mask;
        }
        
        /// <summary>
        /// Detect raw SIFT features in an image.
        /// </summary>
        /// <param name="image">Input image</param>
        /// <param name="mask">Input mask</param>
        /// <param name="numFeatures">Maximum number of features to return</param>
        /// <param name="numOctaves">Number of SIFT octaves</param>
        /// <returns></returns>
        static IEnumerable<SIFTFeature> DetectSIFT(Image<Gray, byte> image, Image<Gray, byte> mask, int numFeatures = 1000, int numOctaves = 4)
        {
            using (Emgu.CV.XFeatures2D.SIFT sift = new Emgu.CV.XFeatures2D.SIFT(numFeatures, numOctaves))
            {
                MKeyPoint[] keypoints = sift.Detect(image, mask);
                if (keypoints.Length < 3) yield break;

                using (Matrix<float> descriptors = new Matrix<float>(keypoints.Length, 128))
                {
                    sift.Compute(image, new VectorOfKeyPoint(keypoints), descriptors);

                    int i;
                    for (i = 0; i < keypoints.Length; i++)
                    {
                        float[] desc = new float[128];
                        for (int j = 0; j < 128; j++)
                        {
                            desc[j] = descriptors[i, j];
                        }
                        yield return new SIFTFeature(
                            new Vector2(keypoints[i].Point.X, keypoints[i].Point.Y),
                            keypoints[i].Size,
                            keypoints[i].Angle,
                            keypoints[i].Octave,
                            keypoints[i].Response,
                            new SIFTDescriptor(desc)
                           );
                    }
                }
            }
        }

        /// <summary>
        /// Detect ASIFT features in an image.
        /// </summary>
        /// <param name="image">Input image</param>
        /// <param name="mask">Input mask, with zero signifying invalid pixels and</param>
        /// <returns>Enumerable of all detected features</returns>
        public IEnumerable<ImageFeature> Detect(Image<Gray, byte> image, Image<Gray, byte> mask)
        {
            double scale = 1;

            foreach (SIFTFeature feat in DetectSIFT(image, mask))
            {
                yield return new SIFTFeature(
                    feat.Location / scale,
                    feat.Size / scale,
                    feat.Angle,
                    feat.Octave,
                    feat.Response,
                    feat.Descriptor
                    );
            }

            if (MaxSimulatedDimension > 0 && image.Width > MaxSimulatedDimension || image.Height > MaxSimulatedDimension)
            {
                scale = ((double)MaxSimulatedDimension) / Math.Max(image.Width, image.Height);
                image = image.Resize(scale, Inter.Lanczos4);
                if (mask != null)
                {
                    mask = mask.Resize(scale, Inter.Nearest);
                }
            }

            // TODO: run full res on good ones

            // formula for generating tilt/phi values from ASIFT paper
            int tiltIdx, phiIdx;
            for (tiltIdx = 1; tiltIdx < 6; tiltIdx++)
            {
                double tilt = Math.Pow(2, tiltIdx / 2.0);
                double deltaPhi = 72.0 / tilt;
                int numPhiSteps = (int)Math.Ceiling(180 / deltaPhi);
                for (phiIdx = 0; phiIdx < numPhiSteps; phiIdx++)
                {
                    double phi = phiIdx * deltaPhi;

                    Image<Gray, byte> skewImage, skewMask;
                    Matrix<float> A;
                    AffineSkew(tilt, phi * Math.PI / 180, image, mask, out skewImage, out skewMask, out A);

                    float det = A[0, 0] * A[1, 1] - A[0, 1] * A[1, 0];
                    Matrix<float> Ai = InvertAffine(A);

                    foreach (SIFTFeature feat in DetectSIFT(skewImage, skewMask))
                    {
                        double fx = feat.Location.X;
                        double fy = feat.Location.Y;
                        double newX = fx * Ai[0, 0] + fy * Ai[0, 1] + Ai[0, 2];
                        double newY = fx * Ai[1, 0] + fy * Ai[1, 1] + Ai[1, 2];
                        if (newX < 0 || newY < 0)
                        {
                            continue;
                        }
                        yield return new SIFTFeature(
                            new Vector2(newX, newY) / scale,
                            feat.Size / scale,
                            feat.Angle,
                            feat.Octave,
                            feat.Response,
                            feat.Descriptor
                            );
                    }
                }
            }
        }

        #region Internal helpers
        static Vector2 Rotate(Vector2 pt, double theta)
        {
            double sin = Math.Sin(theta),
                   cos = Math.Cos(theta);
            return new Vector2(
                cos * pt.X - sin * pt.Y,
                sin * pt.X + cos * pt.Y
                );
        }

        static Matrix<float> InvertAffine(Matrix<float> A)
        {
            Matrix<float> bigger = new Matrix<float>(3, 3);
            int i, j;
            for (i = 0; i < 2; i++)
            {
                for (j = 0; j < 3; j++)
                {
                    bigger[i, j] = A[i, j];
                }
            }
            bigger[2, 2] = 1;
            Matrix<float> inv = new Matrix<float>(3, 3);
            CvInvoke.Invert(bigger, inv, DecompMethod.Svd);
            Matrix<float> res = new Matrix<float>(2, 3);
            for (i = 0; i < 2; i++)
            {
                for (j = 0; j < 3; j++)
                {
                    res[i, j] = inv[i, j];
                }
            }
            return res;
        }

        Vector2 ApplyAffine(Vector2 pt, Matrix<float> A)
        {
            return new Vector2(
                pt.X * A[0, 0] + pt.Y * A[0, 1] + A[0, 2],
                pt.Y * A[1, 0] + pt.Y * A[1, 1] + A[1, 2]
                );
        }
        #endregion
    }
}

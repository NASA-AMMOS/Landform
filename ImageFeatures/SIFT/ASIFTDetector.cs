using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// As described in ASIFT: A NEW FRAMEWORK FOR FULLY AFFINE INVARIANT IMAGE COMPARISON
    /// http://www.cmap.polytechnique.fr/~yu/publications/ASIFT_SIIMS_final.pdf
    /// </summary>
    public class ASIFTDetector : SIFTDetector
    {
        public Func<SIFTFeature, bool> Filter = null;

        public override IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            List<ImageFeature> tmp = new List<ImageFeature>();
            IEnumerable<ImageFeature> detectAndCompute(Image<Gray, byte> img, Image<Gray, byte> msk)
            {
                tmp.Clear();
                foreach (var f in base.Detect(img, msk))
                {
                    if ((Filter == null || Filter((SIFTFeature)f)) && CheckValidFeature(f, img, msk))
                    {
                        tmp.Add(f);
                    }
                }
                AddDescriptors(img, tmp);
                return tmp;
            }

            var origImage = image.ToEmguGrayscale();
            var origMask = (mask != null) ? mask.ToEmguGrayscale() : null;

            foreach (var f in detectAndCompute(origImage, origMask))
            {
                yield return f;
            }

            // formula for generating tilt/phi values from ASIFT paper
            for (int tiltIdx = 0; tiltIdx < 6; tiltIdx++)
            {
                // paper suggests latitude sampling such that the tilts follow a geometric series:
                // 1, a, a^2, a^3 ... a^n. they recommend a = sqrt(2)
                double tilt = Math.Pow(2, tiltIdx / 2.0); //paper: t

                // paper suggests phi follow an arithmetic series 0, b/t, .... kb/t
                // where b = 72degrees and k is the last integer such that kb/t < 180 deg
                double deltaPhiDegrees = 72.0 / tilt; //paper: b/t
                int numPhiSteps = Math.Max(1, (int)(179 / deltaPhiDegrees)); //paper: k

                for (int phiIdx = 0; phiIdx < numPhiSteps; phiIdx++)
                {
                    double phiDegrees = phiIdx * deltaPhiDegrees;
                    
                    Matrix<float> A;
                    Image<Gray, byte> skewImage, skewMask;
                    AffineSkew(tilt, phiDegrees, origImage, origMask, out skewImage, out skewMask, out A);
                    
                    Matrix<float> Ai = new Matrix<float>(2, 3);
                    Emgu.CV.CvInvoke.InvertAffineTransform(A, Ai);

                    //need to add descriptors here based on the skewed image
                    foreach (var f in detectAndCompute(skewImage, skewMask))
                    {
                        //the feature may be out of bounds or in masked parts of the original image
                        //but that will be checked later
                        double fx = f.Location.X;
                        double fy = f.Location.Y;
                        f.Location.X = fx * Ai[0, 0] + fy * Ai[0, 1] + Ai[0, 2];
                        f.Location.Y = fx * Ai[1, 0] + fy * Ai[1, 1] + Ai[1, 2];
                        yield return f;
                    }
                }
            }
        }

        /// <summary>
        /// Apply affine deformation to an input image with mask.
        /// </summary>
        /// <param name="tilt">Simulated camera tilt</param>
        /// <param name="phiDegrees">Simulated camera roll</param>
        /// <param name="image">Image to deform</param>
        /// <param name="mask">Mask with 0 for invalid pixels</param>
        /// <param name="outImage">Output image</param>
        /// <param name="outMask">Output mask image</param>
        /// <param name="A">Will be filled with the affine matrix used</param>
        private static void AffineSkew(double tilt, double phiDegrees, Image<Gray, byte> image, Image<Gray, byte> mask,
                                       out Image<Gray, byte> outImage, out Image<Gray, byte> outMask,
                                       out Matrix<float> A)
        {
            int width = image.Width;
            int height = image.Height;
            if (mask == null)
            {
                mask = new Image<Gray, byte>(width, height);
                mask.SetValue(255);
            }

            A = new Matrix<float>(new float[,] { { 1, 0, 0 }, { 0, 1, 0 } });
            
            if (Math.Abs(phiDegrees) > 1e-6)
            {
                //get rotation matrix about the center of the image (positive angle is counter-clockwise, right handed)
                System.Drawing.PointF center = new System.Drawing.PointF(width / 2.0f, height / 2.0f);
                Emgu.CV.CvInvoke.GetRotationMatrix2D(center, phiDegrees, 1, A);

                //calculate the size of image required for rotated rect
                Emgu.CV.Util.VectorOfPointF corners = new VectorOfPointF();
                Emgu.CV.Util.VectorOfPointF cornersRotated = new VectorOfPointF();
                corners.Push(new System.Drawing.PointF[]
                        {
                            new System.Drawing.PointF(0, 0),
                            new System.Drawing.PointF(width, 0),
                            new System.Drawing.PointF(width, height),
                            new System.Drawing.PointF(0, height)
                        });
                Emgu.CV.CvInvoke.Transform(corners, cornersRotated, A);
                var cornersRotatedX = cornersRotated.ToArray().Select(c => c.X);
                var cornersRotatedY = cornersRotated.ToArray().Select(c => c.Y);
                float minX = cornersRotatedX.Min();
                float maxX = cornersRotatedX.Max();
                float minY = cornersRotatedY.Min();
                float maxY = cornersRotatedY.Max();
                width = (int)Math.Ceiling(maxX - minX);
                height = (int)Math.Ceiling(maxY - minY);

                //calculate the offset to keep the image centered in the new larger image
                System.Drawing.PointF newCenter = new System.Drawing.PointF(width / 2.0f, height / 2.0f);
                A[0, 2] += newCenter.X - center.X;
                A[1, 2] += newCenter.Y - center.Y;

                //rotate the image and the mask
                var bg = new Gray(0);
                image = image.WarpAffine(A.Mat, width, height, Inter.Linear, Warp.Default, BorderType.Constant, bg);
                mask = mask.WarpAffine(A.Mat, width, height, Inter.Linear, Warp.Default, BorderType.Constant, bg);
            }

            if (Math.Abs(tilt) > 1e-6)
            {
                // paper recommends antialising filter in the direction of the t (tilt). 
                // gaussian filter with std dev c*sqrt(t*t-1). recommend c = 0.8 used by Lowe in SIFT
                double sigma = 0.8 * Math.Sqrt(tilt * tilt - 1);
                int kernelDim = 3; //must be odd
                image = image.SmoothGaussian(kernelDim, kernelDim, sigma, 0.01);

                // resize the image using nearest neighbor sampling
                image = image.Resize((int)(width / tilt), height, Inter.Nearest, false);
                mask = mask.Resize((int)(width / tilt), height, Inter.Nearest, false);

                // capture this scaling in the A matrix, so that inverting it can return the features to full 
                // resolution. equivalent to scale in x only matrix * A
                A[0, 0] /= (float)tilt;
                A[0, 1] /= (float)tilt;
                A[0, 2] /= (float)tilt;
            }

            outImage = image;
            outMask = mask;
        }
    }
}

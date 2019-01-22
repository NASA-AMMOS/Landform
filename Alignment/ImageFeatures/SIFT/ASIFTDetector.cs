using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Microsoft.Xna.Framework;
using Emgu.CV.Util;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using System.Drawing;

namespace OPS.Alignment
{
    // As described in ASIFT: A NEW FRAMEWORK FOR FULLY AFFINE INVARIANT IMAGE COMPARISON
    // http://www.cmap.polytechnique.fr/~yu/publications/ASIFT_SIIMS_final.pdf

    public class ASIFTDetector : IFeatureDetector
    {
        public ASIFTDetector()
        { }

        public IEnumerable<ImageFeature> Detect(OPS.Imaging.Image image, OPS.Imaging.Image mask = null)
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
            
            if (Math.Abs(phi) > 1e-6)
            {
                //get rotation matrix about the center of the image (positive angle is counter-clockwise, right handed)
                System.Drawing.PointF center = new System.Drawing.PointF(width / 2.0f, height / 2.0f);
                Emgu.CV.CvInvoke.GetRotationMatrix2D(center, MathHelper.ToDegrees(phi), 1, A);

                //calculate the size of image required for rotated rect
                Emgu.CV.Util.VectorOfPointF corners = new VectorOfPointF();
                Emgu.CV.Util.VectorOfPointF cornersRotated = new VectorOfPointF();
                corners.Push(new System.Drawing.PointF[] { new System.Drawing.PointF(0, 0), new System.Drawing.PointF(width, 0), new System.Drawing.PointF(width, height), new System.Drawing.PointF(0, height) });
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
                image = image.WarpAffine(A.Mat, width, height, Inter.Linear, Warp.Default, BorderType.Constant, new Gray(0));
                mask = mask.WarpAffine(A.Mat, width, height, Inter.Linear, Warp.Default, BorderType.Constant, new Gray(0));
            }

            if (Math.Abs(tilt) > 1e-6)
            {
                // paper recommends antialising filter in the direction of the t (tilt). 
                // gaussian filter with std dev c*sqrt(t*t-1). recommend c = 0.8 used by Lowe in SIFT
                double sigma = 0.8 * Math.Sqrt(tilt * tilt - 1);
                image = image.SmoothGaussian(0, 0, sigma, 0.01);

                // resize the image using nearest neighbor sampling
                image = image.Resize((int)(width / tilt), height, Inter.Nearest, false);
                mask = mask.Resize((int)(width / tilt), height, Inter.Nearest, false);

                // capture this scaling in the A matrix, so that inverting it can return the features to full 
                // resolution. equivalent to scale in x only matrix * A
                A[0, 0] /= (float)tilt;
                A[0, 1] /= (float)tilt;
                A[0, 2] /= (float)tilt;
            }

            width = image.Width;
            height = image.Height;
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
                        //opencv only honors mask pixel as center points of features, not that the feature doesn't sample masked pixels
                        // this code tests all pixels within the size of the feature to ensure they are valid
                        if (mask != null)
                        {
                            if (!TestValidFeature(mask, keypoints[i].Point, keypoints[i].Size))
                                continue;
                        }

                        byte[] desc = new byte[128];
                        for (int j = 0; j < 128; j++)
                        {
                            desc[j] = (byte)descriptors[i, j];
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

        private static bool TestValidFeature(Image<Gray, byte> mask, PointF point, float size)
        {

            if (!TestValidPixel(mask, point))
                return false;

            int halfDiameter = (int)Math.Ceiling(size / 2.0f);
            int minCol = (int)(point.X - halfDiameter);
            int maxCol = (int)Math.Ceiling(point.X + halfDiameter);
            int minRow = (int)(point.Y - halfDiameter);
            int maxRow = (int)Math.Ceiling(point.Y + halfDiameter);

            for (int idxRow = minRow; idxRow <= maxRow; idxRow++)
            {
                for (int idxCol = minCol; idxCol <= maxCol; idxCol++)
                {
                    if (!TestValidPixel(mask, new Point(idxCol,idxRow)))
                        return false;
                }
            }

            return true;
        }

        private static bool TestValidPixel(Image<Gray, byte> mask, Point point)
        {
            if ((point.X < 0) || (point.Y < 0) || (point.X > mask.Width - 1) || (point.Y > mask.Height - 1))
                return false;

            if (mask[point.Y, point.X].Intensity == 0)
                return false;

            return true;
        }

        private static bool TestValidPixel(Image<Gray, byte> mask, PointF point)
        {
            int minCol = (int)(point.X);
            int maxCol = (int)Math.Ceiling(point.X);
            int minRow = (int)(point.Y);
            int maxRow = (int)Math.Ceiling(point.Y);

            if((minCol < 0) || (minRow < 0) || (maxCol > mask.Width-1) || (maxRow > mask.Height-1))
                return false;
           
            if (mask[minRow, minCol].Intensity == 0)
                return false;
            else if (mask[minRow, maxCol].Intensity == 0)
                return false;
            else if (mask[maxRow, minCol].Intensity == 0)
                return false;
            else if (mask[maxRow, maxCol].Intensity == 0)
                return false;

            return true;
        }

        /// <summary>
        /// Detect ASIFT features in an image.
        /// </summary>
        /// <param name="image">Input image</param>
        /// <param name="mask">Input mask, with zero signifying invalid pixels and</param>
        /// <returns>Enumerable of all detected features</returns>
        public IEnumerable<ImageFeature> Detect(Image<Gray, byte> image, Image<Gray, byte> mask)
        {
            foreach (SIFTFeature feat in DetectSIFT(image, mask))
            {
                yield return new SIFTFeature(
                    feat.Location,
                    feat.Size,
                    feat.Angle,
                    feat.Octave,
                    feat.Response,
                    feat.Descriptor
                    );
            }

            // formula for generating tilt/phi values from ASIFT paper
            int tiltIdx, phiIdx;
            for (tiltIdx = 1; tiltIdx < 6; tiltIdx++)
            {
                // paper suggests latitude sampling such that the tilts follow a geometric series:
                // 1,a, a^2, a^3 ... a^n. they recommend a = sqrt(2)
                double tilt = Math.Pow(2, tiltIdx / 2.0);                   //paper: t

                // paper suggests phi follow an arithmetic series 0, b/t, .... kb/t
                // where b = 72degrees and k is the last integer such that kb/t < 180 deg
                double deltaPhiDegrees = 72.0 / tilt;                       //paper: b/t
                int numPhiSteps = Math.Max(1,(int)(179 / deltaPhiDegrees)); //paper: k

                for (phiIdx = 0; phiIdx < numPhiSteps; phiIdx++)
                {
                    double phiDegrees = phiIdx * deltaPhiDegrees;
                    
                    Matrix<float> A;
                    Image<Gray, byte> skewImage, skewMask;
                    AffineSkew(tilt, MathHelper.ToRadians(phiDegrees), image, mask, out skewImage, out skewMask, out A);
                    
                    Matrix<float> Ai = new Matrix<float>(2, 3);
                    Emgu.CV.CvInvoke.InvertAffineTransform(A, Ai);

                    foreach (SIFTFeature feat in DetectSIFT(skewImage, skewMask))
                    {
                        double fx = feat.Location.X;
                        double fy = feat.Location.Y;
                        double newX = fx * Ai[0, 0] + fy * Ai[0, 1] + Ai[0, 2];
                        double newY = fx * Ai[1, 0] + fy * Ai[1, 1] + Ai[1, 2];

                        if (!TestValidFeature(mask, new PointF((float)newX, (float)newY), (float)feat.Size))
                            continue;

                        yield return new SIFTFeature(
                            new Vector2(newX, newY),
                            feat.Size,
                            feat.Angle,
                            feat.Octave,
                            feat.Response,
                            feat.Descriptor
                            );
                    }
                }
            }
        }
    }
}

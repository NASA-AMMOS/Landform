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

namespace OPS.Alignment
{
    public class ASIFT
    {
        static Vector2 Rotate(Vector2 pt, double theta)
        {
            double sin = Math.Sin(theta),
                   cos = Math.Cos(theta);
            return new Vector2(
                cos * pt.X - sin * pt.Y,
                sin * pt.X + cos * pt.Y
                );
        }
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

        static object KeypointLock = new object();
        static object DescriptorLock = new object();

        static IEnumerable<Feature> DetectSkewed(Image<Gray, byte> image, Image<Gray, byte> mask, int numFeatures = 1000, int numOctaves = 4)
        {
            using (SIFT sift = new SIFT(numFeatures, numOctaves, 0.04, 10.0, 1.6))
            {
                MKeyPoint[] keypoints = sift.Detect(image, mask);
                if (keypoints.Length < 3) yield break;

                using (Matrix<float> descriptors = new Matrix<float>(keypoints.Length, 128))
                {
                    try
                    {
                        sift.Compute(image, new VectorOfKeyPoint(keypoints), descriptors);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Console.Write(e.StackTrace);
                        Console.ReadLine();
                        throw;
                    }

                    int i;
                    for (i = 0; i < keypoints.Length; i++)
                    {
                        float[] desc = new float[128];
                        for (int j = 0; j < 128; j++)
                        {
                            desc[j] = descriptors[i, j];
                        }
                        yield return new Feature(
                            new Vector2(keypoints[i].Point.X, keypoints[i].Point.Y),
                            keypoints[i].Size,
                            keypoints[i].Angle,
                            desc
                           );
                    }
                }
            }
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

        public static IEnumerable<Feature> Detect(Image<Gray, byte> image, Image<Gray, byte> mask, bool affine = true)
        {
            double scale = 1;

            foreach (Feature feat in DetectSkewed(image, mask))
            {
                yield return new Feature(
                    feat.location / scale,
                    feat.size / scale,
                    feat.angle,
                    feat.descriptor
                    );
            }
            if (!affine) yield break;

            if (image.Width > 512 || image.Height > 512)
            {
                scale = 512.0 / Math.Max(image.Width, image.Height);
                image = image.Resize(scale, Inter.Lanczos4);
                mask = mask.Resize(scale, Inter.Nearest);
            }

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

                    foreach (Feature feat in DetectSkewed(skewImage, skewMask))
                    {
                        double fx = feat.location.X;
                        double fy = feat.location.Y;
                        double newX = fx * Ai[0, 0] + fy * Ai[0, 1] + Ai[0, 2];
                        double newY = fx * Ai[1, 0] + fy * Ai[1, 1] + Ai[1, 2];
                        if (newX < 0 || newY < 0)
                        {
                            continue;
                            //Console.WriteLine("Something's not right here. {0}, {1}", newX, newY);
                        }
                        yield return new Feature(
                            new Vector2(newX, newY) / scale,
                            feat.size / scale,
                            feat.angle,
                            feat.descriptor
                            );
                    }
                }
            }
        }
    }
}

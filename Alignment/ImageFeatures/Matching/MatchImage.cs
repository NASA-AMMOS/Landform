using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using OPS.Imaging;
using OPS.Imaging.Emgu;

namespace OPS.Alignment
{
    /// <summary>
    /// Creates a debug output image showing matches between two images
    /// </summary>
    public class MatchImage
    {
        public static Imaging.Image Create(IImageLoader loader, ImagePairCorrespondence matches,
                                           ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                           string time = null)
        {
            Image<Gray, byte> modelImage = loader.LoadImage(matches.ModelImageUrl).ToEmguGrayscale();
            Image<Gray, byte> dataImage = loader.LoadImage(matches.DataImageUrl).ToEmguGrayscale();
            
            ImageFeature[] feat0;
            ImageFeature[] feat1;
            int[] indices;
            matches.Flatten(modelFeatures, dataFeatures, out feat0, out feat1, out indices);
            var sift0 = feat0.Cast<SIFTFeature>().ToList();
            var sift1 = feat1.Cast<SIFTFeature>().ToList();

            Matrix<float> descr0 = ToDescriptorMatrix(sift0);
            Matrix<float> descr1 = ToDescriptorMatrix(sift1);
            VectorOfKeyPoint kp0 = new VectorOfKeyPoint(sift0.CastToMKeyPoint().ToArray());
            VectorOfKeyPoint kp1 = new VectorOfKeyPoint(sift1.CastToMKeyPoint().ToArray());

            VectorOfVectorOfDMatch matchVector = new VectorOfVectorOfDMatch();
            for (int i = 0; i < feat1.Length; i++)
            {
                matchVector.Push(new VectorOfDMatch(new MDMatch[]
                {
                    new MDMatch() { TrainIdx = indices[i], QueryIdx = i }
                }));
            }
            Matrix<byte> mask = new Matrix<byte>(feat1.Length, 1);
            mask.SetValue(255);
            int nonZero = feat1.Length;

            Image<Bgr, byte> result = CreateMatchImage(kp0, kp1, modelImage, dataImage, matchVector, mask, nonZero, time);
            return result.ToOPSImage();
        }

        public static void WriteMatchImage(IImageLoader loader, ImagePairCorrespondence matches,
                                           ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                           string outFile, string time = null)
        {
            var img = Create(loader, matches, modelFeatures, dataFeatures, time);
            img.Save<byte>(outFile);
        }

        public static Matrix<float> ToDescriptorMatrix(List<SIFTFeature> features)
        {
            Matrix<float> res = new Matrix<float>(features.Count, features[0].Descriptor.Length);
            float[,] data = res.Data;
            int i, j;
            for (i = 0; i < features.Count; i++)
            {
                var d = ((FeatureDescriptor<byte>)features[i].Descriptor).Data;
                for (j = 0; j < d.Length; j++)
                {
                    data[i, j] = d[j];
                }
            }
            return res;
        }

        // https://github.jpl.nasa.gov/OnSight/Landform/issues/439
        private static Image<Bgr, byte> CreateMatchImage(VectorOfKeyPoint kp0, VectorOfKeyPoint kp1,
                                                         Image<Gray, byte> modelImage, Image<Gray, byte> dataImage,
                                                         VectorOfVectorOfDMatch matches, Matrix<byte> mask, int nonZero,
                                                         string time = null)
        {
            int i;

            Image<Hsv, byte> hsvColors = new Image<Hsv, byte>(1, nonZero);
            for (i = 0; i < nonZero; i++)
            {
                hsvColors[i, 0] = new Hsv(i * 180.0 / (nonZero - 1), 2550, 255);
            }
            Image<Bgr, byte> bgrColors = hsvColors.Convert<Bgr, byte>();

            Image<Bgr, byte> result = new Image<Bgr, byte>(modelImage.Width + dataImage.Width,
                                                           Math.Max(modelImage.Height, dataImage.Height));
            result.ROI = new Rectangle(0, 0, modelImage.Width, modelImage.Height);
            modelImage.Convert<Bgr, byte>().CopyTo(result);
            result.ROI = new Rectangle(modelImage.Width, 0, dataImage.Width, dataImage.Height);
            dataImage.Convert<Bgr, byte>().CopyTo(result);
            result.ROI = new Rectangle(0, 0, modelImage.Width + dataImage.Width,
                                       Math.Max(modelImage.Height, dataImage.Height));

            int pointNum = 0;
            for (i = 0; i < matches.Size; i++)
            {
                if (mask[i, 0] == 0) continue;
                PointF modelPoint = kp0[matches[i][0].TrainIdx].Point,
                       dataPoint = kp1[matches[i][0].QueryIdx].Point;

                result.Draw(new CircleF(modelPoint, 5.0f), new Bgr(0, 255, 0), 2);
                result.Draw(new CircleF(dataPoint + new SizeF(modelImage.Width, 0), 5.0f), new Bgr(0, 255, 0), 2);

                result.Draw(new LineSegment2DF(modelPoint, dataPoint + new SizeF(modelImage.Width, 0)),
                            bgrColors[pointNum, 0], 1);
                pointNum++;
            }

            result.Draw(new Rectangle(0, 0, 490, 70), new Bgr(255, 50, 50), -1);

            result.Draw("matches: " + matches.Size, new Point(5, 55), Emgu.CV.CvEnum.FontFace.HersheySimplex, 2, new Bgr(255, 255, 255), 2);

            if (pointNum != matches.Size)
            {
                result.Draw("matches (unmasked): " + pointNum, new Point(5, 115), Emgu.CV.CvEnum.FontFace.HersheySimplex, 2, new Bgr(255, 255, 255), 2);
            }

            if (time != null)
            {
                result.Draw(new Rectangle(0, 70, 490, 70), new Bgr(255, 50, 50), -1);
                result.Draw("time: " + time, new Point(5, 125), Emgu.CV.CvEnum.FontFace.HersheySimplex, 2,
                            new Bgr(255, 255, 255), 2);
            }

            return result;
        }
    }
}


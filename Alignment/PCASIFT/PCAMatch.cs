using Emgu.CV.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using Emgu.CV.XFeatures2D;
using Emgu.CV;
using System.Drawing;
using OPS.Imaging.Emgu;
using System.Runtime.ExceptionServices;

namespace OPS.Alignment
{
    public class PCAMatch
    {
        [HandleProcessCorruptedStateExceptions]
        public static bool Match(Image<Gray, byte> model, Image<Gray, byte> data,
           Matrix<float> descrA, VectorOfKeyPoint kp0, Matrix<float> descrB, VectorOfKeyPoint kp1, string outFile)
        {
            if (kp0.Size < 30 || kp1.Size < 30) return false;

            Mat descr0 = descrA.Mat;
            Mat descr1 = descrB.Mat;
            // Match descriptors
            VectorOfVectorOfDMatch matches = new VectorOfVectorOfDMatch();
            using (BFMatcher bfm = new BFMatcher(DistanceType.L2))
            {
                bfm.Add(descr0);
                bfm.KnnMatch(descr1, matches, 2, null);
            }


            Matrix<byte> mask = new Matrix<byte>(matches.Size, 1);
            mask.SetValue(255);


            // OpenCV standard correspondence checks
            for (int idx = 0; idx < matches.Size; idx++)
            {
                if (matches[idx][0].Distance > matches[idx][1].Distance * 0.8)
                {
                    mask[idx, 0] = 0;
                }
            }
            int nonZero = CvInvoke.CountNonZero(mask);
            if (nonZero < 1) return false;
            nonZero = Features2DToolbox.VoteForSizeAndOrientation(kp0, kp1, matches, mask.Mat, 1.5, 20);
            if (nonZero < 1) return false;

            Image<Bgr, byte> result = CreateMatchImage(kp0, kp1, model.Convert<Gray, byte>(), data.Convert<Gray, byte>(), matches, mask, nonZero);
            result.Save(outFile);
            return true;
        }
        public static bool Match(Image<Gray, byte> model, Image<Gray, byte> data,
           List<SIFTFeature> modelFeat, List<SIFTFeature> dataFeat, string outFile)
        {
            List<SIFTFeature> feat0 = modelFeat;
            List<SIFTFeature> feat1 = dataFeat;

            Matrix<float> descr0 = ToDescriptorMatrix(feat0);
            Matrix<float> descr1 = ToDescriptorMatrix(feat1);
            VectorOfKeyPoint kp0 = ToVOKP(feat0);
            VectorOfKeyPoint kp1 = ToVOKP(feat1);

            return Match(model, data, descr0, kp0, descr1, kp1, outFile);
        }

        public static void WriteMatchImage(ImagePairCorrespondence matches, string outFile)
        {
            Imaging.Image model = matches.ModelImage.Image;
            Imaging.Image data = matches.DataImage.Image;
            Image<Gray, byte> modelImage = model.ToEmguGrayscale();
            Image<Gray, byte> dataImage = data.ToEmguGrayscale();

            matches.Compact();
            ImageFeature[] feat0;
            ImageFeature[] feat1;
            int[] indices;
            matches.Flatten(out feat0, out feat1, out indices);

            Matrix<float> descr0 = ToDescriptorMatrix(feat0.Cast<SIFTFeature>().ToList());
            Matrix<float> descr1 = ToDescriptorMatrix(feat1.Cast<SIFTFeature>().ToList());
            VectorOfKeyPoint kp0 = ToVOKP(feat0.Cast<SIFTFeature>().ToList());
            VectorOfKeyPoint kp1 = ToVOKP(feat1.Cast<SIFTFeature>().ToList());

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

            Image<Bgr, byte> result = CreateMatchImage(kp0, kp1, modelImage, dataImage, matchVector, mask, nonZero);
            result.Save(outFile);
        }

        static VectorOfKeyPoint ToVOKP(List<SIFTFeature> kps)
        {
            VectorOfKeyPoint res = new VectorOfKeyPoint();
            res.Push(kps.Select(kp =>
            {
                MKeyPoint _kp = new MKeyPoint();
                _kp.Size = (float)kp.Size;
                _kp.Point = new PointF((float)kp.Location.X, (float)kp.Location.Y);
                _kp.Angle = (float)kp.Angle;
                return _kp;
            }).ToArray());
            return res;
        }

        public static Matrix<float> ToDescriptorMatrix(List<SIFTFeature> features)
        {
            Matrix<float> res = new Matrix<float>(features.Count, features[0].Descriptor.Length);
            float[,] data = res.Data;
            int i, j;
            for (i = 0; i < features.Count; i++)
            {
                var d = ((FeatureDescriptor<float>)features[i].Descriptor).Data;
                for (j = 0; j < d.Length; j++)
                {
                    data[i, j] = d[j];
                }
            }
            return res;
        }



        private static Image<Bgr, byte> CreateMatchImage(VectorOfKeyPoint kp0, VectorOfKeyPoint kp1, Image<Gray, byte> modelImage, Image<Gray, byte> dataImage, VectorOfVectorOfDMatch matches, Matrix<byte> mask, int nonZero)
        {
            int i;

            Image<Hsv, byte> hsvColors = new Image<Hsv, byte>(1, nonZero);
            for (i = 0; i < nonZero; i++)
            {
                hsvColors[i, 0] = new Hsv(i * 180.0 / (nonZero - 1), 2550, 255);
            }
            Image<Bgr, byte> bgrColors = hsvColors.Convert<Bgr, byte>();

            Image<Bgr, byte> result = new Image<Bgr, byte>(modelImage.Width + dataImage.Width, Math.Max(modelImage.Height, dataImage.Height));
            result.ROI = new Rectangle(0, 0, modelImage.Width, modelImage.Height);
            modelImage.Convert<Bgr, Byte>().CopyTo(result);
            result.ROI = new Rectangle(modelImage.Width, 0, dataImage.Width, dataImage.Height);
            dataImage.Convert<Bgr, Byte>().CopyTo(result);
            result.ROI = new Rectangle(0, 0, modelImage.Width + dataImage.Width, Math.Max(modelImage.Height, dataImage.Height));

            int pointNum = 0;
            for (i = 0; i < matches.Size; i++)
            {
                if (mask[i, 0] == 0) continue;
                PointF modelPoint = kp0[matches[i][0].TrainIdx].Point,
                       dataPoint = kp1[matches[i][0].QueryIdx].Point;

                result.Draw(new CircleF(modelPoint, 5.0f), new Bgr(0, 255, 0), 2);
                result.Draw(new CircleF(dataPoint + new SizeF(modelImage.Width, 0), 5.0f), new Bgr(0, 255, 0), 2);
                
                result.Draw(new LineSegment2DF(modelPoint, dataPoint + new SizeF(modelImage.Width, 0)), bgrColors[pointNum, 0], 1);
                pointNum++;
            }

            result.Draw(new Rectangle(0, 0, 490, 70), new Bgr(255, 50, 50), -1);
            result.Draw("matches: " + kp0.Size, new Point(5, 55), Emgu.CV.CvEnum.FontFace.HersheySimplex, 2, new Bgr(255, 255, 255), 2);

            return result;
        }

        public static bool Match(ImagePairCorrespondence matches, string outfile)
        {
            Imaging.Image model = matches.ModelImage.Image;
            Imaging.Image data = matches.DataImage.Image;
            Image<Gray, byte> modelImage = model.ToEmguGrayscale();
            Image<Gray, byte> dataImage = data.ToEmguGrayscale();

            return Match(modelImage, dataImage, matches.ModelFeatures.Cast<SIFTFeature>().ToList(), 
                                                matches.DataFeatures.Cast<SIFTFeature>().ToList(), outfile);
        }
    }
}


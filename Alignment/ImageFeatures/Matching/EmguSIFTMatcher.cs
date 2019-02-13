using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Cuda;
using Emgu.CV.Features2D;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using OPS.Util;

namespace OPS.Alignment
{
    public class EmguSIFTMatcher : IFeatureMatcher
    {
        public ImagePairCorrespondence Match(AlignmentScene scene, URLPair pair)
        {
            var modelUrl = pair.One;
            var dataUrl = pair.Two;
            var modelFeat = scene.DetectedFeatures[modelUrl];
            var dataFeat = scene.DetectedFeatures[dataUrl];

            SIFTFeature[] feat0 = modelFeat.Cast<SIFTFeature>().ToArray();
            SIFTFeature[] feat1 = dataFeat.Cast<SIFTFeature>().ToArray();

            Matrix<float> descr0 = ToDescriptorMatrix1(feat0);
            Matrix<float> descr1 = ToDescriptorMatrix1(feat1);
            VectorOfKeyPoint kp0 = ToVOKP(feat0);
            VectorOfKeyPoint kp1 = ToVOKP(feat1);
            
            Matrix<int> indices = new Matrix<int>(descr1.Rows, 2);

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
            if (nonZero < 1) { return null; }
            lock (GlobalLock)
            {
                nonZero = Features2DToolbox.VoteForSizeAndOrientation(kp0, kp1, matches, mask.Mat, 1.5, 20);
            }
            if (nonZero < 1) { return null; }

            List<KeyValuePair<int, int>> dataToModel = new List<KeyValuePair<int, int>>();
            for (int idx = 0; idx < matches.Size; idx++)
            {
                if (mask[idx, 0] != 0)
                {
                    var match = matches[idx][0];
                    dataToModel.Add(new KeyValuePair<int, int>(match.QueryIdx, match.TrainIdx));
                }
            }

            return new ImagePairCorrespondence(modelUrl, dataUrl, dataToModel);
        }

        static VectorOfKeyPoint ToVOKP(SIFTFeature[] kps)
        {
            VectorOfKeyPoint res = new VectorOfKeyPoint();
            res.Push(kps.Select(kp =>
            {
                MKeyPoint _kp = new MKeyPoint();
                _kp.Size = (float)kp.Size;
                _kp.Point = new System.Drawing.PointF((float)kp.Location.X, (float)kp.Location.Y);
                _kp.Angle = (float)kp.Angle;
                _kp.Octave = kp.Octave;
                _kp.Response = (float)kp.Response;
                return _kp;
            }).ToArray());
            return res;
        }

        static Matrix<T> ToDescriptorMatrix2<T>(SIFTFeature[] features) where T: struct
        {
            Matrix<T> res = new Matrix<T>(features.Length, features[0].Descriptor.Length);
            T[,] data = res.Data;
            int i, j;
            for (i = 0; i < features.Length; i++)
            {
                var d = features[i].Descriptor;
                if (d.ElementType != typeof(T) || d.Length != res.Cols)
                {
                    throw new InvalidOperationException("Mismatched descriptor types");
                }

                var fd = (FeatureDescriptor<T>)d;
                for (j = 0; j < d.Length; j++)
                {
                    data[i, j] = fd.Data[j];
                }
            }
            return res;
        }
        static Matrix<float> ToDescriptorMatrix1(SIFTFeature[] features)
        {
            var d0 = features[0].Descriptor;
            if (d0.ElementType == typeof(float))
            {
                return ToDescriptorMatrix2<float>(features);
            }
            //else if (d0.ElementType == typeof(byte))
            //{
            //    return ToDescriptorMatrix2<byte>(features);
            //}
            throw new ArgumentException("descriptors must be byte or float");
        }

        private static readonly object GlobalLock = new object();
    }
}

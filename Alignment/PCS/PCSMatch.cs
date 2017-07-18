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
using OPS.Pipeline;

namespace OPS.Alignment
{
    public class PCSMatch
    {
        [HandleProcessCorruptedStateExceptions]
        public ImagePairCorrespondence Match(ImageRef model, ImageRef data,
            IEnumerable<ImageFeature> modelFeat, IEnumerable<ImageFeature> dataFeat, float theta = 0.9f, int r = 100)
        {
            SIFTFeature[] feat0 = modelFeat.Cast<SIFTFeature>().ToArray();
            SIFTFeature[] feat1 = dataFeat.Cast<SIFTFeature>().ToArray();

            Mat descr0 = ToDescriptorMatrix(feat0);
            Mat descr1 = ToDescriptorMatrix(feat1);
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

            List<VectorOfDMatch> topMatches = new List<VectorOfDMatch>();
            // OpenCV standard correspondence checks
            for (int idx = 0; idx < matches.Size; idx++)
            {
                if (matches[idx][0].Distance > matches[idx][1].Distance * 0.8)
                {
                    mask[idx, 0] = 0;
                    //continue;
                }

                if (matches[idx][0].Distance / matches[idx][1].Distance < theta)
                {
                    topMatches.Add(matches[idx]);
                }
            }
            VectorOfDMatch[] finalMatches = topMatches.OrderBy(x => x[0].Distance).ToArray();

            List<KeyValuePair<int, int>> dataToModel = new List<KeyValuePair<int, int>>();
            for (int idx = 0; idx < topMatches.Count; idx++)
            {
                if (idx >= r) continue;
                var match = topMatches[idx][0];
                dataToModel.Add(new KeyValuePair<int, int>(match.QueryIdx, match.TrainIdx));
            }

            System.Diagnostics.Debug.WriteLine(string.Format("Model features: {0}, Data features: {1}, Matches: {2}", feat0.Length, feat1.Length, dataToModel.Count));
            var res = new ImagePairCorrespondence(model, data, feat0, feat1, dataToModel);
            res.Compact();
            return res;
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

        static Mat ToDescriptorMatrix<T>(SIFTFeature[] features) where T : struct
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
            return res.Mat;
        }
        static Mat ToDescriptorMatrix(SIFTFeature[] features)
        {
            var d0 = features[0].Descriptor;
            if (d0.ElementType == typeof(float))
            {
                return ToDescriptorMatrix<float>(features);
            }
            else if (d0.ElementType == typeof(byte))
            {
                return ToDescriptorMatrix<byte>(features);
            }
            throw new ArgumentException("descriptors must be byte or float");
        }
    }
}


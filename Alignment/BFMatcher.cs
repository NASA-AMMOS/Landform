using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Alignment
{
    public class BFMatcher2
    {
        knnNode[][] Matches;

        public BFMatcher2(IEnumerable<ImageFeature> modelFeatures, IEnumerable<ImageFeature> dataFeatures)
        {
            Matches = modelFeatures.Select((x, j) => new knnNode[2]).ToArray();
        }

        public ImagePairCorrespondence Match(ImageRef model, ImageRef data, 
            IEnumerable<ImageFeature> modelFeat, IEnumerable<ImageFeature> dataFeat)
        {
            SIFTFeature[] feat0 = modelFeat.Cast<SIFTFeature>().ToArray();
            SIFTFeature[] feat1 = dataFeat.Cast<SIFTFeature>().ToArray();

            // Match descriptors
            KnnMatch(feat0, feat1, 2);
            
            Matrix<float> mask = Matrix<float>.Build.Dense(Matches.Length, 1);

            // OpenCV standard correspondence checks
            for (int idx = 0; idx < Matches.Length; idx++)
            {
                if (Matches[idx][0].Value > Matches[idx][1].Value * 0.8)
                {
                    mask[idx, 0] = 0;
                }
                else
                {
                    mask[idx, 0] = 255;
                }
            }

            List<KeyValuePair<int, int>> dataToModel = new List<KeyValuePair<int, int>>();
            for (int idx = 0; idx < Matches.Length; idx++)
            {
                if (mask[idx, 0] != 0)
                {
                    var match = Matches[idx][0];
                    dataToModel.Add(new KeyValuePair<int, int>(idx, match.Index));
                }
            }

            System.Diagnostics.Debug.WriteLine(string.Format("Model features: {0}, Data features: {1}, Matches: {2}", feat0.Length, feat1.Length, dataToModel.Count));
            var res = new ImagePairCorrespondence(model, data, feat0, feat1, dataToModel);
            res.Compact();
            return res;
        }

        private void KnnMatch(SIFTFeature[] modelFeat, SIFTFeature[] dataFeat, int k)
        {
            double[][] dist = new double[modelFeat.Length][].Select(x => new double[dataFeat.Length]).ToArray();
            knnNode[][] knnModel = new knnNode[modelFeat.Length][].Select(x => new knnNode[k]).ToArray();
            knnNode[][] knnData = new knnNode[dataFeat.Length][].Select(x => new knnNode[k]).ToArray();

            FeatureDescriptor<float>[] modelDescr = modelFeat.Select(m => (FeatureDescriptor<float>)m.Descriptor).ToArray();
            FeatureDescriptor<float>[] dataDescr = dataFeat.Select(m => (FeatureDescriptor<float>)m.Descriptor).ToArray();

            // Compute distance matrix
            Parallel.For(0, modelFeat.Length, i =>
            {
                SIFTFeature modelFeature = modelFeat[i];
                for (int j = 0; j < dataFeat.Length; j++)
                {
                    double err = 0;
                    var d0 = modelDescr[i];
                    var d1 = dataDescr[j];
                    for (int jerry = 0; jerry < d0.Length; jerry++)
                    {
                        double signedError = (d1[jerry] - d0[jerry]);
                        err += signedError * signedError;
                    }
                    dist[i][j] = err;
                }
            });

            // Get 2-nearest neighbors for each image, horizontally
            for (int i = 0; i < dist.Length; i++)
            { 
                double minval = double.MaxValue;
                double minval2 = double.MaxValue;
                int minI = 0;
                int minI2 = 0;

                for (int j = 0; j < dist[0].Length; j++)
                {
                    double currentDist = dist[i][j];
                    if (currentDist < minval2)
                    {
                        if (currentDist < minval)
                        {
                            minval2 = minval;
                            minI2 = minI;
                            minval = currentDist;
                            minI = j;
                        }
                        else
                        {
                            minval2 = currentDist;
                            minI2 = j;
                        }
                    }
                }
                knnModel[i] = new knnNode[] { new knnNode(minval, minI), new knnNode(minval2, minI2) };
            }

            // Get 2-nearest neighbors for each image, vertically
            for (int i = 0; i < dist[0].Length; i++)
            {
                double minval = double.MaxValue;
                double minval2 = double.MaxValue;
                int minI = 0;
                int minI2 = 0;

                for (int j = 0; j < dist.Length; j++)
                {
                    double currentDist = dist[j][i];
                    if (currentDist < minval2)
                    {
                        if (currentDist < minval)
                        {
                            minval2 = minval;
                            minI2 = minI;
                            minval = currentDist;
                            minI = j;
                        }
                        else
                        {
                            minval2 = currentDist;
                            minI2 = j;
                        }
                    }
                }
                knnData[i] = new knnNode[] { new knnNode(minval, minI), new knnNode(minval2, minI2) };
            }

            HashSet<int> removed = new HashSet<int>();
            // Filter matches that aren't symmetric
            for (int i = 0; i < knnModel.Length; i++)
            {
                int modelI = knnModel[i][0].Index;
                int dataI = knnData[modelI][0].Index;
                if (dataI != i)
                {
                    removed.Add(i);
                }
                else
                {
                    Matches[i] = new knnNode[] { knnData[i][0], knnData[i][1] };
                }
            }
            Matches = Matches.Where((x, j) => !removed.Contains(j)).ToArray();
        }

        public class knnNode : IComparable<knnNode> {
            public int Index;
            public double Value;

            public knnNode(double value, int index)
            {
                Value = value;
                Index = index;
            }

            public int CompareTo(knnNode y)
            {
                return Value > y.Value ? 1 : -1;
            }

            public override string ToString()
            {
                return Index + ": " + Value;
            }
        }
    }
}

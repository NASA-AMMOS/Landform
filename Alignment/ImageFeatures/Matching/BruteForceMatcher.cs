using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using log4net;
using OPS.Util;

namespace OPS.Alignment
{

    /// <summary>
    /// Given two images and a list of features in each and 
    /// returns a set of matches between them using nearest descriptor distance (L2Norm)
    /// </summary>
    public class BruteForceMatcher : IFeatureMatcher
    {
        const int K = 2;

        public BruteForceMatcher() { }

        public ImagePairCorrespondence Match(AlignmentScene scene, string modelUrl, string dataUrl)
        {
            var modelFeatures = scene.DetectedFeatures[modelUrl]; 
            var dataFeatures = scene.DetectedFeatures[dataUrl];
            return Match(modelFeatures, dataFeatures, modelUrl, dataUrl);
        }

        public ImagePairCorrespondence Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                             string modelUrl, string dataUrl)
        {
            var dataToModel = Match(modelFeatures, dataFeatures).ToArray();
            return new ImagePairCorrespondence(modelUrl, dataUrl, dataToModel);
        }
            
        public IEnumerable<KeyValuePair<int, int>> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures)
        {
            if (modelFeatures.Length < 1 || dataFeatures.Length < 1) yield break;

            SIFTFeature[] feat0 = modelFeatures.Cast<SIFTFeature>().ToArray();
            SIFTFeature[] feat1 = dataFeatures.Cast<SIFTFeature>().ToArray();

            knnNode[][] Matches = dataFeatures.Select((x, j) => new knnNode[2]).ToArray();

            // Match descriptors
            KnnMatch(feat0, feat1, Matches);
            
            Matrix<float> mask = Matrix<float>.Build.Dense(Matches.Length, 1);

            // OpenCV standard correspondence checks
            for (int idx = 0; idx < Matches.Length; idx++)
            {
                if (Matches[idx][0] == null)
                {
                    mask[idx, 0] = 0;
                }
                else if (Matches[idx][0].Value > Matches[idx][1].Value * 0.8)
                {
                    mask[idx, 0] = 0;
                }
                else
                {
                    mask[idx, 0] = 255;
                }
            }

            for (int idx = 0; idx < Matches.Length; idx++)
            {
                if (mask[idx, 0] != 0)
                {
                    var match = Matches[idx][0];
                    yield return new KeyValuePair<int, int>(idx, match.Index);
                }
            }
        }

        private void KnnMatch(SIFTFeature[] modelFeatures, SIFTFeature[] dataFeatures, knnNode[][] Matches)
        {
            double[][] dist = new double[modelFeatures.Length][].Select(x => new double[dataFeatures.Length]).ToArray();
            knnNode[][] knnModel = new knnNode[modelFeatures.Length][].Select(x => new knnNode[K]).ToArray();
            knnNode[][] knnData = new knnNode[dataFeatures.Length][].Select(x => new knnNode[K]).ToArray();

            FeatureDescriptor<byte>[] modelDescr =
                modelFeatures.Select(m => (FeatureDescriptor<byte>)m.Descriptor).ToArray();
            FeatureDescriptor<byte>[] dataDescr =
                dataFeatures.Select(m => (FeatureDescriptor<byte>)m.Descriptor).ToArray();

            int descriptorLength = modelDescr[0].Length;

            // Compute distance matrix
            CoreLimitedParallel.For(0, modelDescr.Length, i =>
            {
                for (int j = 0; j < dataDescr.Length; j++)
                {
                    double err = 0;
                    var d0 = modelDescr[i];
                    var d1 = dataDescr[j];
                    for (int k = 0; k < descriptorLength; k++)
                    {
                        double signedError = (d1[k] - d0[k]);
                        err += signedError * signedError;
                    }
                    dist[i][j] = err;
                }
            });

            var taskA = Task.Run(() =>
            {
                CoreLimitedParallel.For(0, dist.Length, i =>
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
                });
            });

            var taskB = Task.Run(() =>
            {
                CoreLimitedParallel.For(0, dist[0].Length, i =>
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
                });
            });

            taskA.Wait();
            taskB.Wait();
            for (int i = 0; i < knnData.Length; i++)
            {
                Matches[i] = new knnNode[] { knnData[i][0], knnData[i][1] };
            }
        }

        class knnNode {
            public int Index;
            public double Value;

            public knnNode(double value, int index)
            {
                Value = value;
                Index = index;
            }

            public override string ToString()
            {
                return Index + ": " + Value;
            }
        }
    }
}

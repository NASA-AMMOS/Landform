using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using log4net;
using OPS.Plumbing;

namespace OPS.Alignment
{

    /// <summary>
    /// Given two images and a list of features in each and 
    /// returns a set of matches between them using nearest descriptor distance (L2Norm)
    /// </summary>
    public class BruteForceMatcher : IFeatureMatcher
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(BruteForceMatcher));
        const int K = 2;

        public BruteForceMatcher()
        {
            
        }

        public ImagePairCorrespondence Match(AlignmentScene scene, UnorderedImagePair pair)
        {
            return Match(pair.One, pair.Two, scene.DetectedFeatures[pair.One], scene.DetectedFeatures[pair.Two]);
        }

        /// <summary>
        /// Compute correspondences between two images
        /// </summary>
        public ImagePairCorrespondence Match(ImageRef model, ImageRef data, 
            ImageFeature[] modelFeat, ImageFeature[] dataFeat)
        {
            if (modelFeat.Length < 1 || dataFeat.Length < 1) return ImagePairCorrespondence.Empty;

            SIFTFeature[] feat0 = modelFeat.Cast<SIFTFeature>().ToArray();
            SIFTFeature[] feat1 = dataFeat.Cast<SIFTFeature>().ToArray();

            knnNode[][] Matches = dataFeat.Select((x, j) => new knnNode[2]).ToArray();
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

            List<KeyValuePair<int, int>> dataToModel = new List<KeyValuePair<int, int>>();
            for (int idx = 0; idx < Matches.Length; idx++)
            {
                if (mask[idx, 0] != 0)
                {
                    var match = Matches[idx][0];
                    dataToModel.Add(new KeyValuePair<int, int>(idx, match.Index));
                }
            }

            logger.Info(string.Format("Model features: {0}, Data features: {1}, Matches: {2}", feat0.Length, feat1.Length, dataToModel.Count));
            return new ImagePairCorrespondence(model, data, dataToModel);
        }


        private void KnnMatch(SIFTFeature[] modelFeat, SIFTFeature[] dataFeat, knnNode[][] Matches)
        {
            double[][] dist = new double[modelFeat.Length][].Select(x => new double[dataFeat.Length]).ToArray();
            knnNode[][] knnModel = new knnNode[modelFeat.Length][].Select(x => new knnNode[K]).ToArray();
            knnNode[][] knnData = new knnNode[dataFeat.Length][].Select(x => new knnNode[K]).ToArray();

            FeatureDescriptor<byte>[] modelDescr = modelFeat.Select(m => (FeatureDescriptor<byte>)m.Descriptor).ToArray();
            FeatureDescriptor<byte>[] dataDescr = dataFeat.Select(m => (FeatureDescriptor<byte>)m.Descriptor).ToArray();

            int descriptorLength = modelDescr[0].Length;

            // Compute distance matrix
            Parallel.For(0, modelFeat.Length, i =>
            {
                SIFTFeature modelFeature = modelFeat[i];
                for (int j = 0; j < dataFeat.Length; j++)
                {
                    double err = 0;
                    var d0 = modelDescr[i];
                    var d1 = dataDescr[j];
                    for (int jerry = 0; jerry < descriptorLength; jerry++)
                    {
                        double signedError = (d1[jerry] - d0[jerry]);
                        err += signedError * signedError;
                    }
                    dist[i][j] = err;
                }
            });

            var taskA = Task.Run(() =>
            {
                Parallel.For(0, dist.Length, i =>
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
                Parallel.For(0, dist[0].Length, i =>
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

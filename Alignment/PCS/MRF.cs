using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Alignment
{
    public class MRF
    {
        int[] Features;
        SIFTFeature[] ModelFeatures;
        SIFTFeature[] DataFeatures;
        Dictionary<SIFTFeature, SIFTFeature> C;
        Dictionary<SIFTFeature, SIFTFeature[]> KNN;
        double Alpha = 0.5;
        int K = 5;

        public MRF(IEnumerable<SIFTFeature> modelFeat, IEnumerable<SIFTFeature> dataFeat)
        {
            Features = InitialSeedFeatures(modelFeat, dataFeat);
        }

        public MRF(ImagePairCorrespondence initialMatches)
        {
            ModelFeatures = (SIFTFeature[])initialMatches.ModelFeatures;
            DataFeatures = (SIFTFeature[])initialMatches.DataFeatures;
            Features = InitialSeedFeatures(ModelFeatures, DataFeatures);
        }

        public int[] InitialSeedFeatures(IEnumerable<SIFTFeature> modelFeat, IEnumerable<SIFTFeature> dataFeat, 
                                                float theta = 0.9f, int r = 100, int k = 5)
        {
            return modelFeat.Select((x, j) => new { Value = x, Index = j })
                                             .OrderBy(x => FeatureSimilarity(x.Value, dataFeat.ElementAt(x.Index)))
                                             .Select(x => x.Index)
                                             .Take(r)
                                             .ToArray();
        }

        public Matrix<double> TransformPatch(int index)
        {
            SIFTFeature feature = ModelFeatures[index];
            double sigma = feature.Size;
            double theta = feature.Angle;
            double x = feature.Location.X;
            double y = feature.Location.Y;
            Matrix<double> res = Matrix<double>.Build.Dense(3, 3);
            res.SetRow(0, new double[] { sigma * Math.Cos(theta), -sigma * Math.Sin(theta), x });
            res.SetRow(1, new double[] { sigma * Math.Sin(theta), sigma * Math.Cos(theta), y });
            res.SetRow(2, new double[] { 0, 0, 1 });

            return res;
        }

        /// <summary>
        /// Calculates similary between matched features in the correspondence, equivalent to E_phi(C).
        /// </summary>
        /// <param name="correspondence">Feature mapping from reference to target image.</param>
        /// <returns>Similarity of features.</returns>
        public double CorrespondenceSimilarity(Dictionary<SIFTFeature, SIFTFeature> correspondence)
        {
            double res = 0;

            foreach (SIFTFeature feature in correspondence.Keys)
            {
                res += FeatureSimilarity(feature);
            }

            return res;
        }

        /// <summary>
        /// Calculates similarity between a starting feature and its match, if it exists; equivalent to e_phi(c_i).
        /// </summary>
        /// <param name="startFeature"></param>
        /// <returns></returns>
        public double FeatureSimilarity(SIFTFeature startFeature, SIFTFeature endFeature)
        {
            double result = 0;
            float[] startDesc = ((PCASIFTDescriptor)startFeature.Descriptor).Data;
            float[] endDesc = ((PCASIFTDescriptor)endFeature.Descriptor).Data;

            for (int i = 0; i < startDesc.Length; i++)
            {
                result += Math.Pow(startDesc[i] - endDesc[i], 2);
            }

            return result;
        }

        /// <summary>
        /// Calculates similarity between a starting feature and its match, if it exists; equivalent to e_phi(c_i).
        /// </summary>
        /// <param name="startFeature"></param>
        /// <returns></returns>
        public double FeatureSimilarity(SIFTFeature startFeature)
        {
            SIFTFeature endFeature = C[startFeature];
            if (endFeature == null)
            {
                return Alpha;
            }

            double result = 0;
            float[] startDesc = ((PCASIFTDescriptor)startFeature.Descriptor).Data;
            float[] endDesc = ((PCASIFTDescriptor)endFeature.Descriptor).Data;

            for (int i = 0; i < startDesc.Length; i++)
            {
                result += Math.Pow(startDesc[i] - endDesc[i], 2);
            }

            return result;
        }

        public double PairwisePotential(Dictionary<SIFTFeature, SIFTFeature> correspondence)
        {
            double res = 0;

            foreach (SIFTFeature c_i in correspondence.Keys)
            {
                foreach (SIFTFeature c_j in KNN[c_i])
                {
                    res += PairwiseEnergy(c_i, c_j);
                }
            }

            return res;
        }

        public double PairwiseEnergy(ImageFeature c_i, ImageFeature c_j)
        {
            return OneWayTransferDistance(c_i, c_j) + OneWayTransferDistance(c_j, c_i);
        }

        public double OneWayTransferDistance(ImageFeature c, ImageFeature cPrime)
        {
            throw new NotImplementedException();
        }

        public class MRFCorrespondence
        {
            public int S;
            public int T;

            public MRFCorrespondence(int i, int j)
            {
                S = i;
                T = j;
            }

            public MRFCorrespondence Prime()
            {
                return new MRFCorrespondence(T, S);
            }
        }
    }
}

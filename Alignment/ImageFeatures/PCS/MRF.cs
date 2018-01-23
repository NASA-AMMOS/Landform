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
        Dictionary<MRFCorrespondence, MRFCorrespondence[]> KNN;
        Dictionary<int, Matrix<double>> TransformPatches;
        double Alpha = 0.5;
        int K = 5;
        const int ABSENT = -1;

        public MRF(IEnumerable<SIFTFeature> modelFeat, IEnumerable<SIFTFeature> dataFeat)
        {
            Features = InitialSeedFeatures(modelFeat, dataFeat);
            TransformPatches = new Dictionary<int, Matrix<double>>();
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

        /// <summary>
        /// Given a list of feature correspondences, calculates total pairwise potential due to forward
        ///     and backward feature warping.
        /// </summary>
        /// <param name="correspondences">List of correspondences.</param>
        /// <returns>Total pairwise potential.</returns>
        public double PairwisePotential(List<MRFCorrespondence> correspondences)
        {
            double res = 0;

            foreach (MRFCorrespondence c_i in correspondences)
            {
                foreach (MRFCorrespondence c_j in KNN[c_i])
                {
                    res += PairwiseEnergy(c_i, c_j);
                }
            }

            return res;
        }

        /// <summary>
        /// Calcultes pairwise energy between two correspondences by considering one-way transfer distance 
        ///     in both directions.
        /// </summary>
        /// <param name="c"></param>
        /// <param name="cPrime"></param>
        /// <returns></returns>
        public double PairwiseEnergy(MRFCorrespondence c, MRFCorrespondence cPrime)
        {
            return OneWayTransferDistance(c, cPrime) + OneWayTransferDistance(cPrime, c) +
                   OneWayTransferDistance(c.Inverse(), cPrime.Inverse()) + OneWayTransferDistance(cPrime.Inverse(), c.Inverse());
        }

        /// <summary>
        /// Calculates approximate distance between warped features. 
        /// </summary>
        /// <param name="cPrime">Correspondence to check.</param>
        /// <param name="c">Base correspondence.</param>
        /// <returns></returns>
        public double OneWayTransferDistance(MRFCorrespondence cPrime, MRFCorrespondence c)
        {
            if (c.T == ABSENT || cPrime.T == ABSENT)
            {
                return 0;
            }
            Matrix<double>  x_sPc = WarpedPosition(c, cPrime.S);
            SIFTFeature feat = DataFeatures[cPrime.T];
            Matrix<double> x_tP = Matrix<double>.Build.Dense(3, 1, new double[] { feat.Location.X, feat.Location.Y, 1 });
            Matrix<double> resMatrix = (x_sPc - x_tP);
            double res = 0;

            for (int row = 0; row < resMatrix.RowCount; row++)
            {
                for (int col = 0; col < resMatrix.ColumnCount; col++)
                {
                    res += resMatrix[row, col] * resMatrix[row, col];
                }
            }

            return res;
        }

        public Matrix<double> WarpedPosition(MRFCorrespondence c, int sP)
        {
            SIFTFeature feature = DataFeatures[sP];
            Matrix<double> location = Matrix<double>.Build.Dense(3, 1, new double[] { feature.Location.X, feature.Location.Y, 1 });
            return TransformPatches[c.T] * TransformPatches[c.S].Inverse() * location;
        }

        public class MRFCorrespondence
        {
            public int S;
            public int T;

            public MRFCorrespondence(int i, int j = ABSENT)
            {
                S = i;
                T = j;
            }

            public MRFCorrespondence Inverse()
            {
                return new MRFCorrespondence(T, S);
            }
        }
    }
}

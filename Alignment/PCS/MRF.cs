using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment.PCG
{
    public class MRF
    {
        PCSFeature[] Features;
        Dictionary<PCSFeature, PCSFeature> C;
        Dictionary<PCSFeature, PCSFeature[]> KNN;
        double Alpha = 0.5;
        int K = 5;

        public MRF(IEnumerable<ImageFeature> features)
        {
            Features = InitialSeedFeatures(features);
        }

        public PCSFeature[] InitialSeedFeatures(IEnumerable<ImageFeature> features, float theta = 0.9f, int r = 100, int k = 5)
        {
            return null;
        }

        public double[][] TransformPatch(int index)
        {
            return null;
        }

        /// <summary>
        /// Calculates similary between matched features in the correspondence, equivalent to E_phi(C).
        /// </summary>
        /// <param name="correspondence">Feature mapping from reference to target image.</param>
        /// <returns>Similarity of features.</returns>
        public double CorrespondenceSimilarity(Dictionary<PCSFeature, PCSFeature> correspondence)
        {
            double res = 0;

            foreach (PCSFeature feature in correspondence.Keys)
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
        public double FeatureSimilarity(PCSFeature startFeature)
        {
            PCSFeature endFeature = C[startFeature];
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

        public double PairwisePotential(Dictionary<PCSFeature, PCSFeature> correspondence)
        {
            double res = 0;

            foreach (PCSFeature c_i in correspondence.Keys)
            {
                foreach (PCSFeature c_j in KNN[c_i])
                {
                    res += PairwiseEnergy(c_i, c_j);
                }
            }

            return res;
        }

        public double PairwiseEnergy(PCSFeature c_i, PCSFeature c_j)
        {
            return OneWayTransferDistance(c_i, c_j) + OneWayTransferDistance(c_j, c_i);
        }

        public double OneWayTransferDistance(PCSFeature c, PCSFeature cPrime)
        {
            throw new NotImplementedException();
        }
    }
}

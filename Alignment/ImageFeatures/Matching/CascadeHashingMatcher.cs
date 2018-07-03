using MathNet.Numerics.LinearAlgebra;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment.ImageFeatures.Matching
{
    /// <summary>
    /// Matcher for SIFT keypoints using cascade hashing.
    /// 
    /// Based on the paper:
    /// Fast and Accurate Image Matching with Cascade Hashing for 3D Reconstruction
    /// Jian Cheng, Cong Leng, Jiaxiang Wu, Hainan Cui, Hanqing Lu. [CVPR 2014]
    /// </summary>
    public class CascadeHashingMatcher : IFeatureMatcher
    {
        public readonly int DescriptorSize;
        public readonly int PrimaryHashBits;
        public readonly int SecondaryHashBits;
        public readonly int BucketCount;
        public readonly int MinimumKnnCandidates;
        public readonly int MaximumKnnCandidates;
        public readonly double MaximumDistanceRatio;

        private readonly Matrix<float>[] projectionMatrices;
        private readonly Matrix<float> hammingProjectionMatrix;

        /// <summary>
        /// Construct an instance with the given parameters.
        /// </summary>
        /// <param name="descriptorSize">Number of elements in input descriptors</param>
        /// <param name="primaryHashBits">Number of bits to use in primary hash code</param>
        /// <param name="secondaryHashBits">Number of bits to use in each secondary hash</param>
        /// <param name="bucketCount">Number of secondary hashes</param>
        /// <param name="minCandidates">Minimum number of candidates in hamming distance KNN</param>
        /// <param name="maxCandidates">Maximum number of candidates in hamming distance KNN</param>
        /// <param name="maxRatio">Maximum distance ratio between best and second best match to accept match</param>
        public CascadeHashingMatcher(int descriptorSize=128, int primaryHashBits = 128, int secondaryHashBits = 8, int bucketCount=6, int minCandidates=6, int maxCandidates=10, double maxRatio=0.8)
        {
            DescriptorSize = descriptorSize;
            PrimaryHashBits = primaryHashBits;
            SecondaryHashBits = secondaryHashBits;
            BucketCount = bucketCount;
            MinimumKnnCandidates = minCandidates;
            MaximumKnnCandidates = maxCandidates;
            MaximumDistanceRatio = maxRatio;

            Random r = new Random();

            // Create projection matrices
            projectionMatrices = new Matrix<float>[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                projectionMatrices[i] = MakeProjection(DescriptorSize, SecondaryHashBits, r);
            }
            hammingProjectionMatrix = MakeProjection(DescriptorSize, PrimaryHashBits, r);
        }

        public ImagePairCorrespondence Match(AlignmentScene scene, UnorderedImagePair pair)
        {
            var model = pair.One;
            var data = pair.Two;
            var modelFeat = scene.Context.DetectedFeatures[model];
            var dataFeat = scene.Context.DetectedFeatures[data];
            var meanDescriptor = FeatureMean(scene, pair);

            List<HashCode> modelHashes = new List<HashCode>();
            Dictionary<HashCode, List<int>>[] modelSecondaryHashes = new Dictionary<HashCode, List<int>>[BucketCount];
            for (int hash = 0; hash < BucketCount; hash++)
            {
                var hashtable = modelSecondaryHashes[hash] = new Dictionary<HashCode, List<int>>();
            }

            // Hash all model features
            for (int i = 0; i < modelFeat.Length; i++)
            {
                var mc = GetMeanCentered(((FeatureDescriptor<byte>)modelFeat[i].Descriptor).Data, meanDescriptor);
                modelHashes[i] = Project(mc, hammingProjectionMatrix);

                for (int hash = 0; hash < BucketCount; hash++)
                {
                    var hashtable = modelSecondaryHashes[hash];
                    var hc = Project(mc, projectionMatrices[hash]);

                    if (!hashtable.ContainsKey(hc))
                    {
                        hashtable[hc] = new List<int>();
                    }
                    hashtable[hc].Add(i);
                }
            }

            List<KeyValuePair<int, int>> d2m = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < dataFeat.Length; i++)
            {
                var mc = GetMeanCentered(((FeatureDescriptor<byte>)dataFeat[i].Descriptor).Data, meanDescriptor);

                // Collect list of candidate features in model image
                HashSet<int> candidateMatches = new HashSet<int>();
                for (int hash = 0; hash < BucketCount; hash++)
                {
                    var hashtable = modelSecondaryHashes[hash];

                    var hc = Project(mc, projectionMatrices[hash]);
                    if (hashtable.ContainsKey(hc))
                    {
                        foreach (var c in hashtable[hc])
                        {
                            candidateMatches.Add(c);
                        }
                    }
                }

                var myHash = Project(mc, hammingProjectionMatrix);
                // Get KNN in hamming space
                KNNMatcher<HashCode>.Node[] knnHamming;
                {
                    KNNMatcher<HashCode> matcher = new KNNMatcher<HashCode>((c0, c1) => c0.HammingDistance(c1));
                    knnHamming = matcher.Find(myHash, modelHashes, MaximumKnnCandidates).ToArray();
                }
                if (knnHamming.Length < MinimumKnnCandidates)
                {
                    continue;
                }

                // Finally, get 2NN in euclidean space out of results
                KNNMatcher<ImageFeature>.Node[] nearest;
                {
                    KNNMatcher<ImageFeature> matcher = new KNNMatcher<ImageFeature>((f0, f1) =>
                    {
                        double res = 0;
                        var d0 = ((FeatureDescriptor<byte>)f0.Descriptor).Data;
                        var d1 = ((FeatureDescriptor<byte>)f1.Descriptor).Data;
                        for (int k = 0; k > d0.Length; k++)
                        {
                            var dist = d1[k] - d0[k];
                            res += dist * dist;
                        }
                        return res;
                    });
                    nearest = matcher.Find(dataFeat[i], knnHamming.Select(n => modelFeat[n.Index]).ToArray(), 2).ToArray();
                }
                if (nearest.Length < 2)
                {
                    continue;
                }

                if (nearest[0].Distance < nearest[0].Distance * MaximumDistanceRatio*MaximumDistanceRatio)
                {
                    d2m.Add(new KeyValuePair<int, int>(i, knnHamming[nearest[0].Index].Index));
                }
            }

            return new ImagePairCorrespondence(model, data, d2m);
        }


        private Vector<float> FeatureMean(AlignmentScene scene, UnorderedImagePair pair)
        {
            Vector<float> res = CreateVector.Dense(DescriptorSize, 0.0f);
            int count = 0;
            foreach (var imgRef in new[] { pair.One, pair.Two })
            {
                var feats = scene.Context.DetectedFeatures[imgRef];
                foreach (var feat in feats)
                {
#if DEBUG
                    if (feat.Descriptor.Length != DescriptorSize)
                    {
                        throw new Exception("Descriptor size mismatch");
                    }
#endif
                    byte[] data = ((FeatureDescriptor<byte>)feat.Descriptor).Data;
                    for (int i = 0; i < DescriptorSize; i++)
                    {
                        res[i] += data[i];
                    }
                    count++;
                }
            }
            return res / count;
        }

        private Vector<float> GetMeanCentered(byte[] descriptor, Vector<float> mean)
        {
            return CreateVector.DenseOfArray(descriptor.Cast<float>().ToArray()) - mean;
        }

        private HashCode Project(Vector<float> meanCentered, Matrix<float> mat)
        {
            var p = mat * meanCentered;
            HashCode res = new HashCode(p.Count);
            for (int i = 0; i < p.Count; i++)
            {
                int byteIdx = i / 8;
                int bitIdx = i % 8;
                if (p[i] > 0)
                {
                    res.Data[byteIdx] |= (byte)(1 << bitIdx);
                }
            }
            return res;
        }

        /// <summary>
        /// Sample a random number from the standard normal distribution
        /// </summary>
        /// <param name="r">Random number generator</param>
        private static double NormalRandom(Random r)
        {
            var u1 = r.NextDouble();
            var u2 = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        /// <summary>
        /// Make a matrix for projection into a random hamming space.
        /// </summary>
        /// <param name="fromDimension">Dimension of input vectors</param>
        /// <param name="toDimension">Dimension of hamming space</param>
        /// <param name="r">Random number generator</param>
        /// <returns>Matrix of size (to x from)</returns>
        private static Matrix<float> MakeProjection(int fromDimension, int toDimension, Random r)
        {
            var res = CreateMatrix.Dense<float>(toDimension, fromDimension);
            for (int i = 0; i < toDimension; i++)
            {
                for (int j = 0; j < fromDimension; j++)
                {
                    res[i, j] = (float)NormalRandom(r);
                }
            }
            return res;
        }

        private struct HashCode
        {
            public readonly byte[] Data;
            public readonly int BitCount;

            public HashCode(int bitCount, byte[] data = null)
            {
                if (data == null)
                {
                    data = new byte[bitCount];
                }
                Data = data;
                BitCount = bitCount;
            }

            public override bool Equals(object obj)
            {
                if (obj == null || obj.GetType() != GetType()) return false;
                return ((HashCode)obj).BitCount == BitCount
                    && Enumerable.SequenceEqual(((HashCode)obj).Data, Data);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (17 * 31) + BitCount.GetHashCode();
                    foreach (var b in Data)
                    {
                        hash = (hash * 31) + b.GetHashCode();
                    }
                    return hash;
                }
            }

            public int HammingDistance(HashCode code)
            {
                if (code.BitCount != BitCount)
                {
                    throw new InvalidOperationException("HammingDistance between different bit lengths");
                }
                int res = 0;

                var xor = new byte[Data.Length];
                for (int i = 0; i < Data.Length; i++)
                {
                    xor[i] = (byte)(Data[i] ^ code.Data[i]);
                }

                for (int i = 0; i < BitCount; i++)
                {
                    int byteIdx = i / 8;
                    int bitIdx = i % 8;
                    if ((xor[byteIdx] & (1 << bitIdx)) != 0)
                    {
                        res++;
                    }
                }
                return res;
            }
        }
    }

    public class KNNMatcher<T>
    {
        public struct Node
        {
            public int Index;
            public double Distance;
        }
        public readonly Func<T, T, double> Distance;

        public KNNMatcher(Func<T, T, double> distance)
        {
            Distance = distance;
        }

        public IEnumerable<Node> Find(T query, IList<T> candidates, int K)
        {
            Node[] res = new Node[K];
            for (int i = 0; i < K; i++)
            {
                res[i].Index = -1;
                res[i].Distance = double.PositiveInfinity;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var dist = Distance(query, candidates[i]);
                // skip any that suck
                if (dist > res[K - 1].Distance) continue;

                // binary search insertion point
                int insertPt = res.Select(n => n.Distance).ToList().BinarySearch(dist);
                if (insertPt < 0)
                {
                    // if < 0, result of BinarySearch is complement of first index of larger element
                    insertPt = ~insertPt;
                }
                if (insertPt >= K)
                {
                    // shouldn't ever happen, but better safe than sorry
                    continue;
                }

                // shift elements down
                for (int j = K - 1; j > insertPt; j--)
                {
                    res[j] = res[j - 1];
                }
                res[insertPt] = new Node()
                {
                    Index = i,
                    Distance = dist
                };
            }

            for (int i = 0; i < K; i++)
            {
                if (res[i].Index < 0)
                {
                    break;
                }
                yield return res[i];
            }
        }
    }
}

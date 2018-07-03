using MathNet.Numerics.LinearAlgebra;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
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

        private readonly LocalitySensitiveHash primaryHash;
        private LocalitySensitiveHash[] secondaryHashes;

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
        public CascadeHashingMatcher(int descriptorSize = 128, int primaryHashBits = 128, int secondaryHashBits = 8, int bucketCount = 6, int minCandidates = 6, int maxCandidates = 10, double maxRatio = 0.8)
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
            secondaryHashes = new LocalitySensitiveHash[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                secondaryHashes[i] = new LocalitySensitiveHash(DescriptorSize, SecondaryHashBits, r);
            }
            primaryHash = new LocalitySensitiveHash(DescriptorSize, PrimaryHashBits, r);
        }

        /// <summary>
        /// Set of hash codes computed for a feature descriptor
        /// </summary>
        struct DescriptorHashes
        {
            public HashCode PrimaryHash;
            public HashCode[] SecondaryHashes;
        }

        /// <summary>
        /// Compute all hashes for an image's features
        /// </summary>
        private DescriptorHashes[] ComputeHashes(AlignmentScene scene, ImageRef imgRef, Vector<float> featureMean)
        {
            var features = scene.DetectedFeatures[imgRef];
            DescriptorHashes[] res = new DescriptorHashes[features.Length];
            Parallel.For(0, features.Length, i =>
            {
                var mc = GetMeanCentered(features[i], featureMean);
                var primary = primaryHash.Project(mc);
                var secondary = new HashCode[BucketCount];
                for (int j = 0; j < BucketCount; j++)
                {
                    secondary[j] = secondaryHashes[j].Project(mc);
                }
                res[i] = new DescriptorHashes()
                {
                    PrimaryHash = primary,
                    SecondaryHashes = secondary
                };
            });
            return res;
        }

        public ImagePairCorrespondence Match(AlignmentScene scene, UnorderedImagePair pair)
        {
            var model = pair.One;
            var data = pair.Two;
            var modelFeat = scene.DetectedFeatures[model];
            var dataFeat = scene.DetectedFeatures[data];
            var meanDescriptor = FeatureMean(scene, pair);

            // Compute hashes
            DescriptorHashes[] modelHashes = ComputeHashes(scene, model, meanDescriptor);
            DescriptorHashes[] dataHashes = ComputeHashes(scene, data, meanDescriptor);

            // Put model features in buckets
            Dictionary<HashCode, List<int>>[] buckets = new Dictionary<HashCode, List<int>>[BucketCount];
            for (int i = 0; i < modelFeat.Length; i++)
            {
                var dh = modelHashes[i];
                for (int j = 0; j < BucketCount; j++)
                {
                    var bucket = buckets[j];
                    var hash = dh.SecondaryHashes[j];
                    if (!bucket.ContainsKey(hash))
                    {
                        bucket[hash] = new List<int>();
                    }
                    bucket[hash].Add(i);
                }
            }

            int[] d2m = new int[dataFeat.Length];
            Parallel.For(0, dataFeat.Length, i =>
            {
                d2m[i] = -1;

                var dh = dataHashes[i];

                // Collect list of candidate features in model image from secondary hash collisions
                List<int> candidateIndices;
                {
                    HashSet<int> candidateMatches = new HashSet<int>();
                    for (int hashIdx = 0; hashIdx < BucketCount; hashIdx++)
                    {
                        var bucket = buckets[hashIdx];
                        var hash = dh.SecondaryHashes[hashIdx];
                        if (bucket.ContainsKey(hash))
                        {
                            foreach (var c in bucket[hash])
                            {
                                candidateMatches.Add(c);
                            }
                        }
                    }
                    candidateIndices = new List<int>(candidateMatches);
                }

                // Get KNN in hamming space with primary hash
                KNNMatcher<HashCode>.Node[] knnHamming;
                {
                    KNNMatcher<HashCode> matcher = new KNNMatcher<HashCode>((c0, c1) => c0.HammingDistance(c1));
                    knnHamming = matcher.Find(dh.PrimaryHash, candidateIndices.Select(idx => modelHashes[idx].PrimaryHash).ToArray(), MaximumKnnCandidates).ToArray();
                }
                if (knnHamming.Length < MinimumKnnCandidates)
                {
                    return;
                }

                // Finally, get 2NN in euclidean space
                KNNMatcher<ImageFeature>.Node[] nearest;
                {
                    KNNMatcher<ImageFeature> matcher = new KNNMatcher<ImageFeature>((f0, f1) =>
                    {
                        double res = 0;
                        var d0 = ((FeatureDescriptor<byte>)f0.Descriptor).Data;
                        var d1 = ((FeatureDescriptor<byte>)f1.Descriptor).Data;
                        for (int k = 0; k < d0.Length; k++)
                        {
                            var dist = d1[k] - d0[k];
                            res += dist * dist;
                        }
                        return res;
                    });
                    nearest = matcher.Find(dataFeat[i], knnHamming.Select(n => modelFeat[candidateIndices[n.Index]]).ToArray(), 2).ToArray();
                }
                if (nearest.Length < 2)
                {
                    return;
                }

                if (nearest[0].Distance < nearest[1].Distance * MaximumDistanceRatio * MaximumDistanceRatio)
                {
                    // what a tangled web we weave
                    int indexInKnnHamming = nearest[0].Index;
                    int indexInCandidateIndices = knnHamming[indexInKnnHamming].Index;
                    int featureIndex = candidateIndices[indexInCandidateIndices];
                    d2m[i] = featureIndex;
                }
            });

            // Convert flat array to list of feature pairs
            List<KeyValuePair<int, int>> goodD2m = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < d2m.Length; i++)
            {
                if (d2m[i] < 0)
                {
                    continue;
                }
                goodD2m.Add(new KeyValuePair<int, int>(i, d2m[i]));
            }

            return new ImagePairCorrespondence(model, data, goodD2m);
        }

        /// <summary>
        /// Compute the mean descriptor value between both images in a pair.
        /// </summary>
        private Vector<float> FeatureMean(AlignmentScene scene, UnorderedImagePair pair)
        {
            Vector<float> res = CreateVector.Dense(DescriptorSize, 0.0f);
            int count = 0;
            foreach (var imgRef in new[] { pair.One, pair.Two })
            {
                var feats = scene.DetectedFeatures[imgRef];
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
            return CreateVector.DenseOfArray(descriptor.Select(b => (float)b).ToArray()) - mean;
        }
        
        private Vector<float> GetMeanCentered(ImageFeature feat, Vector<float> mean)
        {
            return GetMeanCentered(((FeatureDescriptor<byte>)feat.Descriptor).Data, mean);
        }

        /// <summary>
        /// Result of applying a locality sensitive hash.
        /// </summary>
        private struct HashCode
        {
            public readonly byte[] Data;
            public readonly int BitCount;

            public HashCode(int bitCount, byte[] data = null)
            {
                if (data == null)
                {
                    data = new byte[(int)Math.Ceiling(bitCount / 8.0)];
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

            /// <summary>
            /// Lookup table for number of bits set in a given byte.
            /// 
            /// This is much faster than looping over each bit at runtime. Ideally
            /// we want something that compiles down to a popcnt instruction but
            /// we don't get that in C#.
            /// </summary>
            static int[] ByteBitCount;
            static HashCode()
            {
                ByteBitCount = new int[256];
                for (int i = 0; i < 256; i++)
                {
                    byte b = (byte)i;
                    int cnt = 0;
                    for (int j = 0; j < 8; j++)
                    {
                        if ((b & (1 << j)) != 0)
                        {
                            cnt++;
                        }
                    }
                    ByteBitCount[i] = cnt;
                }
            }

            /// <summary>
            /// Return the number of bits that differ between this and another HashCode.
            /// </summary>
            /// <param name="code">HashCode with same BitCount</param>
            /// <returns>[0, BitCount]</returns>
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

                for (int i = 0; i < Data.Length; i++)
                {
                    res += ByteBitCount[xor[i]];
                }
                return res;
            }
        }
        
        private class LocalitySensitiveHash
        {
            public readonly Matrix<float> Projection;
            public LocalitySensitiveHash(int inputSize, int outputBits, Random r)
            {
                Projection = CreateMatrix.Dense(outputBits, inputSize, 0.0f);
                for (int i = 0; i < outputBits; i++)
                {
                    for (int j = 0; j < inputSize; j++)
                    {
                        Projection[i, j] = (float)NormalRandom(r);
                    }
                }
            }
            
            /// <summary>
            /// Project a mean-centered vector into a binary hash code.
            /// </summary>
            /// <param name="meanCentered">Mean-centered vector</param>
            /// <param name="mat">Hash projection matrix</param>
            /// <returns></returns>
            public HashCode Project(Vector<float> meanCentered)
            {
                var p = Projection * meanCentered;
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
            /// Sample a random number from the standard normal distribution.
            /// </summary>
            /// <param name="r">Random number generator</param>
            private static double NormalRandom(Random r)
            {
                var u1 = r.NextDouble();
                var u2 = r.NextDouble();
                return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }
        }
        
        /// <summary>
        /// Internal KNN matcher for doing KNN with arbitrary distance metrics
        /// </summary>
        private class KNNMatcher<T>
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
}

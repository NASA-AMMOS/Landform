using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using JPLOPS.Util;

namespace JPLOPS.ImageFeatures
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
        //maximum ratio between distance of nearest data feature descriptor to model feature descriptor
        //vs 2nd nearest data feature descriptor to the same model feature descriptor
        //set to 1 to disable filtering by this ratio
        public double MaxDistanceRatio = 0.9;

        //number of bits to use in primary hash code
        public int PrimaryHashBits = 128;

        //number of bits to use in each secondary hash
        public readonly int SecondaryHashBits = 8;

        //number of secondary hashes
        public readonly int BucketCount = 6;

        //minimum number of candidates in hamming distance KNN
        public readonly int MinimumKnnCandidates = 6;

        //maximum number of candidates in hamming distance KNN
        public readonly int MaximumKnnCandidates = 10;

        public IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures)
        {
            if (modelFeatures.Length < 1 || dataFeatures.Length < 1) yield break;

            Random rand = NumberHelper.MakeRandomGenerator();

            int descriptorSize = modelFeatures[0].Descriptor.Length;

            // Create projection matrices
            var primaryHash = new LocalitySensitiveHash(descriptorSize, PrimaryHashBits, rand);
            var secondaryHashes = new LocalitySensitiveHash[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                secondaryHashes[i] = new LocalitySensitiveHash(descriptorSize, SecondaryHashBits, rand);
            }

            int totalFeatures = 0;
            Vector<float> meanDescriptor = CreateVector.Dense(descriptorSize, 0.0f);
            foreach (var f in modelFeatures.Concat(dataFeatures))
            {
                for (int i = 0; i < descriptorSize; i++)
                {
                    meanDescriptor[i] += (float)f.Descriptor.GetElement(i);
                }
                totalFeatures++;
            }
            meanDescriptor = meanDescriptor / totalFeatures;

            var modelHashes = ComputeHashes(primaryHash, secondaryHashes, modelFeatures, meanDescriptor);
            var dataHashes = ComputeHashes(primaryHash, secondaryHashes, dataFeatures, meanDescriptor);

            // Put model features in buckets
            Dictionary<HashCode, List<int>>[] buckets = new Dictionary<HashCode, List<int>>[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                buckets[i] = new Dictionary<HashCode, List<int>>();
            }

            for (int i = 0; i < modelFeatures.Length; i++)
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

            var anyDescriptor = dataFeatures[0].Descriptor;
            double maxDistanceRatio = anyDescriptor.BestDistanceToFastDistance(MaxDistanceRatio);
            for (int i = 0; i < dataFeatures.Length; i++)
            {
                var dh = dataHashes[i];

                // Collect list of candidate features in model image from secondary hash collisions
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
                var candidateIndices = candidateMatches.ToArray();

                // Get KNN in hamming space with primary hash
                KNNMatcher<HashCode>.Node[] knnHamming;
                {
                    KNNMatcher<HashCode> matcher = new KNNMatcher<HashCode>((c0, c1) => c0.Distance(c1));
                    knnHamming = matcher.Find(dh.PrimaryHash,
                                              candidateIndices.Select(idx => modelHashes[idx].PrimaryHash).ToArray(),
                                              MaximumKnnCandidates).ToArray();
                }
                if (knnHamming.Length < MinimumKnnCandidates)
                {
                    continue;
                }

                // Finally, get 2NN in euclidean space
                KNNMatcher<ImageFeature>.Node[] nearest;
                {
                    KNNMatcher<ImageFeature> matcher =
                        new KNNMatcher<ImageFeature>((f0, f1) => f0.Descriptor.FastDistance(f1.Descriptor));
                    nearest = matcher.Find(dataFeatures[i],
                                           knnHamming.Select(n => modelFeatures[candidateIndices[n.Index]]).ToArray(),
                                           2).ToArray();
                }

                if (nearest.Length < 2)
                {
                    continue;
                }

                //keep match iff bestDist/2ndBestDist <= MaxDistanceRatio
                if (anyDescriptor.CheckFastDistanceRatio(nearest[0].Distance, nearest[1].Distance, maxDistanceRatio))
                {
                    // what a tangled web we weave
                    int indexInKnnHamming = nearest[0].Index;
                    int indexInCandidateIndices = knnHamming[indexInKnnHamming].Index;
                    int featureIndex = candidateIndices[indexInCandidateIndices];
                    yield return new FeatureMatch()
                    {
                        DataIndex = i,
                        ModelIndex = featureIndex,
                        DescriptorDistance = (float)anyDescriptor.FastDistanceToBestDistance(nearest[0].Distance)
                    };
                }
            }
        }

        /// <summary>
        /// Set of hash codes computed for a feature descriptor
        /// </summary>
        private struct DescriptorHashes
        {
            public HashCode PrimaryHash;
            public HashCode[] SecondaryHashes;
        }

        private DescriptorHashes[] ComputeHashes(LocalitySensitiveHash primaryHash,
                                                 LocalitySensitiveHash[] secondaryHashes,
                                                 ImageFeature[] features, Vector<float> featureMean)
        {
            DescriptorHashes[] res = new DescriptorHashes[features.Length];
            for (int i = 0; i < features.Length; i++)
            {
                FeatureDescriptor descriptor = features[i].Descriptor;
                Vector<float> meanCenteredDescriptor = CreateVector.Dense(descriptor.Length, 0.0f);
                for (int j = 0; j < descriptor.Length; j++)
                {
                    meanCenteredDescriptor[j] = ((float)descriptor.GetElement(j)) - featureMean[j];
                }

                var primary = primaryHash.Project(meanCenteredDescriptor);
                var secondary = new HashCode[BucketCount];
                for (int j = 0; j < BucketCount; j++)
                {
                    secondary[j] = secondaryHashes[j].Project(meanCenteredDescriptor);
                }
                res[i] = new DescriptorHashes()
                {
                    PrimaryHash = primary,
                    SecondaryHashes = secondary
                };
            }
            return res;
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
            /// Return the number of bits that differ between this and another HashCode.
            /// </summary>
            /// <param name="code">HashCode with same BitCount</param>
            /// <returns>[0, BitCount]</returns>
            public int Distance(HashCode code)
            {
                return HammingDistance.Distance(Data, code.Data);
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

            private readonly Func<T, T, double> distance;

            public KNNMatcher(Func<T, T, double> distance)
            {
                this.distance = distance;
            }

            public IEnumerable<Node> Find(T query, IList<T> candidates, int k)
            {
                Node[] res = new Node[k];
                for (int i = 0; i < k; i++)
                {
                    res[i].Index = -1;
                    res[i].Distance = double.PositiveInfinity;
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    var dist = distance(query, candidates[i]);

                    // skip any that suck
                    if (dist > res[k - 1].Distance) continue;

                    // binary search insertion point
                    int insertPt = res.Select(n => n.Distance).ToList().BinarySearch(dist);
                    if (insertPt < 0)
                    {
                        // if < 0, result of BinarySearch is complement of first index of larger element
                        insertPt = ~insertPt;
                    }

                    if (insertPt >= k)
                    {
                        // shouldn't ever happen, but better safe than sorry
                        continue;
                    }

                    // shift elements down
                    for (int j = k - 1; j > insertPt; j--)
                    {
                        res[j] = res[j - 1];
                    }

                    res[insertPt] = new Node()
                    {
                        Index = i,
                        Distance = dist
                    };
                }

                for (int i = 0; i < k; i++)
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

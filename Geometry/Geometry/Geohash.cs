using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Generalized geohash over N dimensions.
    /// </summary>
    public class Geohash
    {
        public readonly int Dimension;
        public readonly double[] MinValue;
        public readonly double[] MaxValue;

        public Geohash(double[] minValue, double[] maxValue)
        {
            if (minValue.Length != maxValue.Length)
            {
                throw new ArgumentException("minValue and maxValue must have the same size");
            }
            MinValue = minValue;
            MaxValue = maxValue;
            Dimension = MinValue.Length;
        }
        
        /// <summary>
        /// Internal helper for encoding geohashes
        /// </summary>
        struct EncodingState
        {
            public StringBuilder String;
            public double[] Min;
            public double[] Max;
            public readonly int Dimension;
            public bool Partial
            {
                get { return idx % Dimension != 0; }
            }

            int idx;
            int bits;

            public int Axis
            {
                get { return idx % Dimension; }
            }
            public double Midpoint
            {
                get { return (Min[Axis] + Max[Axis]) / 2; }
            }

            public EncodingState(double[] min, double[] max)
            {
                String = new StringBuilder();
                idx = 0;
                bits = 0;
                this.Min = (double[])min.Clone();
                this.Max = (double[])max.Clone();
                Dimension = min.Length;
            }

            public EncodingState(EncodingState other)
            {
                String = new StringBuilder(other.String.ToString());
                idx = other.idx;
                bits = other.bits;
                this.Min = (double[])other.Min.Clone();
                this.Max = (double[])other.Max.Clone();
                Dimension = other.Dimension;
            }

            public void AddBit(bool bit)
            {
                idx = (idx << 1) | (bit ? 1 : 0);
                if (bit)
                {
                    Min[Axis] = Midpoint;
                }
                else
                {
                    Max[Axis] = Midpoint;
                }

                bits++;
                if (bits % 5 == 0)
                {
                    String.Append(Base32Characters[idx]);
                    idx = 0;
                }
            }

            public EncodingState WithBit(bool bit)
            {
                var res = new EncodingState(this);
                res.AddBit(bit);
                return res;
            }
        }

        /// <summary>
        /// Encode a position to a given precision.
        /// </summary>
        /// <param name="position">ND position</param>
        /// <param name="precision">Number of characters</param>
        /// <returns></returns>
        public string Encode(double[] position, int precision)
        {
            EncodingState state = new EncodingState(MinValue, MaxValue);
            
            while (state.String.Length < precision)
            {
                state.AddBit(position[state.Axis] >= state.Midpoint);
            }
            return state.String.ToString();
        }

        public string Encode(Vector3 position, int precision)
        {
            return Encode(position.ToDoubleArray().Take(Dimension).ToArray(), precision);
        }

        /// <summary>
        /// Return a string prefix that will be present on all nodes within (min, max).
        /// </summary>
        public string ContainingPrefix(double[] min, double[] max, int maxPrecision)
        {
            EncodingState state = new EncodingState(MinValue, MaxValue);
            for (int i = 0; i < maxPrecision; i++)
            {
                if (min[state.Axis] > state.Midpoint)
                {
                    state.AddBit(true);
                }
                else if (max[state.Axis] < state.Midpoint)
                {
                    state.AddBit(false);
                }
                else
                {
                    break;
                }
            }
            return state.String.ToString();
        }

        public string ContainingPrefix(BoundingBox bounds, int maxPrecision)
        {
            if (Dimension > 3)
            {
                throw new ArgumentException("Cant use XNA bounds with dim>3");
            }
            return ContainingPrefix(bounds.Min.ToDoubleArray().Take(Dimension).ToArray(), bounds.Max.ToDoubleArray().Take(Dimension).ToArray(), maxPrecision);
        }

        IEnumerable<string> OverlappingInternal(
            EncodingState state,
            double[] min, double[] max, int maxPrecision)
        {
            while (state.String.Length < maxPrecision)
            {
                if (!state.Partial)
                {
                    // check if this prefix is wholly within passed bounds
                    bool contained = true;
                    for (int axis = 0; axis < Dimension; axis++)
                    {
                        if (state.Min[axis] < min[axis] || state.Max[axis] > max[axis])
                        {
                            contained = false;
                            break;
                        }
                    }

                    if (contained)
                    {
                        yield return state.String.ToString();
                        yield break;
                    }
                }


                if (max[state.Axis] < state.Midpoint)
                {
                    state.AddBit(false);
                }
                else if (min[state.Axis] > state.Midpoint)
                {
                    state.AddBit(true);
                }
                else
                {
                    // Box straddles the midpoint. Must consider both branches.
                    foreach (var s in OverlappingInternal(state.WithBit(false), min, max, maxPrecision))
                    {
                        yield return s;
                    }
                    foreach (var s in OverlappingInternal(state.WithBit(true), min, max, maxPrecision))
                    {
                        yield return s;
                    }
                    yield break;
                }
            }
            // Reached max precision.
            yield return state.String.ToString();
        }
        /// <summary>
        /// Return all geohashes that intersect a region.
        /// 
        /// Geohashes that lie entirely inside the region will not be subdivided.
        /// </summary>
        /// <param name="min">Region min</param>
        /// <param name="max">Region max</param>
        /// <param name="maxPrecision">Maximum number of characters in geohashes</param>
        /// <returns>IEnumerable of prefixes</returns>
        public IEnumerable<string> Overlapping(double[] min, double[] max, int maxPrecision)
        {
            foreach (var s in OverlappingInternal(new EncodingState(MinValue, MaxValue), min, max, maxPrecision))
            {
                yield return s;
            }
        }

        /// <summary>
        /// Decode a geohash to a bounding box.
        /// </summary>
        /// <param name="hash">Geohash string</param>
        /// <param name="min">Minimum point</param>
        /// <param name="max">Maximum point</param>
        public void Decode(string hash, out double[] min, out double[] max)
        {
            min = (double[])MinValue.Clone();
            max = (double[])MaxValue.Clone();

            int bitIndex = 0;
            for (int i = 0; i < hash.Length; i++)
            {
                var c = hash[i];
                int idx = Base32Characters.IndexOf(c);
                if (idx == -1) throw new ArgumentException("Invalid geohash");

                for (int n = 0; n < 5; n++)
                {
                    int axis = (bitIndex % Dimension);
                    var midpoint = (min[axis] + max[axis]) / 2;

                    bool bitN = (idx & 1) != 0;
                    if (bitN)
                    {
                        min[axis] = midpoint;
                    }
                    else
                    {
                        max[axis] = midpoint;
                    }

                    idx >>= 1;
                    bitIndex++;
                }
            }
        }

        public static readonly string Base32Characters = "0123456789bcdefghjkmnpqrstuvwxyz";
    }
}

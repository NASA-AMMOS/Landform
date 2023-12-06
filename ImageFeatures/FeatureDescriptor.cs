using System;
using Emgu.CV;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// Base class for all feature descriptor types.
    /// 
    /// All descriptors should have an array of numeric elements.
    /// 
    /// In the common case you should derive from <see cref="FeatureDescriptor{T}"/>,
    /// not this class.
    /// </summary>
    public abstract class FeatureDescriptor
    {
        /// <summary>
        /// The type of element in the descriptor array.
        /// </summary>
        public abstract Type ElementType { get; }

        /// <summary>
        /// Number of entries in the descriptor array.
        /// </summary>
        public abstract int Length { get; }

        public abstract double GetElement(int index);

        public virtual double L2DistanceSquared(FeatureDescriptor other)
        {
            if (other.Length != Length)
            {
                throw new ArgumentException("cannot compare feature descriptor of length " + other.Length +
                                            " to feature descriptor of length " + Length);
            }
            if (other.ElementType != ElementType)
            {
                throw new ArgumentException("cannot compare " + other.ElementType + " feature descriptor to " +
                                            ElementType + " feature descriptor");
            }
            double ret = 0;
            for (int i = 0; i < Length; i++)
            {
                double d = GetElement(i) - other.GetElement(i);
                ret += d * d;
            }
            return ret;
        }

        public virtual double FastDistance(FeatureDescriptor other)
        {
            return L2DistanceSquared(other);
        }

        public virtual double BestDistance(FeatureDescriptor other)
        {
            return FastDistanceToBestDistance(FastDistance(other));
        }

        public virtual double FastDistanceToBestDistance(double d)
        {
            return Math.Sqrt(d);
        }

        public virtual double BestDistanceToFastDistance(double d)
        {
            return d * d;
        }

        public virtual bool CheckFastDistanceRatio(double closestDist, double secondClosestDist, double maxRatio)
        {
            //keep match iff bestDist/2ndBestDist <= MaxDistanceRatio
            return closestDist <= secondClosestDist * maxRatio;
        }

        //convert M feature descriptors of N elements each into an EmguCV float matrix of M rows by N columns
        public static Matrix<float> ToDescriptorMatrix(ImageFeature[] features)
        {
            int m = features.Length;
            int n = features.Length > 0 ? features[0].Descriptor.Length : 0;
            Matrix<float> res = new Matrix<float>(m, n);
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    res.Data[i, j] = (float)features[i].Descriptor.GetElement(j);
                }
            }
            return res;
        }
    }

    /// <summary>
    /// Base class for feature descriptors with element type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Numeric type of descriptor elements</typeparam>
    public abstract class FeatureDescriptor<T> : FeatureDescriptor
        where T : struct
    {
        public override Type ElementType
        {
            get
            {
                return typeof(T);
            }
        }

        /// <summary>
        /// Array of descriptor elements.
        /// </summary>
        public T[] Data;
        public T this[int idx]
        {
            get
            {
                return Data[idx];
            }
            set
            {
                Data[idx] = value;
            }
        }
    }
}

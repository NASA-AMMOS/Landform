using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;

namespace OPS.ImageFeatures
{
    public class BRIEFDescriptor : FeatureDescriptor<byte>
    {
        public override int Length
        {
            get
            {
                return Data.Length;
            }
        }

        public override double GetElement(int index)
        {
            return Data[index];
        }

        public BRIEFDescriptor(byte[] data)
        {
            this.Data = data;
        }

        public override double FastDistance(FeatureDescriptor other)
        {
            return HammingDistance.Distance(Data, ((BRIEFDescriptor)other).Data);
        }

        public override double BestDistance(FeatureDescriptor other)
        {
            return HammingDistance.Distance(Data, ((BRIEFDescriptor)other).Data);
        }

        public override double FastDistanceToBestDistance(double d)
        {
            return d;
        }

        public override double BestDistanceToFastDistance(double d)
        {
            return d;
        }

        public override bool CheckFastDistanceRatio(double closestDist, double secondClosestDist, double maxRatio)
        {
            return (secondClosestDist - closestDist) >= (1 - maxRatio) * Data.Length;
        }

    }
}

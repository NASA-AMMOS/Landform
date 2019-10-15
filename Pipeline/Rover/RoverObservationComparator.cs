using System;
using System.Collections.Generic;
using System.Linq;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class RoverObservationComparator : IComparer<RoverObservation>
    {
        private string pointsType = ObservationType.Points.ToString(), rangeType = ObservationType.Range.ToString();
        private bool preferMSSSToOPGS, preferLinearToNonlinear;
        
        public RoverObservationComparator(bool preferMSSSToOPGS, bool preferLinearToNonlinear)
        {
            this.preferMSSSToOPGS = preferMSSSToOPGS;
            this.preferLinearToNonlinear = preferLinearToNonlinear;
        }
        
        public int Compare(RoverObservation a, RoverObservation b)
        {
            // Return should be:
            // negative if a is "better" than b
            // 0 if a and b are equivalently good
            // positive if a is "worse than" b
            
            // always prefer XYZ to RNG if both are available
            // https://github.jpl.nasa.gov/OnSight/Landform/issues/471
            if (a.ObservationType == pointsType && b.ObservationType == rangeType)
            {
                return -1;
            }
            if (a.ObservationType == rangeType && b.ObservationType == pointsType)
            {
                return 1;
            }
            
            // sort next by producer
            if (a.Producer == RoverProductProducer.MSSS && b.Producer == RoverProductProducer.OPGS)
            {
                return preferMSSSToOPGS ? -1 : 1;
            }
            if (a.Producer == RoverProductProducer.OPGS && b.Producer == RoverProductProducer.MSSS)
            {
                return preferMSSSToOPGS ? 1 : -1;
            }
            
            // sort next by linear-ness, prefer linear
            var linearA = a.IsLinear();
            var linearB = b.IsLinear();
            if (linearA && !linearB)
            {
                return preferLinearToNonlinear ? -1 : 1;
            }
            if (!linearA && linearB)
            {
                return preferLinearToNonlinear ? 1 : -1;
            }
            
            // finally sort by version, prefer higer versions
            // versions go numeric 1 to 9, A-Z, _ (opgs) and numeric 0 to 9, A-Z (msss)
            return (int)b.Version[0] - (int)a.Version[0];
        }
    }
}

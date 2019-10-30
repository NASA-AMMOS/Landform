using System;
using System.Collections.Generic;
using System.Linq;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class RoverObservationComparator : IComparer<RoverObservation>
    {
        private bool preferMSSSToOPGS;
        private bool preferLinearToNonlinear;
        private bool preferColorToGrayscale;
        
        public RoverObservationComparator(bool preferMSSSToOPGS, bool preferLinearToNonlinear, bool preferColor)
        {
            this.preferMSSSToOPGS = preferMSSSToOPGS;
            this.preferLinearToNonlinear = preferLinearToNonlinear;
            this.preferColorToGrayscale = preferColor;
        }
        
        public int Compare(RoverObservation a, RoverObservation b)
        {
            // Return should be:
            // negative if a is "better" than b
            // 0 if a and b are equivalently good
            // positive if a is "worse than" b
            
            // always prefer XYZ to RNG if both are available
            // https://github.jpl.nasa.gov/OnSight/Landform/issues/471
            if (a.ObservationType == RoverProductType.Points && b.ObservationType == RoverProductType.Range)
            {
                return -1;
            }
            if (a.ObservationType == RoverProductType.Range && b.ObservationType == RoverProductType.Points)
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
            
            //sort images by color
            if (a.ObservationType == RoverProductType.Image && b.ObservationType == RoverProductType.Image)
            {
                if (a.Bands > b.Bands)
                {
                    return preferColorToGrayscale ? -1 : 1;
                }
                else if (b.Bands > a.Bands)
                {
                    return preferColorToGrayscale ? 1 : -1;
                }
            }

            // sort next by linear-ness
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
            
            // finally sort by version, prefer higher versions
            return b.Version - a.Version;
        }
    }
}

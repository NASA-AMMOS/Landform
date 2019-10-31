using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class RoverObservationComparator : IComparer<RoverObservation>
    {
        private bool preferMSSSToOPGS, preferLinearToNonlinear, preferColorToGrayscale;
        
        public RoverObservationComparator(bool preferMSSS, bool preferLinear, bool preferColor)
        {
            this.preferMSSSToOPGS = preferMSSS;
            this.preferLinearToNonlinear = preferLinear;
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
                else if (a.Bands == 1 && b.Bands == 1)
                {
                    return RoverProduct.BandPreference(a.Color) - RoverProduct.BandPreference(b.Color);
                }
            }

            // sort next by linear-ness
            var linearA = a.IsLinear;
            var linearB = b.IsLinear;
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

        public IEnumerable<RoverObservation> SortRoverObservations(IEnumerable<Observation> observations,
                                                                   Func<RoverObservation, bool> filter = null)
        {
            return observations
                .Where(o => o is RoverObservation)
                .Cast<RoverObservation>()
                .Where(o => filter == null || filter(o))
                .OrderBy(o => o, this);
        }

        public RoverObservation GetBestRoverObservation(IEnumerable<Observation> observations,
                                                        params RoverProductType[] types)
        {
            return observations
                .Where(o => o is RoverObservation)
                .Cast<RoverObservation>()
                .Where(o => types.Length == 0 || types.Any(t => t == o.ObservationType))
                .OrderBy(o => o, this)
                .FirstOrDefault();
        }
    }
}

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

        public IEnumerable<RoverObservation> KeepBestRoverObservations(IEnumerable<Observation> observations,
                                                                       params RoverProductType[] types)
        {
            return KeepBestRoverObservations(observations, null, types);
        }

        public IEnumerable<RoverObservation> KeepBestRoverObservations(IEnumerable<Observation> observations,
                                                                       Func<RoverObservation, bool> filter,
                                                                       params RoverProductType[] types)
        {
            if (types.Length > 0)
            {
                return observations
                    .Where(obs => obs is RoverObservation)
                    .Cast<RoverObservation>()
                    .Where(o => types.Any(t => t == o.ObservationType))
                    .Where(o => filter == null || filter(o))
                    .GroupBy(o => o.FrameName)
                    .Select(group => group.OrderBy(o => o, this).First());
            }
            else
            {
                //no types specified, so filter each type separately

                //be careful to not mix linear and nonlinear products for each observation
                //matters (only) in the case that both mission.AllowLinear() = mission.AllowNonLinear() = true
                var linear = new Dictionary<string, bool>();
                void registerLinear(IEnumerable<RoverObservation> roverObservations)
                {
                    foreach (var obs in roverObservations)
                    {
                        if (!linear.ContainsKey(obs.FrameName))
                        {
                            linear[obs.FrameName] = obs.IsLinear;
                        }
                    }
                }
                bool linearFilter(RoverObservation obs)
                {
                    return !linear.ContainsKey(obs.FrameName) || linear[obs.FrameName] == obs.IsLinear;
                }

                //(only) in the case that both mission.AllowLinear() = mission.AllowNonLinear() = true
                //the order we process each type here matters
                //because the first type found for each observation will determine
                //whether we keep linear or nonlinear products for that observation

                //filter RNG and XYZ together so we can keep only the latter if both are available
                var xyz = KeepBestRoverObservations(observations, RoverProductType.Range, RoverProductType.Points);
                registerLinear(xyz);

                var img = KeepBestRoverObservations(observations, linearFilter, RoverProductType.Image);
                registerLinear(img);

                var msk = KeepBestRoverObservations(observations, linearFilter, RoverProductType.RoverMask);
                registerLinear(msk);

                var uvw = KeepBestRoverObservations(observations, linearFilter, RoverProductType.Normals);
                registerLinear(uvw);

                var err = KeepBestRoverObservations(observations, linearFilter, RoverProductType.RangeError);

                return xyz.Concat(img).Concat(msk).Concat(uvw).Concat(err);
            }
        }
    }
}

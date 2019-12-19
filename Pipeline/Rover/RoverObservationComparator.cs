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
        private RoverStereoEye preferEyeForGeometry;
        private Func<RoverObservation, RoverObservation, int> ext;
        
        public RoverObservationComparator(bool preferMSSS, bool preferLinear, bool preferColor,
                                          RoverStereoEye preferEyeForGeometry,
                                          Func<RoverObservation, RoverObservation, int> ext = null)
        {
            this.preferMSSSToOPGS = preferMSSS;
            this.preferLinearToNonlinear = preferLinear;
            this.preferColorToGrayscale = preferColor;
            this.preferEyeForGeometry = preferEyeForGeometry;
            this.ext = ext;
        }

        public RoverObservationComparator()
            : this(preferMSSS: false, preferLinear: true, preferColor: true, preferEyeForGeometry: RoverStereoEye.Left)
        {}

        public RoverObservationComparator(RoverObservationComparator other)
            : this(preferMSSS: other.preferMSSSToOPGS, preferLinear: other.preferLinearToNonlinear, 
                  preferColor: other.preferColorToGrayscale, preferEyeForGeometry: other.preferEyeForGeometry, ext: other.ext)
        { }

        public enum CompareCriteria
        {
            StereoFrameName,
            XYZ_RNG,
            Producer,
            Color_Grayscale,
            Linear_NonLinear,
            SameObsTypeAndProducer,
            StereoEye,
            SameCameraAndColor,
            ExtendedCompare,
            Version,
            Name
        };

        public int Compare(RoverObservation a, RoverObservation b, CompareCriteria finalCrit)
        {
            //this function can only make judgements about observations in the same frame
            //StereoFrameName abstracts Left/Right distinctions
            //allowing comparison between the two eyes for the same frame
            if (a.StereoFrameName != b.StereoFrameName)
            {
                return 0;
            }

            if(finalCrit == CompareCriteria.StereoFrameName)
            {
                return 0;
            }

            //always prefer XYZ to RNG if both are available
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/471
            if (a.ObservationType == RoverProductType.Points && b.ObservationType == RoverProductType.Range)
            {
                return -1;
            }
            if (a.ObservationType == RoverProductType.Range && b.ObservationType == RoverProductType.Points)
            {
                return 1;
            }

            if (finalCrit == CompareCriteria.XYZ_RNG)
            {
                return 0;
            }

            //sort next by producer
            if (a.Producer == RoverProductProducer.MSSS && b.Producer == RoverProductProducer.OPGS)
            {
                return preferMSSSToOPGS ? -1 : 1;
            }
            if (a.Producer == RoverProductProducer.OPGS && b.Producer == RoverProductProducer.MSSS)
            {
                return preferMSSSToOPGS ? 1 : -1;
            }

            if (finalCrit == CompareCriteria.Producer)
            {
                return 0;
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
                //else if (a.Bands == 1 && b.Bands == 1)
                //{
                //    return RoverProduct.BandPreference(a.Color) - RoverProduct.BandPreference(b.Color);
                //}
            }

            if (finalCrit == CompareCriteria.Color_Grayscale)
            {
                return 0;
            }

            //sort next by linear-ness
            bool linearA = a.IsLinear, linearB = b.IsLinear;
            if (linearA && !linearB)
            {
                return preferLinearToNonlinear ? -1 : 1;
            }
            if (!linearA && linearB)
            {
                return preferLinearToNonlinear ? 1 : -1;
            }

            if (finalCrit == CompareCriteria.Linear_NonLinear)
            {
                return 0;
            }

            //fine-grained comparisons from here down
            //but allow comparing between eyes, e.g. NavcamLeft to NavcamRight and colors
            if (a.ObservationType != b.ObservationType || a.Producer != b.Producer)
            {
                return 0;
            }

            if (finalCrit == CompareCriteria.SameObsTypeAndProducer)
            {
                return 0;
            }

            RoverStereoEye aEye = a.StereoEye, bEye = b.StereoEye;
            if (preferEyeForGeometry != RoverStereoEye.Any && aEye != bEye &&
                (aEye == preferEyeForGeometry || bEye == preferEyeForGeometry) &&
                RoverProduct.IsGeometry(a.ObservationType) && !RoverProduct.IsRaster(a.ObservationType) &&
                RoverProduct.IsGeometry(b.ObservationType) && !RoverProduct.IsRaster(b.ObservationType))
            {
                return aEye == preferEyeForGeometry ? -1 : 1;
            }

            if (finalCrit == CompareCriteria.StereoEye)
            {
                return 0;
            }

            //only compare same camera observations (e.g. NavcamLeft to NavcamLeft) from here down
            if (a.Camera != b.Camera || a.Color != b.Color)
            {
                return 0;
            }

            if (finalCrit == CompareCriteria.SameCameraAndColor)
            {
                return 0;
            }

            if (ext != null)
            {
                var ev = ext(a, b);
                if (ev != 0)
                {
                    return ev;
                }
            }

            if (finalCrit == CompareCriteria.ExtendedCompare)
            {
                return 0;
            }

            //prefer higher versions
            if (a.Version != b.Version)
            {
                return b.Version - a.Version;
            }

            if (finalCrit == CompareCriteria.Version)
            {
                return 0;
            }

            //at this point the observations are otherwise equivalent
            //revert to just a string comparison on their names
            //just so that results are stable and repeatable
            return a.Name.CompareTo(b.Name);

        }
        /// <summary>
        /// 0 if a and b are equivalently good
        /// negative if a is "better" than b
        /// positive if a is "worse than" b
        /// </summary>
        public int Compare(RoverObservation a, RoverObservation b)
        {
            return Compare(a, b, CompareCriteria.Name);
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

        public enum KeepLinearVariants { Best, Both };

        public IEnumerable<RoverObservation> KeepBestRoverObservations(IEnumerable<Observation> observations, KeepLinearVariants keepLinVariants,
                                                                       params RoverProductType[] types)
        {
            return KeepBestRoverObservations(observations, keepLinVariants, null, null, types);
        }

     

        public IEnumerable<RoverObservation> KeepBestRoverObservations(IEnumerable<Observation> observations,
                                                                       KeepLinearVariants keepLinVariants,
                                                                       ILogger logger,
                                                                       params RoverProductType[] types)
        {
            return KeepBestRoverObservations(observations, keepLinVariants, logger, null, types);
        }
        
        public IEnumerable<RoverObservation> KeepBestRoverObservations(IEnumerable<Observation> observations,
                                                                       KeepLinearVariants keepLinVariants,
                                                                       ILogger logger,
                                                                       Func<RoverObservation, bool> filter,
                                                                       params RoverProductType[] types)
        {

            if (types.Length > 0)
            {
                IEnumerable<RoverObservation> filterGroup(IEnumerable<RoverObservation> group)
                {
                    group = group.OrderBy(o => o, this);

                    if (logger != null && group.Count() > 1)
                    {
                        logger.LogVerbose("keeping only first of\n  {0}",
                                          String.Join("\n  ", group.Select(o => o.ToString())));
                    }

                    if (group.Count() > 1 ||                                                                      //have more than one image
                        keepLinVariants == KeepLinearVariants.Both ||                                             //want both flavors of linearity if appropriate
                        0 == Compare(group.ElementAt(0), group.ElementAt(1), CompareCriteria.Color_Grayscale) ||  //no higher priority difference than linearity between the first two images
                        0 != Compare(group.ElementAt(0), group.ElementAt(1), CompareCriteria.Linear_NonLinear))   //differ in linearity
                    {
                        return group.Take(2); 
                    }
                    else
                    {
                        return group.Take(1);
                    }
                }

                var grouped = observations
                    .Where(obs => obs is RoverObservation)
                    .Cast<RoverObservation>()
                    .Where(o => types.Any(t => t == o.ObservationType))
                    .Where(o => filter == null || filter(o))
                    .GroupBy(o => o.FrameName);

                List<RoverObservation> result = new List<RoverObservation>();
                foreach(var group in grouped)
                {
                    result.AddRange(filterGroup(group));
                }

                return result;
            }
            else if(keepLinVariants == KeepLinearVariants.Both)
            {
                // no types specified, so filter each type separately
                // keep best 1 or 2 by the logic above, rely on linear check at the time when the images are used to prevent mismatch
                var xyz = KeepBestRoverObservations(observations, keepLinVariants, logger, null, RoverProductType.Range, RoverProductType.Points);
                var img = KeepBestRoverObservations(observations, keepLinVariants, logger, null, RoverProductType.Image);
                var msk = KeepBestRoverObservations(observations, keepLinVariants, logger, null, RoverProductType.RoverMask);
                var uvw = KeepBestRoverObservations(observations, keepLinVariants, logger, null, RoverProductType.Normals);
                var err = KeepBestRoverObservations(observations, keepLinVariants, logger, null, RoverProductType.RangeError);
                return xyz.Concat(img).Concat(msk).Concat(uvw).Concat(err);
            }
            else
            {
                //is KeepLinearVariants.Best with no types specified, so filter each type separately
                // since returning single best images for each, keep linearity consistent

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
                var xyz = KeepBestRoverObservations(observations, keepLinVariants, logger, null,
                                                    RoverProductType.Range, RoverProductType.Points);
                registerLinear(xyz);

                var img = KeepBestRoverObservations(observations, keepLinVariants, logger, linearFilter, RoverProductType.Image);
                registerLinear(img);

                var msk = KeepBestRoverObservations(observations, keepLinVariants, logger, linearFilter, RoverProductType.RoverMask);
                registerLinear(msk);

                var uvw = KeepBestRoverObservations(observations, keepLinVariants, logger, linearFilter, RoverProductType.Normals);
                registerLinear(uvw);

                var err = KeepBestRoverObservations(observations, keepLinVariants, logger, linearFilter, RoverProductType.RangeError);

                return xyz.Concat(img).Concat(msk).Concat(uvw).Concat(err);
            }
        }

        /// <summary>
        /// Does a related job to KeepBestRoverObservations but operates on raw product IDs (or URLs).
        /// This is used during fetch to avoid downloading stuff that's just going to get skipped anyway.
        /// It's also used as a first pass for culling in ingest (KeepBestRoverObservations() is also used there).
        /// It's tricky to do this just based on the ID (no metadata available here).
        /// So this is not quite as powerful as KeepBestRoverObservations().
        /// But that's OK it's just intended to be a first pass.
        /// </summary>
        public static IEnumerable<string> FilterProductIdGroups(IEnumerable<string> products,
                                                                MissionSpecific mission = null)
        {
            IEnumerable<RoverProductId> filterRNG(IEnumerable<RoverProductId> ids)
            {
                bool hasXYZ = ids.Any(id => id.ProductType == RoverProductType.Points);
                return ids.Where(id => !hasXYZ || id.ProductType != RoverProductType.Range);
            }

            //IEnumerable<RoverProductId> filterGeometry(IEnumerable<RoverProductId> ids, bool preferLinearToNonlinear)
            //{
            //    bool hasLinear = ids.Any(id => id.Geometry == RoverProductGeometry.Linearized);
            //    bool hasNonlinear = ids.Any(id => id.Geometry == RoverProductGeometry.Raw);
            //    return ids.Where(id => !hasLinear || !hasNonlinear ||
            //                     (preferLinearToNonlinear && id.Geometry == RoverProductGeometry.Linearized) ||
            //                     (!preferLinearToNonlinear && id.Geometry == RoverProductGeometry.Raw));
            //}

            IEnumerable<RoverProductId> filterColor(IEnumerable<RoverProductId> ids, bool preferColorToGrayscale)
            {
                bool hasColor = ids.Any(id => id.Color == RoverProductColor.FullColor);
                bool hasGrayscale = ids.Any(id => RoverProduct.IsMonochrome(id.Color));
                var best = RoverProductColor.FullColor;
                if (hasGrayscale && (!hasColor || !preferColorToGrayscale))
                {
                    best = ids
                        .Select(id => id.Color)
                        .Where(color => RoverProduct.IsMonochrome(color))
                        .OrderBy(color => RoverProduct.BandPreference(color))
                        .FirstOrDefault();
                }
                return ids.Where(id => id.Color == best);
            }

            IEnumerable<RoverProductId> filterEye(IEnumerable<RoverProductId> ids, RoverStereoEye preferEyeForGeometry)
            {
                bool hasLeft = ids.Any(id => RoverStereoPair.IsStereoEye(id.Camera, RoverStereoEye.Left));
                bool hasRight = ids.Any(id => RoverStereoPair.IsStereoEye(id.Camera, RoverStereoEye.Right));
                return ids.Where(id => RoverProduct.IsRaster(id.ProductType) ||
                                 !RoverProduct.IsGeometry(id.ProductType) || !hasLeft || !hasRight ||
                                 RoverStereoPair.IsStereoEye(id.Camera, preferEyeForGeometry));
            }

            var idToProduct = new Dictionary<RoverProductId, string>();
            foreach (var product in products)
            {
                string idStr = StringHelper.GetLastUrlPathSegment(product, stripExtension: true);
                var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                if (id != null)
                {
                    idToProduct[id] = product;
                }
            }

            //filter each type of ID separately
            //this keeps us from comparing e.g. MSSS to OPGS ids
            //not that it wouldn't be nice if we could do that
            //but the code just doesn't support that
            //KeepBestRoverObservations() does consider producer
            foreach (var group in idToProduct.Keys.GroupBy(id => id.GetType()))
            {
                var filtered = group.ToList();

                //keep only latest version
                filtered = filtered
                    .GroupBy(id => id.GetPartialId(includeVersion: false))
                    .Select(ids => ids.OrderByDescending(id => id.Version).First())
                    .ToList();

                //skip RNG if XYZ is available
                filtered = filtered
                    .GroupBy(id => id.GetPartialId(mission, includeProductType: false, includeVariants: false))
                    .SelectMany(ids => filterRNG(ids))
                    .ToList();

                //if (mission != null && mission.AllowLinear() && mission.AllowNonlinear())
                //{
                //    bool preferLinearToNonlinear = mission.PreferLinearToNonlinear();
                //    filtered = filtered
                //        .GroupBy(id => id.GetPartialId(mission, includeGeometry: false, includeVariants: false))
                //        .SelectMany(ids => filterGeometry(ids, preferLinearToNonlinear))
                //        .ToList();
                //}

                //if both color and grayscale are available, keep the preferred one
                //also if multiple grayscale bands are available, keep the preferred one
                bool preferColorToGrayscale = mission != null ? mission.PreferColorToGrayscale() : true;
                filtered = filtered
                    .GroupBy(id => id.GetPartialId(mission, includeColorFilter: false, includeVariants: false))
                    .SelectMany(ids => filterColor(ids, preferColorToGrayscale))
                    .ToList();

                //keep preferred eye for geometry products
                var preferEyeForGeometry = mission != null ? mission.PreferEyeForGeometry() : RoverStereoEye.Any;
                if (preferEyeForGeometry != RoverStereoEye.Any)
                {
                    filtered = filtered
                        .GroupBy(id => RoverStereoPair.GetStereoCamera(id.Camera) +
                                 id.GetPartialId(mission, includeInstrument: false, includeColorFilter: false,
                                                 includeVariants: false))
                        .SelectMany(ids => filterEye(ids, preferEyeForGeometry))
                        .ToList();
                }

                if (mission != null)
                {
                    filtered = mission.FilterProductIdGroups(filtered).ToList();
                }

                foreach (var id in filtered)
                {
                    yield return idToProduct[id];
                }
            }

            yield break;
        }

        public void SetPreferLinearToNonlinear(bool preferLinear)
        {
            preferLinearToNonlinear = preferLinear;
        }
    }
}

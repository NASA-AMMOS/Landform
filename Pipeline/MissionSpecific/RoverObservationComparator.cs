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
        public enum CompareCriteria
        {
            StereoFrameName,
            XYZ_RNG,
            Producer,
            Color_Grayscale,
            SameObsTypeAndProducer,
            StereoEye,
            SameCameraAndColor,
            Linear_NonLinear,
            ExtendedCompare,
            Version,
            Name,
            None
        };

        public enum LinearVariants { Best, Both };

        public ILogger logger;

        private bool preferMSSSToOPGS, preferLinearGeometryProducts, preferLinearRasterProducts, preferColorToGrayscale;
        private RoverStereoEye preferEyeForGeometry;
        private Func<RoverObservation, RoverObservation, int> ext;
        private MissionSpecific mission;

        public RoverObservationComparator(bool preferMSSS, bool preferLinearGeometryProducts,
                                          bool preferLinearRasterProducts, bool preferColor,
                                          RoverStereoEye preferEyeForGeometry, MissionSpecific mission,
                                          Func<RoverObservation, RoverObservation, int> ext = null)
        {
            this.preferMSSSToOPGS = preferMSSS;
            this.preferLinearGeometryProducts = preferLinearGeometryProducts;
            this.preferLinearRasterProducts = preferLinearRasterProducts;
            this.preferColorToGrayscale = preferColor;
            this.preferEyeForGeometry = preferEyeForGeometry;
            this.mission = mission;
            this.ext = ext;
        }

        public RoverObservationComparator()
            : this(preferMSSS: false, preferLinearGeometryProducts: true, preferLinearRasterProducts: false,
                   preferColor: true, preferEyeForGeometry: RoverStereoEye.Left, mission: null)
        { }

        public RoverObservationComparator(RoverObservationComparator other)
            : this(preferMSSS: other.preferMSSSToOPGS, preferLinearGeometryProducts: other.preferLinearGeometryProducts,
                   preferLinearRasterProducts: other.preferLinearRasterProducts,
                   preferColor: other.preferColorToGrayscale, preferEyeForGeometry: other.preferEyeForGeometry,
                   mission: other.mission, ext: other.ext)
        { }


        public void SetPreferLinearGeometryProducts(bool preferLinear)
        {
            preferLinearGeometryProducts = preferLinear;
        }

        public void SetPreferLinearRasterProducts(bool preferLinear)
        {
            preferLinearRasterProducts = preferLinear;
        }

        /// <summary>
        /// 0 if a and b are equivalently good
        /// negative if a is "better" than b
        /// positive if a is "worse than" b
        /// </summary>
        public int Compare(RoverObservation a, RoverObservation b, out CompareCriteria reason,
                           params CompareCriteria[] exceptCrit)
        {
            //this function can only make judgements about observations in the same frame
            //StereoFrameName abstracts Left/Right distinctions
            //allowing comparison between the two eyes for the same frame
            if (!exceptCrit.Contains(CompareCriteria.StereoFrameName) && a.StereoFrameName != b.StereoFrameName)
            {
                reason = CompareCriteria.StereoFrameName;
                return 0;
            }

            //always prefer XYZ to RNG if both are available
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/471
            if (!exceptCrit.Contains(CompareCriteria.XYZ_RNG))
            {
                reason = CompareCriteria.XYZ_RNG;
                if (a.ObservationType == RoverProductType.Points && b.ObservationType == RoverProductType.Range)
                {
                    return -1;
                }
                if (a.ObservationType == RoverProductType.Range && b.ObservationType == RoverProductType.Points)
                {
                    return 1;
                }
            }

            //sort by producer
            if (!exceptCrit.Contains(CompareCriteria.Producer))
            {
                reason = CompareCriteria.Producer;
                if (a.Producer == RoverProductProducer.MSSS && b.Producer == RoverProductProducer.OPGS)
                {
                    return preferMSSSToOPGS ? -1 : 1;
                }
                if (a.Producer == RoverProductProducer.OPGS && b.Producer == RoverProductProducer.MSSS)
                {
                    return preferMSSSToOPGS ? 1 : -1;
                }
            }

            //sort images by color
            if (!exceptCrit.Contains(CompareCriteria.Color_Grayscale) &&
                a.ObservationType == RoverProductType.Image && 
                b.ObservationType == RoverProductType.Image)
            {
                reason = CompareCriteria.Color_Grayscale;
                if (a.Bands > b.Bands)
                {
                    return preferColorToGrayscale ? -1 : 1;
                }
                else if (b.Bands > a.Bands)
                {
                    return preferColorToGrayscale ? 1 : -1;
                }
                else if (a.Color != b.Color)
                {
                    return RoverProduct.BandPreference(a.Color) - RoverProduct.BandPreference(b.Color);
                }
            }

            //fine-grained comparisons from here down
            //but allow comparing between eyes, e.g. NavcamLeft to NavcamRight and colors
            if (!exceptCrit.Contains(CompareCriteria.SameObsTypeAndProducer) && 
                (a.ObservationType != b.ObservationType || a.Producer != b.Producer))
            {
                reason = CompareCriteria.SameObsTypeAndProducer;
                return 0;
            }

            RoverStereoEye aEye = a.StereoEye, bEye = b.StereoEye;
            if (!exceptCrit.Contains(CompareCriteria.StereoEye) &&
                preferEyeForGeometry != RoverStereoEye.Any && aEye != bEye &&
                (aEye == preferEyeForGeometry || bEye == preferEyeForGeometry) &&
                RoverProduct.IsGeometry(a.ObservationType) && !RoverProduct.IsRaster(a.ObservationType) &&
                RoverProduct.IsGeometry(b.ObservationType) && !RoverProduct.IsRaster(b.ObservationType))
            {
                reason = CompareCriteria.StereoEye;
                return aEye == preferEyeForGeometry ? -1 : 1;
            }

            //only compare same camera and color filter observations (e.g. NavcamLeft color to NavcamLeft color)
            //from here down
            if (!exceptCrit.Contains(CompareCriteria.SameCameraAndColor) && 
                (a.Camera != b.Camera || a.Color != b.Color))
            {
                reason = CompareCriteria.SameCameraAndColor;
                return 0;
            }

            //sort by linear-ness
            //note mask products are considered both geometry and raster
            if (!exceptCrit.Contains(CompareCriteria.Linear_NonLinear) && a.ObservationType == b.ObservationType &&
                (RoverProduct.IsGeometry(a.ObservationType) || RoverProduct.IsRaster(a.ObservationType)) &&
                (preferLinearGeometryProducts == preferLinearRasterProducts ||
                 !(RoverProduct.IsGeometry(a.ObservationType) && RoverProduct.IsRaster(a.ObservationType))))
            {
                reason = CompareCriteria.Linear_NonLinear;
                bool linearA = a.IsLinear, linearB = b.IsLinear;
                bool preferLinear = RoverProduct.IsGeometry(a.ObservationType)
                    ? preferLinearGeometryProducts : preferLinearRasterProducts;
                if (linearA && !linearB)
                {
                    return preferLinear ? -1 : 1;
                }
                if (!linearA && linearB)
                {
                    return preferLinear ? 1 : -1;
                }
            }

            if (!exceptCrit.Contains(CompareCriteria.ExtendedCompare) && ext != null)
            {
                var ev = ext(a, b);
                if (ev != 0)
                {
                    reason = CompareCriteria.ExtendedCompare;
                    return ev;
                }
            }

            //prefer higher versions
            if (!exceptCrit.Contains(CompareCriteria.Version) && a.Version != b.Version)
            {
                reason = CompareCriteria.Version;
                return b.Version - a.Version;
            }

            //at this point the observations are otherwise equivalent
            //revert to just a string comparison on their names
            //just so that results are stable and repeatable
            if (!exceptCrit.Contains(CompareCriteria.Name))
            {
                reason = CompareCriteria.Name;
                return a.Name.CompareTo(b.Name);
            }
           
            reason = CompareCriteria.None;
            return 0;
        }

        /// <summary>
        /// 0 if a and b are equivalently good
        /// negative if a is "better" than b
        /// positive if a is "worse than" b
        /// </summary>
        public int Compare(RoverObservation a, RoverObservation b)
        {
            var ret = Compare(a, b, out CompareCriteria reason);
            if (logger != null)
            {
                string rel = ret == 0 ? "=" : ret < 0 ? ">" : "<";
                logger.LogDebug("{0} {1} {2} because {3}", a.Name, rel, b.Name, reason);
            }
            return ret;
        }

        /// <summary>
        /// Discards any observations that aren't RoverObservations or that don't pass the filter (which is optional).
        /// Then sorts using Compare().
        /// </summary>
        public IEnumerable<RoverObservation> SortRoverObservations(IEnumerable<Observation> observations,
                                                                   Func<RoverObservation, bool> filter = null)
        {
            return observations
                .Where(o => o is RoverObservation)
                .Cast<RoverObservation>()
                .Where(o => filter == null || filter(o))
                .OrderBy(o => o, this);
        }

        /// <summary>
        /// Discards any observations that aren't RoverObservations or that don't pass the filter (which is optional).
        /// Iff types is nonempty then
        /// 1) discards any observations of other types
        /// 2) groups by frame name (typically camera name + RMC, see MissionSpecific.GetObservationFrameName()
        /// 3) sorts each group using Compare()
        /// 4) keeps best observation in group
        /// 5) if linVars=Both also keeps the best with opposite linearness in group, if any
        /// If types is empty then run separately on every RoverProductType, except run Points and Range together, and
        /// return concatenated results.
        /// </summary>
        public IEnumerable<RoverObservation> KeepBestRoverObservations(IEnumerable<Observation> observations,
                                                                       LinearVariants linVars,
                                                                       Func<RoverObservation, bool> filter,
                                                                       params RoverProductType[] types)
        {
            if (types.Length > 0)
            {
                IEnumerable<RoverObservation> filterGroup(IEnumerable<RoverObservation> group)
                {
                    int num = group.Count();
                    if (num < 2)
                    {
                        return group;
                    }

                    var best = group.OrderBy(o => o, this).First();
                    var keepers = new List<RoverObservation>() { best };

                    if (linVars == LinearVariants.Both)
                    {
                        //if there is an observation in the group that differs only from the best one in linearness
                        //(and also possibly in version, because all product types are versioned independently)
                        //then include the best one of those as well
                        var id = RoverProductId.Parse(best.Name, mission, throwOnFail: false);
                        string bestPartial =
                            id != null ? id.GetPartialId(mission, includeGeometry: false, includeVersion: false) : null;
                        var bestOtherLin = group
                            .Where(obs => {
                                    var i = RoverProductId.Parse(obs.Name, mission, throwOnFail: false);
                                    if (i == null)
                                    {
                                        return false;
                                    }
                                    return i.GetPartialId(includeGeometry: false, includeVersion: false) == bestPartial;
                                })
                            .Where(o => o.IsLinear != best.IsLinear)
                            .OrderBy(o => o, this)
                            .FirstOrDefault();
                        if (bestOtherLin != null)
                        {
                            keepers.Add(bestOtherLin);
                        }
                    }

                    if (logger != null && keepers.Count < num)
                    {
                        logger.LogVerbose("keeping best observation(s) {0} of {1}", 
                                          String.Join(", ", keepers.Select(o => o.Name)),
                                          String.Join(", ", group.Select(o => o.Name)));
                    }

                    return keepers;
                }

                return observations
                    .Where(obs => obs is RoverObservation)
                    .Cast<RoverObservation>()
                    .Where(o => types.Any(t => t == o.ObservationType))
                    .Where(o => filter == null || filter(o))
                    .GroupBy(o => o.FrameName)
                    .SelectMany(group => filterGroup(group));
            }
            else //no types given, so filter each type separately, except do range and points together
            {
                types = Enum.GetValues(typeof(RoverProductType)).Cast<RoverProductType>().ToArray(); //all types

                //extend the given filter (null ok) with a check that only allows matching linearness
                //among products for the same frame, iff linVars=Best
                //note: as of 9/2/20 no codepaths currently call this function with both types=empty and linVars=Best
                var linear = new Dictionary<string, bool>();
                void registerLinear(IEnumerable<RoverObservation> roverObservations)
                {
                    if (linVars == LinearVariants.Best)
                    {
                        foreach (var obs in roverObservations)
                        {
                            if (!linear.ContainsKey(obs.FrameName))
                            {
                                linear[obs.FrameName] = obs.IsLinear;
                            }
                        }
                    }
                }
                bool linFilt(RoverObservation obs)
                {
                    if (filter != null && !filter(obs))
                    {
                        return false;
                    }
                    return !linear.ContainsKey(obs.FrameName) || linear[obs.FrameName] == obs.IsLinear;
                }

                var pts = new HashSet<RoverProductType>(types);
                var ret = Enumerable.Empty<RoverObservation>();
                if (pts.Contains(RoverProductType.Range) && pts.Contains(RoverProductType.Points)) //typically true
                {
                    var filtered = KeepBestRoverObservations(observations, linVars, linFilt,
                                                             RoverProductType.Range, RoverProductType.Points);
                    registerLinear(filtered);
                    ret = ret.Concat(filtered);
                    pts.Remove(RoverProductType.Range);
                    pts.Remove(RoverProductType.Points);
                }
                foreach (var pt in pts)
                {
                    var filtered = KeepBestRoverObservations(observations, linVars, linFilt, pt);
                    registerLinear(filtered);
                    ret = ret.Concat(filtered);
                }
                return ret;
            }
        }

        /// <summary>
        /// KeepBestRoverObservations() with no additional filter.
        /// </summary>
        public IEnumerable<RoverObservation> KeepBestRoverObservations(IEnumerable<Observation> observations,
                                                                       LinearVariants linVars,
                                                                       params RoverProductType[] types)
        {
            return KeepBestRoverObservations(observations, linVars, null, types);
        }

        /// <summary>
        /// Does a related job to KeepBestRoverObservations but operates on raw product IDs (or URLs).
        /// This is used during fetch to avoid downloading stuff that's just going to get skipped anyway.
        /// It's also used as a first pass for culling in ingest (KeepBestRoverObservations() is also used there).
        /// And it's used in ProcessContextual to filter wedge product IDs.
        /// It's tricky to do this just based on the ID (no metadata available here).
        /// So this is not quite as powerful as KeepBestRoverObservations().
        /// But that's OK it's just intended to be a first pass.
        /// </summary>
        public static IEnumerable<string>
            FilterProductIdGroups(IEnumerable<string> products, MissionSpecific mission = null,
                                  LinearVariants linVars = LinearVariants.Best,
                                  Action<string> log = null, Func<string, bool> logFilter = null)
        {
            int maxProductsPerID = 0;
            var idToProducts = new Dictionary<RoverProductId, List<string>>();
            foreach (var product in products)
            {
                string idStr =
                    mission != null ? mission.GetProductIDString(product) :
                    StringHelper.GetLastUrlPathSegment(product, stripExtension: true);
                var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                if (id != null)
                {
                    if (!idToProducts.ContainsKey(id))
                    {
                        idToProducts[id] = new List<string>();
                    }
                    idToProducts[id].Add(product);
                    maxProductsPerID = Math.Max(maxProductsPerID, idToProducts[id].Count);
                }
            }

            if (maxProductsPerID > 1 && log != null)
            {
                log($"up to {maxProductsPerID} products associated with each product ID");
            }

            string idToFile(RoverProductId id)
            {
                return StringHelper.GetLastUrlPathSegment(idToProducts[id][0]);
            }

            foreach (var id in FilterProductIdGroups(idToProducts.Keys, mission, linVars, log, logFilter, idToFile))
            {
                foreach (var product in idToProducts[id])
                {
                    yield return product;
                }
            }
        }

        /// <summary>
        /// Implementation of FilterProductIdGroups(IEnumerable<string>, ...), operates directly on product IDs.
        /// </summary>
        public static IEnumerable<RoverProductId>
            FilterProductIdGroups(IEnumerable<RoverProductId> products, MissionSpecific mission = null,
                                  LinearVariants linVars = LinearVariants.Best,
                                  Action<string> log = null, Func<string, bool> logFilter = null,
                                  Func<RoverProductId, string> idToFile = null)
        {
            //given a set of ids that only differ in product type, variants, and version
            //check if there is an XYZ (pointcloud) product
            //if so, remove any RNG (range map) products
            IEnumerable<RoverProductId> filterRNG(IEnumerable<RoverProductId> ids)
            {
                bool hasPts = ids.Any(id => id.ProductType == RoverProductType.Points);
                return hasPts ? ids.Where(id => id.ProductType != RoverProductType.Range) : ids;
            }

            //given a set of ids that only differ in color filter, variants, and version
            //if both color and grayscale are available, keep the preferred one
            //also if multiple grayscale bands are available, keep the preferred one
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

            //check if id is strictly a geometry product, e.g. XYZ, RNG, UVW, RNE
            //note that masks are both raster and geometry products
            bool isGeom(RoverProductId id)
            {
                return RoverProduct.IsGeometry(id.ProductType) && !RoverProduct.IsRaster(id.ProductType);
            }

            //check if id is strictly a raster product, e.g. RAS
            //note that masks are both raster and geometry products
            bool isRaster(RoverProductId id)
            {
                return RoverProduct.IsRaster(id.ProductType) && !RoverProduct.IsGeometry(id.ProductType);
            }

            //check if id is strictly a mask product, e.g. MXY
            //note that masks are both raster and geometry products
            bool isMask(RoverProductId id)
            {
                return id.ProductType == RoverProductType.RoverMask;
            }

            bool isLin(RoverProductId id)
            {
                return id.Geometry == RoverProductGeometry.Linearized;
            }

            bool isRaw(RoverProductId id)
            {
                return id.Geometry == RoverProductGeometry.Raw;
            }

            bool checkLin(RoverProductId id, bool lin)
            {
                return lin ? isLin(id) : isRaw(id);
            }

            //given a set of ids that only differ in stereo eye, variants, and version
            //if the product type is strictly geometry
            //NOT and both stereo eyes are present https://github.jpl.nasa.gov/OnSight/Landform/issues/1160
            //and the preferred stereo eye is left or right
            //then remove products of the non-preferred eye
            IEnumerable<RoverProductId> filterEye(IEnumerable<RoverProductId> ids, RoverStereoEye preferEyeForGeometry)
            {
                var gids = ids.Where(id => isGeom(id)).ToList();
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/1160
                //bool hasLeft = gids.Any(id => RoverStereoPair.IsStereoEye(id.Camera, RoverStereoEye.Left));
                //bool hasRight = gids.Any(id => RoverStereoPair.IsStereoEye(id.Camera, RoverStereoEye.Right));
                //if (hasLeft && hasRight)
                //{
                    return gids
                        .Where(id => RoverStereoPair.IsStereoEye(id.Camera, preferEyeForGeometry))
                        .Concat(ids.Where(id => !isGeom(id)));
                //}
                //return ids;
            }

            //given a set of ids that only differ in product type, linearness, variants, and version
            //if both linearnesses present
            //then remove products of the non-preferred linearness
            IEnumerable<RoverProductId> filterLinear(IEnumerable<RoverProductId> ids)
            {
                if (!ids.Any(isLin) || !ids.Any(isRaw))
                {
                    return ids;
                }

                var gids = ids.Where(isGeom);
                bool linGeo = mission.PreferLinearGeometryProducts();
                if (gids.Any(isLin) && gids.Any(isRaw))
                {
                    gids = gids.Where(id => checkLin(id, linGeo));
                }

                var rids = ids.Where(isRaster);
                bool linRas = mission.PreferLinearRasterProducts();
                if (rids.Any(isLin) && rids.Any(isRaw))
                {
                    rids = rids.Where(id => checkLin(id, linRas));
                }

                var mids = ids.Where(isMask);
                if (gids.Count() > 0 && rids.Count() == 0)
                {
                    mids = mids.Where(id => checkLin(id, linGeo));
                }
                else if (rids.Count() > 0 && gids.Count() == 0)
                {
                    mids = mids.Where(id => checkLin(id, linRas));
                }
                else
                {
                    bool hasLin = gids.Any(isLin) || rids.Any(isLin);
                    bool hasRaw = gids.Any(isRaw) || rids.Any(isRaw);
                    if (hasLin && !hasRaw)
                    {
                        mids = mids.Where(isLin);
                    }
                    else if (hasRaw && !hasLin)
                    {
                        mids = mids.Where(isRaw);
                    }
                }

                return gids.Concat(rids).Concat(mids);
            }

            void spew(string what, List<RoverProductId> orig, List<RoverProductId> filtered)
            {
                Func<RoverProductId, string> fmt = idToFile != null ? idToFile : (id => id.FullId);
                if (log != null && filtered.Count() < orig.Count() &&
                    (logFilter == null || filtered.Any(id => logFilter(idToFile(id)))))
                {
                    log($"filtered {orig.Count}->{filtered.Count} products by {what}:");
                    var keepers = new HashSet<RoverProductId>(filtered);
                    foreach (var id in orig.OrderByDescending(id => id.FullId))
                    {
                        log($"  " + fmt(id) + (keepers.Contains(id) ? " *" : ""));
                    }
                }
            }

            //filter each type of ID separately
            //this keeps us from comparing e.g. MSSS to OPGS ids
            //but the code just doesn't support that
            //KeepBestRoverObservations() does consider producer
            var typeGroups = products.GroupBy(id => id.GetType());
            foreach (var typeGroup in typeGroups)
            {
                if (typeGroups.Count() > 1 && log != null)
                {
                    log($"filtering {typeGroup.Count()} {typeGroup.First().GetType()} product IDs");
                }

                foreach (var obsGroup in
                         typeGroup.GroupBy(id => id.GetPartialId(mission,
                                                                 includeProductType: false, includeGeometry: false,
                                                                 includeColorFilter: false, includeVariants: false,
                                                                 includeVersion: false, includeStereoEye: false,
                                                                 includeStereoPartner: false)))
                {
                    //obsGroup contains ids of
                    //* same type (e.g. MSSS vs OPGS)
                    //* same instrument (but any stereo eye)
                    //* same sequence number and timestamp(s)
                    //* same size (thumbnail vs regular)
                    //* same special processing
                    //but
                    //* all product types
                    //* all geometries (linearized, raw)
                    //* all color filters
                    //* all variants
                    //* all versions
                    //* all stereo eyes (left, right, mono)
                    //* all stereo partners
                    
                    var orig = obsGroup.ToList();
                    
                    //where multiple ids differ only in version, keep latest
                    //Note: every product type is independently versioned
                    var filtered = orig
                        .GroupBy(id => id.GetPartialId(includeVersion: false))
                        .Select(ids => ids.OrderByDescending(id => id.Version).First())
                        .ToList();
                    spew("version", orig, filtered);
                    orig = filtered;

                    //skip RNG if XYZ is available
                    filtered = orig
                        .GroupBy(id => id.GetPartialId(mission, includeProductType: false,
                                                       includeVariants: false, includeVersion: false))
                        .SelectMany(ids => filterRNG(ids))
                        .ToList();
                    spew("RNG->XYZ", orig, filtered);
                    orig = filtered;
                    
                    if (mission != null)
                    {
                        //if both color and grayscale are available, keep the preferred one
                        //also if multiple grayscale bands are available, keep the preferred one
                        filtered = orig
                            .GroupBy(id => id.GetPartialId(mission, includeColorFilter: false,
                                                           includeVariants: false, includeVersion: false))
                            .SelectMany(ids => filterColor(ids, mission.PreferColorToGrayscale()))
                            .ToList();
                        spew("color", orig, filtered);
                        orig = filtered;
                        
                        //keep preferred eye for geometry products
                        var preferEyeForGeometry = mission.PreferEyeForGeometry();
                        if (preferEyeForGeometry != RoverStereoEye.Any)
                        {
                            filtered = orig
                                .GroupBy(id => id.GetPartialId(mission, includeStereoEye: false,
                                                               includeVariants: false, includeVersion: false))
                                .SelectMany(ids => filterEye(ids, preferEyeForGeometry))
                                .ToList();
                            spew("eye", orig, filtered);
                            orig = filtered;
                        }
                        
                        //keep preferred linearness
                        if (linVars == LinearVariants.Best)
                        {
                            filtered = orig
                                .GroupBy(id => id.GetPartialId(mission, includeProductType: false,
                                                               includeGeometry: false,
                                                               includeVariants: false, includeVersion: false))
                                .SelectMany(ids => filterLinear(ids))
                                .ToList();
                            spew("linearity", orig, filtered);
                            orig = filtered;
                        }
                        
                        //apply any mission specific filtering (e.g. may handle variants)
                        filtered = mission.FilterProductIdGroups(orig, spew).ToList();
                        spew("mission", orig, filtered);
                        orig = filtered;
                    }

                    foreach (var id in filtered)
                    {
                        yield return id;
                    }
                }
            }
        }
    }
}

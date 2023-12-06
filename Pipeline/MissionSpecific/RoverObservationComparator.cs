using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JPLOPS.Util;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public class RoverObservationComparator : IComparer<RoverObservation>
    {
        public enum LinearVariants { Best, Both };

        private MissionSpecific mission;
        private ILogger logger;

        public RoverObservationComparator(MissionSpecific mission = null, ILogger logger = null)
        {
            this.mission = mission;
            this.logger = logger;
        }

        public struct CompareResult
        {
            public readonly int Code;
            public readonly string Reason;
            public CompareResult(int code, string reason)
            {
                this.Code = code;
                this.Reason = reason;
            }
        }

        /// <summary>
        /// 0 if a and b are equivalently good
        /// negative if a is "better" than b
        /// positive if a is "worse than" b
        /// </summary>
        public CompareResult Compare(RoverObservation a, RoverObservation b, params string[] exceptCrit)
        {
            CompareResult done(int code, string reason)
            {
                if (logger != null)
                {
                    logger.LogDebug("{0} {1} {2}: {3}", a.Name, code == 0 ? "=" : code < 0 ? ">" : "<", b.Name, reason);
                }
                return new CompareResult(code, reason);
            }

            //NOTE: the order of comparisons here should approximately match the order in FilterProductIDGroups()

            //this function can only make judgements about observations in the same frame
            //StereoFrameName abstracts Left/Right distinctions
            //allowing comparison between the two eyes for the same frame
            if (!exceptCrit.Contains("stereo_camera_frame") && a.StereoFrameName != b.StereoFrameName)
            {
                return done(0, "stereo_camera_frame");
            }

            //always prefer XYZ to RNG if both are available
            if (!exceptCrit.Contains("xyz_rng"))
            {
                if (a.ObservationType == RoverProductType.Points && b.ObservationType == RoverProductType.Range)
                {
                    return done(-1, "xyz_rng");
                }
                if (a.ObservationType == RoverProductType.Range && b.ObservationType == RoverProductType.Points)
                {
                    return done(1, "xyz_rng");
                }
            }

            //only compare observations of same type from here down
            //but allow comparing between eyes, sizes, and color filters
            if (!exceptCrit.Contains("obs_type") && (a.ObservationType != b.ObservationType))
            {
                return done(0, "obs_type");
            }

            //sort by producer, and only proceed with further criteria if producers match
            if (!exceptCrit.Contains("producer") && a.Producer != b.Producer)
            {
                if (mission != null)
                {
                    var apt = mission.GetAllowedProducers();
                    var diff = apt.FindIndex(p => p == b.Producer) - apt.FindIndex(p => p == a.Producer);
                    if (diff != 0)
                    {
                        return done(diff, "producer");
                    }
                }
                return done(0, "producer");
            }

            //sort by special processing
            if (!exceptCrit.Contains("special") && mission != null)
            {
                var apt = mission.GetAllowedProcessingTypes();
                var idA = RoverProductId.Parse(a.Name, mission, throwOnFail: false) as OPGSProductId;
                var idB = RoverProductId.Parse(b.Name, mission, throwOnFail: false) as OPGSProductId;
                if (idA != null && idB != null)
                {
                    var diff = apt.FindIndex(p => p == idB.Spec) - apt.FindIndex(p => p == idA.Spec);
                    if (diff != 0)
                    {
                        return done(diff, "special");
                    }
                }
            }

            //sort by color filter, and only compare same color filter image observations from here down
            if (!exceptCrit.Contains("color") &&
                a.ObservationType == RoverProductType.Image && b.ObservationType == RoverProductType.Image)
            {
                if (mission != null)
                {
                    bool preferColor = mission.PreferColorToGrayscale();
                    if (a.Bands > b.Bands)
                    {
                        return done(preferColor ? -1 : 1, "color");
                    }
                    else if (b.Bands > a.Bands)
                    {
                        return done(preferColor ? 1 : -1, "color");
                    }
                }
                if (a.Color != b.Color)
                {
                    return done(RoverProduct.BandPreference(a.Color) - RoverProduct.BandPreference(b.Color), "color");
                }
            }

            //sort by stereo eye for geometry products
            var preferEye = mission != null ? mission.PreferEyeForGeometry() : RoverStereoEye.Any;
            if (!exceptCrit.Contains("eye") && preferEye != RoverStereoEye.Any && a.StereoEye != b.StereoEye &&
                (a.StereoEye == preferEye || b.StereoEye == preferEye) &&
                RoverProduct.IsGeometry(a.ObservationType) && !RoverProduct.IsRaster(a.ObservationType) &&
                RoverProduct.IsGeometry(b.ObservationType) && !RoverProduct.IsRaster(b.ObservationType))
            {
                return done(a.StereoEye == preferEye ? -1 : 1, "eye");
            }

            //only compare same stereo eye observations from here down
            if (!exceptCrit.Contains("eye") && (a.Camera != b.Camera))
            {
                return done(0, "eye");
            }

            //sort by linear-ness
            //note mask products are considered both geometry and raster
            if (!exceptCrit.Contains("linearity") && a.ObservationType == b.ObservationType && mission != null &&
                (RoverProduct.IsGeometry(a.ObservationType) || RoverProduct.IsRaster(a.ObservationType)) &&
                (mission.PreferLinearGeometryProducts() == mission.PreferLinearRasterProducts() ||
                 !(RoverProduct.IsGeometry(a.ObservationType) && RoverProduct.IsRaster(a.ObservationType))))
            {
                bool linearA = a.IsLinear, linearB = b.IsLinear;
                bool preferLinear = RoverProduct.IsGeometry(a.ObservationType)
                    ? mission.PreferLinearGeometryProducts() : mission.PreferLinearRasterProducts();
                if (linearA && !linearB)
                {
                    return done(preferLinear ? -1 : 1, "linearity");
                }
                if (!linearA && linearB)
                {
                    return done(preferLinear ? 1 : -1, "linearity");
                }
            }

            //sort by size, and only compare same size observations from here down
            if (!exceptCrit.Contains("size"))
            {
                long aSize = a.Width * a.Height;
                long bSize = b.Width * b.Height;
                if (aSize != bSize)
                {
                    return done(Math.Sign(bSize - aSize), "size");
                }
            }

            //apply extended (e.g. mission specific) criteria
            if (!exceptCrit.Contains("mission") && mission != null)
            {
                var cr = mission.CompareRoverObservations(a, b, exceptCrit);
                if (cr.Code != 0)
                {
                    return cr;
                }
            }

            //prefer higher versions
            if (!exceptCrit.Contains("version") && a.Version != b.Version)
            {
                return done(b.Version - a.Version, "version");
            }

            //at this point the observations are otherwise equivalent
            //revert to just a string comparison on their names
            //just so that results are stable and repeatable
            //prefer last in dictionary order, e.g. to take later SCLK
            if (!exceptCrit.Contains("name"))
            {
                return done(b.Name.CompareTo(a.Name), "name");
            }

            return done(0, "none");
        }

        public int Compare(RoverObservation a, RoverObservation b)
        {
            return Compare(a, b, "none").Code;
        }

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

                    var ordered = group.OrderBy(o => o, this).ToList();
                    var best = ordered.First();
                    var keepers = new List<RoverObservation>() { best };

                    if (linVars == LinearVariants.Both)
                    {
                        //if there is an observation in the group that differs only from the best one in linearness
                        //(and also possibly in version, because all product types are versioned independently)
                        //then include the best one of those as well
                        var id = RoverProductId.Parse(best.Name, mission, throwOnFail: false);
                        string bestPartial =
                            id != null ? id.GetPartialId(mission, includeGeometry: false, includeVersion: false) : null;
                        var bestOtherLin = ordered
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

                    // keep all sizes of mask
                    if (best.ObservationType == RoverProductType.RoverMask)
                    {
                        foreach (var obs in ordered)
                        {
                            if (obs.ObservationType == RoverProductType.RoverMask)
                            {
                                var i = keepers.FindIndex(o => o.ObservationType == RoverProductType.RoverMask &&
                                                          o.Width == obs.Width && o.Height == obs.Height);
                                if (i < 0)
                                {
                                    keepers.Add(obs);
                                }
                                else if (Compare(obs, keepers[i], "size", "name").Code < 0)
                                {
                                    keepers[i] = obs;
                                }
                            }
                        }
                    }

                    if (logger != null && keepers.Count < num)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            sb.Append(ordered[i].Name);
                            if (i < ordered.Count - 1)
                            {
                                var res = Compare(ordered[i], ordered[i + 1], "none");
                                sb.Append(" ");
                                sb.Append(res.Code == 0 ? "=" : res.Code < 0 ? ">" : "<");
                                sb.Append(res.Reason.ToString());
                                sb.Append(" ");
                            }
                        }
                        logger.LogVerbose("keeping best observation(s) {0} of {1}", 
                                          String.Join(", ", keepers.Select(o => o.Name)), sb.ToString());
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
            FilterProductIDGroups(IEnumerable<string> products, MissionSpecific mission = null,
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

            foreach (var id in FilterProductIDGroups(idToProducts.Keys, mission, linVars, log, logFilter, idToFile))
            {
                foreach (var product in idToProducts[id])
                {
                    yield return product;
                }
            }
        }

        /// <summary>
        /// Implementation of FilterProductIDGroups(IEnumerable<string>, ...), operates directly on product IDs.
        /// </summary>
        public static IEnumerable<RoverProductId>
            FilterProductIDGroups(IEnumerable<RoverProductId> products, MissionSpecific mission = null,
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
            //and the preferred stereo eye is left or right
            //then remove products of the non-preferred eye
            IEnumerable<RoverProductId> filterEye(IEnumerable<RoverProductId> ids, RoverStereoEye preferEyeForGeometry)
            {
                var gids = ids.Where(id => isGeom(id)).ToList();
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

            //given a set of ids that only differ in special processing, variants, and version
            //for the subset that are OPGS product IDs (which should be all or none)
            //keep only the best special processing
            IEnumerable<RoverProductId> filterSpecialProcessing(IEnumerable<RoverProductId> ids,
                                                                List<string> allowedProcessingTypes)
            {
                var opgsIds = ids.Where(id => id is OPGSProductId).Cast<OPGSProductId>();
                var bestSpec = opgsIds.Select(id => id.Spec)
                    .DefaultIfEmpty("_")
                    .OrderBy(s => allowedProcessingTypes.FindIndex(t => t == s))
                    .Last();
                return opgsIds.Where(id => id.Spec == bestSpec).Concat(ids.Where(id => !(id is OPGSProductId)));
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

            //apply any mission specific filtering (e.g. may handle variants)
            //note, the case of "orphan" mask products with no matching geometry or image product
            //is handled in the mission specific FilterProductIDGroups()
            //because it's tricky: for missions like MSL that can work with a mix of products
            //from different producers (e.g. OPGS, MSSS)
            //and when those have different product ID formats (cough, MSL)
            //then it can be hard or impossible to associate e.g. OPGS mask products
            //with MSSS image products just based on the product ID alone
            //(later when RDRs have been downloaded and parsed we can typically use RMC
            //to actually group products across producers)
            if (mission != null)
            {
                var orig = products.ToList();
                var filtered = mission.FilterProductIDGroups(orig, spew).ToList();
                spew("mission", orig, filtered);
                products = filtered;
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
                                                                 includeSpecialProcessing: false,
                                                                 includeStereoPartner: false)))
                {
                    //obsGroup contains ids of
                    //* same type (e.g. MSSS vs OPGS)
                    //* same instrument (but any stereo eye)
                    //* same sequence number and timestamp(s)
                    //* same size (thumbnail vs regular)
                    //* same producer
                    //* same mesh type
                    //but
                    //* all product types
                    //* all geometries (linearized, raw)
                    //* all color filters
                    //* all variants
                    //* all versions
                    //* all stereo eyes (left, right, mono)
                    //* all stereo partners
                    //* all special processings
                    
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
                        filtered = orig
                            .GroupBy(id => id.GetPartialId(mission, includeVariants: false, includeVersion: false,
                                                           includeSpecialProcessing: false))
                            .SelectMany(ids => filterSpecialProcessing(ids, mission.GetAllowedProcessingTypes()))
                            .ToList();
                        spew("special processing", orig, filtered);
                        orig = filtered;

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

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.MathExtensions;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public class WedgeCommandOptions : LandformCommandOptions
    {
        [Option(HelpText = "Wedge mesh decimation blocksize, 0 to disable, -1 for auto", Default = -1)]
        public virtual int DecimateWedgeMeshes { get; set; }

        [Option(HelpText = "Wedge image decimation blocksize, 0 to disable, -1 for auto", Default = -1)]
        public virtual int DecimateWedgeImages { get; set; }

        [Option(HelpText = "Wedge mesh auto decimation target resolution", Default = 1024)]
        public virtual int TargetWedgeMeshResolution { get; set; }

        [Option(HelpText = "Wedge image auto decimation target resolution", Default = 1024)]
        public virtual int TargetWedgeImageResolution { get; set; }

        [Option(HelpText = "Mesh decimation method (EdgeCollapse, ResampleFSSR, ResamplePoisson, MeshLab, MeshLabResample)", Default = MeshDecimationMethod.EdgeCollapse)]
        public virtual MeshDecimationMethod MeshDecimator { get; set; }

        [Option(HelpText = "Only use specific surface observations, comma separated (e.g. MLF_452276219RASLS0311330MCAM02600M1)", Default = null)]
        public virtual string OnlyForObservations { get; set; }

        [Option(HelpText = "Only use specific surface frames, comma separated (e.g. MastcamLeft_00031013300028400454000060009001618010680001200000)", Default = null)]
        public virtual string OnlyForFrames { get; set; }

        [Option(HelpText = "Only use specific surface cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc)", Default = null)]
        public virtual string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations from specific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public virtual string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted, Manual, Landform, LandformBEV, LandformBEVRoot, LandformBEVCalf, Agisoft)", Default = null)]
        public virtual string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior, LegacyManifest, PlacesDB, LocationsDB, PlacesDBSitePDSLocal, PDSChained, PDS)", Default = null)]
        public virtual string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public virtual bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public virtual bool OnlyAligned { get; set; }

        [Option(HelpText = "Disable orbital", Default = false, Required = false)]
        public virtual bool NoOrbital { get; set; }

        [Option(HelpText = "Disable suface observations, only orbital", Default = false)]
        public virtual bool NoSurface { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital DEM file path")]
        public string OrbitalDEM { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital image file path")]
        public string OrbitalImage { get; set; }
    }

    public class WedgeCommand : LandformCommand
    {
        protected WedgeCommandOptions wcopts;

        protected SiteDrive[] siteDrives;
        protected TransformSource[] priorSources;
        protected TransformSource[] adjustedSources;

        protected FrameCache frameCache;
        protected ObservationCache observationCache;

        protected SiteDrive? rootSiteDrive;

        protected DEM orbitalDEM;
        protected Observation orbitalDEMObservation;
        protected Matrix orbitalDEMToRoot; //unprojected point from orbitalDEMObservation camera model -> root
        protected double orbitalDEMAvgMetersPerPixel
        {
            get
            {
                return (orbitalDEMObservation.CameraModel as ConformalCameraModel).AvgMetersPerPixel;
            }
        }

        protected WedgeCommand(WedgeCommandOptions wcopts) : base(wcopts)
        {
            this.wcopts = wcopts;
        }

        protected virtual bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (wcopts.NoOrbital && wcopts.NoSurface)
            {
                throw new Exception("cannot combine --noorbital with --nosurface");
            }

            if (wcopts.UsePriors && wcopts.OnlyAligned)
            {
                throw new Exception("cannot specify both --usepriors and --onlyaligned");
            }

            siteDrives = SiteDrive.ParseList(wcopts.OnlyForSiteDrives);
            priorSources = FrameTransform.ParseSources(wcopts.PriorTransformSources);
            adjustedSources = FrameTransform.ParseSources(wcopts.AdjustedTransformSources);

            if (outDir != null)
            {
                outDir = DecorateOutDir(outDir);
            }

            if (!base.ParseArguments(outDir))
            {
                return false; //help
            }

            if (project != null)
            {
                LoadFrameCache();
                LoadObservationCache();
            }

            return true;
        }

        protected virtual string DecorateOutDir(string outDir)
        {
            return FrameTransform.AppendSourcesPath(outDir, adjustedSources, priorSources, wcopts.UsePriors);
        }

        protected override bool ParseArguments(string outDir)
        {
            throw new NotImplementedException();
        }

        protected virtual void LoadFrameCache()
        {
            frameCache = new FrameCache(pipeline, project.Name);
            int num = frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, wcopts.UsePriors);
            pipeline.LogInfo("loaded {0} frames in project {1}", num, project.Name);
            rootSiteDrive = frameCache.CheckPriors(mission.GetLandingSiteDrive());
            if (!rootSiteDrive.HasValue)
            {
                pipeline.LogError("incomplete priors: not all sitedrives are connected");
            }
            else
            {
                pipeline.LogInfo("effective root frame for project: {0}", rootSiteDrive.Value);
            }
        }

        protected virtual bool ObservationFilter(RoverObservation obs)
        {
            return true;
        }

        protected virtual string DescribeObservationFilter()
        {
            return "";
        }

        protected virtual void LoadObservationCache()
        {
            var observations = StringHelper.ParseList(wcopts.OnlyForObservations);
            var frames = StringHelper.ParseList(wcopts.OnlyForFrames);
            var cams = RoverCamera.ParseList(wcopts.OnlyForCameras);

            observationCache = new ObservationCache(pipeline, project.Name);

            int num = observationCache.
                Preload(obs =>
                        (!wcopts.NoOrbital && obs.IsOrbital) ||
                        (!wcopts.NoSurface && (obs is RoverObservation) && ObservationFilter((RoverObservation)obs) &&
                         (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)) &&
                         (observations.Length == 0 || observations.Any(name => name == obs.Name)) &&
                         (frames.Length == 0 || frames.Any(name => name == obs.FrameName)) &&
                         (cams.Length == 0 || cams.Any(c => RoverCamera.IsCamera(c, ((RoverObservation)obs).Camera)))));

            //TODO
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/1037 
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/976
            //if (num == 0)
            //{
            //    throw new Exception("no surface or orbital data available");
            //}

            int numOrbital = wcopts.NoOrbital ? 0 : observationCache.GetAllObservations().Count(obs => obs.IsOrbital);
            int numSurface = num - numOrbital;

            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/1037 
            //wcopts.NoOrbital |= numOrbital == 0;
            wcopts.NoSurface |= numSurface == 0;

            pipeline.LogInfo("loaded {0}{1} surface observations{2} in project {3}{4}{5}",
                             numSurface, DescribeObservationFilter(),
                             numOrbital > 0 ? $" and {numOrbital} orbital observations" : "",
                             project.Name,
                             siteDrives.Length > 0 ? (" for sitedrives " + string.Join(", ", siteDrives)): "",
                             cams.Length > 0 ? (" for cameras " + string.Join(", ", cams)) : "");
        }

        protected void LoadOrbitalDEM(SiteDrive originSiteDrive)
        {
            try
            {
                var cfg = OrbitalConfig.Instance;
                var heightmap = LoadOrbitalAsset(originSiteDrive, Observation.ORBITAL_DEM_INDEX,
                                                 out orbitalDEMObservation, out orbitalDEMToRoot);
                orbitalDEM = new DEM(heightmap, cfg.DEMMetersPerPixel, cfg.DEMMinFilter, cfg.DEMMaxFilter);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("failed to load orbital DEM, running without orbital: {0}", ex.Message);
                wcopts.NoOrbital = true;
            }
        }

        /// <summary>
        /// Common implementation of LoadOrbitalDEM() and TextureCommand.LoadOrbitalImage().
        /// TODO #1037 move most of this to ingest
        /// </summary>
        protected Image LoadOrbitalAsset(SiteDrive originSiteDrive, int obsIndex,
                                         out Observation orbitalObservation, out Matrix orbitalToRoot)
        {
            orbitalObservation = null;
            orbitalToRoot = Matrix.Identity;

            int demIdx = Observation.ORBITAL_DEM_INDEX;
            int imgIdx = Observation.ORBITAL_IMAGE_INDEX;
            bool isDEM = obsIndex == demIdx;
            bool isImg = obsIndex == imgIdx;
            if (!isDEM && !isImg)
            {
                throw new Exception($"expected orbital observation index {demIdx} (DEM) or {imgIdx} (image), " +
                                    $"got {obsIndex}");
            }

            string what = isDEM ? "DEM" : "image";

            var sdName = originSiteDrive.ToString();
            var sdPriorToRoot = orbitalToRoot = frameCache.GetBestPrior(sdName).Transform.Mean;
            var sdAlignedToRoot = frameCache.GetBestTransform(sdName).Transform.Mean;
            var sdAlignedToPrior = sdAlignedToRoot * Matrix.Invert(sdPriorToRoot);

            var cfg = OrbitalConfig.Instance;

            string filePath = isDEM ? wcopts.OrbitalDEM : wcopts.OrbitalImage;
            string storagePath = isDEM ? cfg.DEMStoragePath : cfg.ImageStoragePath;
            if (string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(storagePath))
            {
                filePath = Path.Combine(LocalPipelineConfig.Instance.StorageDir, storagePath);
            }
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new Exception($"orbital {what} not found: {filePath}");
            }

            bool isGeoTIFF = isDEM ? cfg.DEMIsGeoTIFF : cfg.ImageIsGeoTIFF;
            bool isOrthographic = isDEM ? cfg.DEMIsOrthographic : cfg.ImageIsOrthographic;

            if (!isGeoTIFF && !isOrthographic)
            {
                throw new Exception($"{what} must use GeoTIFF metadata or OrthographicCameraModel (or both)");
            }

            //maybe load the GeoTIFF metadata
            var gisCam = isGeoTIFF ? new GISCameraModel(filePath, cfg.BodyName) : null;
            if (gisCam != null)
            {
                pipeline.LogInfo("loaded GeoTIFF metadata from {0}", filePath);
                gisCam.Dump(pipeline);
            }

            double nominalMPP = isDEM ? cfg.DEMMetersPerPixel : cfg.ImageMetersPerPixel;
            if (nominalMPP <= 0 && !isGeoTIFF)
            {
                throw new Exception($"{what} must use GeoTIFF metadata or have configured meters per pixel");
            }
            if (gisCam != null)
            {
                if (nominalMPP <= 0)
                {
                    nominalMPP = gisCam.AvgMetersPerPixel;
                }
                else if (Math.Abs(gisCam.AvgMetersPerPixel - nominalMPP) > 1e-3)
                {
                    throw new Exception(string.Format("expected {0:f3} meters per pixel orbital {1}, got {2:f3} in {3}",
                                                      nominalMPP, what, gisCam.AvgMetersPerPixel, filePath));
                }
            }

            //init PlacesDB and check metadata
            int placesIndex = isDEM ? cfg.DEMPlacesDBIndex : cfg.ImagePlacesDBIndex;
            if (placesIndex < 0)
            {
                throw new Exception($"no PlacesDB index for orbital {what}");
            }

            pipeline.LogInfo("using PlacesDB metadata at index {0} for orbital {1}", placesIndex, what);

            var placesDB = new PlacesDB(pipeline);

            //could pass filename = Path.GetFileName(filePath) here
            //but leaving it out will make the comparison vs the last path segment of OrbitalConfig.{DEM,Image}URL
            //which is probably a better idea
            //because perhaps the local copy of the asset was renamed (which is probably not a good idea...)
            Vector2? ulcEastingNorthing = gisCam != null ? gisCam.ULCEastingNorthing : (Vector2?)null;
            var variances = isDEM ? placesDB.CheckOrbitalDEMMetadata(placesIndex, nominalMPP, ulcEastingNorthing)
                : placesDB.CheckOrbitalImageMetadata(placesIndex, nominalMPP, ulcEastingNorthing);
            if (variances != null && variances.Length > 0)
            {
                string msg = $"PlacesDB metadata variances for orbital {what} at index {placesIndex}: " +
                    $"{string.Join(", ", variances)}";
                if (isDEM ? cfg.EnforceDEMPlacesDBMetadata : cfg.EnforceImagePlacesDBMetadata)
                {
                    throw new Exception(msg);
                }
                else
                {
                    pipeline.LogWarn(msg);
                }
            }

            //get sitedrive specifics
            var eastingNorthingElev = placesDB.GetEastingNorthingElevation(originSiteDrive, placesIndex);
            double sdElevation = eastingNorthingElev.Z;
            pipeline.LogInfo("site drive {0} (easting, northing, elevation) = " +
                             "({1:f3}, {2:f3}, {3:f3})m, source PlacesDB",
                             originSiteDrive, eastingNorthingElev.X, eastingNorthingElev.Y, eastingNorthingElev.Z);
                
            var sdLonLat = gisCam != null ? gisCam.EastingNorthingToLonLat(eastingNorthingElev).XY()
                : PlanetaryBody.GetByName(cfg.BodyName).EastingNorthingToLonLat(eastingNorthingElev).XY();
            pipeline.LogInfo("site drive {0} (lon, lat) = ({1:f7}, {2:f7})deg, source {3}",
                             originSiteDrive, sdLonLat.X, sdLonLat.Y, gisCam != null ? filePath : "PlacesDB");
            
            var sdPixel = gisCam != null ? gisCam.EastingNorthingToImage(eastingNorthingElev).XY()
                : placesDB.GetOrbitalPixel(originSiteDrive, placesIndex, nominalMPP);
            pipeline.LogInfo("site drive {0} pixel (x, y) = ({1:f3}, {2:f3})px, source {3}",
                             originSiteDrive, sdPixel.X, sdPixel.Y, gisCam != null ? filePath : "PlacesDB");

            var effectiveMPP = nominalMPP * Vector2.One;
            if (gisCam != null)
            {
                effectiveMPP = gisCam.CheckLocalGISImageBasisAndGetResolution(sdPixel, pipeline, throwOnError: true);
                pipeline.LogInfo("site drive {0} effective meters per pixel: ({1:f3}, {2:f3}), source {3}",
                                 originSiteDrive, effectiveMPP.X, effectiveMPP.Y, filePath);
            }

            //do some cross-checks if we have GeoTIFF metadata
            if (gisCam != null)
            {
                try
                {
                    var px = placesDB.GetOrbitalPixel(originSiteDrive, placesIndex, nominalMPP,
                                                      gisCam.ULCEastingNorthing);
                    pipeline.LogInfo("site drive {0} pixel (x, y) = ({1:f3}, {2:f3})px, source PlacesDB",
                                     originSiteDrive, px.X, px.Y);

                    if (Vector2.Distance(px, sdPixel) > 1)
                    {
                        pipeline.LogWarn("PlacesDB disagrees with {0} for sitedrive {1} pixel",
                                         filePath, originSiteDrive);
                    }

                    px = gisCam.LonLatToImage(sdLonLat);
                    pipeline.LogInfo("site drive {0} pixel (x, y) = ({1:f3}, {2:f3})px at " +
                                     "(lon, lat) = ({3:f7}, {4:f7})deg, source {5}",
                                     originSiteDrive, px.X, px.Y, sdLonLat.X, sdLonLat.Y, filePath);
                    
                    if (Vector2.Distance(px, sdPixel) > 1)
                    {
                        pipeline.LogWarn("easting/northing disagrees with lon/lat for sitedrive {0} pixel",
                                         originSiteDrive);
                    }

                    var gisULC = gisCam.ULCEastingNorthing;
                    pipeline.LogInfo("ULC (easting, northing) = ({0:f3}, {1:f3})m, source {2}",
                                     gisULC.X, gisULC.Y, filePath);

                    var placesULC = placesDB.GetULCEastingNorthing(placesIndex);
                    if (placesULC.HasValue)
                    {
                        pipeline.LogInfo("ULC (easting, northing) = ({0:f3}, {1:f3})m, source PlacesDB",
                                         placesULC.Value.X, placesULC.Value.Y);
                        if (Vector2.Distance(gisULC, placesULC.Value) > 1)
                        {
                            pipeline.LogWarn("PlacesDB disagrees with {0} for ULC (easting, northing)", filePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error checking site drive origin pixel or ULC (easting, northing): {0}",
                                     ex.Message);
                }
            }

            //load the asset
            Image asset = null;
            if (isDEM)
            {
                asset = isGeoTIFF ? new SparseGISElevationMap(filePath)
                    : Image.Load(filePath, ImageConverters.PassThrough);
            }
            else
            {
                asset = isGeoTIFF ? new SparseGISImage(filePath) : Image.Load(filePath);
            }
            pipeline.LogInfo("loaded {0}x{1} orbital {2} as {3}",
                             asset.Width, asset.Height, what, asset.GetType().Name);

            //prefer the elevation at sdOriginPixel in the DEM to the value we got from PlacesDB
            if (isDEM)
            {
                //GetInterpolatedElevation() does not need CameraModel
                //and intentionally not using cfg.DEMMinFilter and cfg.DEMMaxFilter here
                double? demElev = (new DEM(asset, cfg.DEMElevationScale)).GetInterpolatedElevation(sdPixel);
                if (demElev.HasValue)
                {
                    if (Math.Abs(demElev.Value - sdElevation) > 1e-3)
                    {
                        pipeline.LogWarn("using site drive {0} DEM elevation {1:f3}m from {2}, " +
                                         "differs from PlacesDB {3:f3}m",
                                         originSiteDrive, demElev.Value, filePath, sdElevation);
                        sdElevation = demElev.Value;
                    }
                    else
                    {
                        pipeline.LogInfo("site drive {0} DEM elevation: {1:f3}m, source {2}",
                                         originSiteDrive, demElev.Value, filePath);
                    }
                }
                else
                {
                    pipeline.LogWarn("failed to get elevation for site drive {0} at pixel ({1:f3}, {2:f3}) " +
                                     "in DEM {3}, using {4:f3}m from PlacesDB",
                                     originSiteDrive, sdPixel.X, sdPixel.Y, filePath, sdElevation);
                }
            }

            ConformalCameraModel cmod = gisCam;

            if (isOrthographic) //create ortho camera model
            {
                mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevation,
                                                                out Vector3 right, out Vector3 down);

                Vector2 centerPixel = new Vector2(asset.Width - 1, asset.Height - 1) * 0.5;
            
                Vector2 sdToCenter = centerPixel - sdPixel;

                right *= effectiveMPP.X;
                down *= effectiveMPP.Y;

                //XY location in meters of center pixel
                Vector3 centerPixelInSiteDrive = sdToCenter.X * right + sdToCenter.Y * down;

                //move image plane down by sdElevation
                //so that absolute DEM elevations unproject to elevations relative to sitedrive
                Vector3 elevationOffset = -1 * sdElevation * elevation;

                cmod = new OrthographicCameraModel(centerPixelInSiteDrive + elevationOffset, elevation, right, down,
                                                   asset.Width, asset.Height);
            }
            else //finish configuring GIS camera model
            {
                mission.GetLocalLevelBasis(out Vector3 north, out Vector3 east, out Vector3 nadir);
                var sdPriorToBody = gisCam.GetLocalLevelToBodyTransform(sdPixel, north, east, nadir, sdElevation);
                gisCam.LocalToBody = sdAlignedToPrior * sdPriorToBody;
            }

            asset.CameraModel = cmod;

            var originPixel = cmod.Project(Vector3.Zero);

            pipeline.LogInfo("using {0}x{1} {2} for orbital {3} at site drive {4}, pixel ({5:f3}, {6:f3}), " +
                             "({7:f3}, {8:f3}) meters per pixel, source {9}PlacesDB",
                             cmod.Width, cmod.Height, cmod.GetType().Name, what, originSiteDrive,
                             originPixel.X, originPixel.Y, cmod.MetersPerPixel.X, cmod.MetersPerPixel.Y,
                             gisCam != null ? $"{filePath} and " : "");

            orbitalObservation =
                Observation.Create(pipeline, frameCache.GetFrame(sdName),
                                   "Orbital" + StringHelper.UppercaseFirst(what),
                                   StringHelper.NormalizeUrl(Path.GetFullPath(filePath), "file://"),
                                   cmod, useForAlignment: isDEM, useForMeshing: isDEM, useForTexturing: isImg,
                                   width: asset.Width, height: asset.Height, bands: asset.Bands,
                                   bits: gisCam != null ? gisCam.Bits : isDEM ? 32 : 8,
                                   day: 0, version: 0, index: obsIndex, save: false);
            
            return asset;
        }
    }
}

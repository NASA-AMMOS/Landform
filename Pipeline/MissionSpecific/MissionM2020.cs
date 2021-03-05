using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class MissionM2020Config : SingletonConfig<MissionM2020Config>
    {
        public const string CONFIG_FILENAME = "mission-m2020"; //config file will be ~/.landform/mission-m2020.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }

        [ConfigEnvironmentVariable("LANDFORM_PREFER_LINEAR_GEOMETRY_PRODUCTS")]
        public bool PreferLinearGeometryProducts { get; set; } = false;

        //CSSO credentials uername parameter in SSM, {venue} will be replaced
        [ConfigEnvironmentVariable("LANDFORM_CSSO_USERNAME_PARAMETER_IN_SSM")]
        public string CSSOUsernameParameterInSSM { get; set; } = "/m20/{venue}/ids/pipeline/csso_username";

        //whether CSSO credentials username parameter in SSM is encrypted
        [ConfigEnvironmentVariable("LANDFORM_CSSO_USERNAME_PARAMETER_IN_SSM_ENCRYPTED")]
        public bool CSSOUsernameParameterInSSMEncrypted { get; set; } = true;

        //CSSO credentials password parameter in SSM, {venue} will be replaced
        [ConfigEnvironmentVariable("LANDFORM_CSSO_PASSWORD_PARAMETER_IN_SSM")]
        public string CSSOPasswordParameterInSSM { get; set; } = "/m20/{venue}/ids/pipeline/csso_password";

        //whether CSSO credentials password parameter in SSM is encrypted
        [ConfigEnvironmentVariable("LANDFORM_CSSO_PASSWORD_PARAMETER_IN_SSM_ENCRYPTED")]
        public bool CSSOPasswordParameterInSSMEncrypted { get; set; } = true;

        //{venue} will be replaced with mission venue
        [ConfigEnvironmentVariable("LANDFORM_S3_DATA_PROXY")]
        public string S3Proxy { get; set; } = "https://data.{venue}.m20.jpl.nasa.gov";

        //comma separated list of processing types to allow
        //sorted in order of preference (best last)
        //https://wiki.jpl.nasa.gov/pages/viewpage.action?spaceKey=MSMFS&title=Special+Character+Flags
        [ConfigEnvironmentVariable("LANDFORM_ALLOWED_PROCESSING_TYPES")]
        public string AllowedProcessingTypes { get; set; } = "_,C"; 

        //comma separated list of producers to allow
        //must match RoverProductProducer enum values
        //sorted in order of preference (best last)
        [ConfigEnvironmentVariable("LANDFORM_ALLOWED_PRODUCERS")]
        public string AllowedProducers { get; set; } = "OPGS";  //"OPGS,ASU"
    }
    
    public class MissionM2020 : MissionSpecific
    {
        public const int EECAM_DOWNSAMPLE_FIELD = 46;
        public const int EECAM_RECONSTRUCTION_FIELD = 47;
        public const int DOWNSAMPLE_FIELD = 48;
        public const int COMPRESSION_FIELD = 49;
        public const int COMPRESSION_FIELD_LENGTH = 2;
        public const int VERSION_FIELD = 52;
        public const int VERSION_FIELD_LENGTH = 2;

        public MissionM2020(string venue = null) : base(venue) { }

        public override Mission GetMission()
        {
            return Mission.M2020;
        }

        public override string RefreshCredentials(string awsProfile = null, string awsRegion = null, bool quiet = true,
                                                  bool dryRun = false, bool throwOnFail = false, ILogger logger = null)
        {
            void error(string msg)
            {
                if (throwOnFail)
                {
                    throw new Exception(msg);
                }
                else if (logger != null)
                {
                    logger.LogError(msg);
                }
            }

            int duration = 8 * 60 * 60; //8h
            string section = "credss-app";

            awsProfile = awsProfile ?? GetDefaultAWSProfile();
            awsRegion = awsRegion ?? GetDefaultAWSRegion();

            string user = null, pass = null;
            try
            {
                var cfg = MissionM2020Config.Instance;

                using (var ps = new ParameterStore(awsProfile, awsRegion))
                {
                    logger.LogInfo("opened parameter store to fetch CSSO credentials, profile={0}, region={1}",
                                   awsProfile, awsRegion);

                    string userKey = cfg.CSSOUsernameParameterInSSM.Replace("{venue}", venue);
                    bool userEncrypted = cfg.CSSOUsernameParameterInSSMEncrypted;
                    if (logger != null)
                    {
                        logger.LogInfo("fetching CSSO username from {0}, encrypted={1}", userKey, userEncrypted);
                    }
                    user = ps.GetParameter(userKey, userEncrypted);
                    if (string.IsNullOrEmpty(user))
                    {
                        error($"failed to get \"{userKey}\" from SSM, encrypted={userEncrypted}");
                        return null;
                    }
                    
                    string passKey = cfg.CSSOPasswordParameterInSSM.Replace("{venue}", venue);
                    bool passEncrypted = cfg.CSSOPasswordParameterInSSMEncrypted;
                    if (logger != null)
                    {
                        logger.LogInfo("fetching CSSO password from {0}, encrypted={1}", passKey, passEncrypted);
                    }
                    pass = ps.GetParameter(passKey, passEncrypted);
                    if (string.IsNullOrEmpty(user))
                    {
                        error($"failed to get \"{passKey}\" from SSM, encrypted={passEncrypted}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                error("error getting credentials from SSM: " + ex.Message.Replace("{", "{{").Replace("}", "}}"));
                return null;
            }

            string credssFilename = "credss.exe";
            string credssExe = StringHelper.NormalizeSlashes(PathHelper.GetExe(credssFilename));
            string origCredssExe = credssExe;
            while (!File.Exists(credssExe) && credssExe.LastIndexOf('/') >= 0)
            {
                string dir = StringHelper.StripLastUrlPathSegment(credssExe);
                string tryUtils = $"{dir}/Utils/{credssFilename}";
                if (File.Exists(tryUtils))
                {
                    credssExe = tryUtils;
                    break;
                }
                string parent = dir.LastIndexOf('/') > 0 ? StringHelper.StripLastUrlPathSegment(dir) : null;
                if (parent == null)
                {
                    break;
                }
                credssExe = $"{parent}/{credssFilename}";
            }

            if (!File.Exists(credssExe))
            {
                
                if (logger != null)
                {
                    logger.LogWarn("{0} not found, searched based on {1}, trying system installed {0}",
                                   credssFilename, origCredssExe);
                }
                credssExe = credssFilename;
            }

            string cmd = $"--venue {venue} --app-account -d {duration} -s {section} -u USER -p PASS";

            if (logger != null)
            {
                logger.LogInfo("{0}running {1} {2}", dryRun ? "dry " : "", credssExe, cmd);
            }

            //avoid plaintexting credentials in log
            cmd = cmd.Replace("USER", user);
            cmd = cmd.Replace("PASS", pass);

            if (!dryRun)
            {
                try
                {
                    var runner = new ProgramRunner(credssExe, cmd, captureOutput: quiet);
                    int code = runner.Run(); //blocks until process exits or dies
                    if (code == 0)
                    {
                        return section;
                    }
                    else
                    {
                        string msg = (runner.ErrorText ?? "").TrimEnd('\r', '\n');
                        error(string.Format("{0} failed with code {1}{2}{3}", credssExe, code,
                                            code == -1 ? " (killed)" : "",
                                            msg != "" ? (Environment.NewLine + msg) : ""));
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    error("error running {credssFilename}: " + ex.Message);
                    return null;
                }
            }

            return null;
        }

        public override int GetDefaultCredentialRefreshSec()
        {
            return 4 * 60 * 60; //4h
        }

        //some images have invalid PLANET_DAY_NUMBER
        //we have seen this in multiple M2020 datasets so far including ROASTT19 and TT4
        public override int DayNumber(PDSParser parser)
        {
            try
            {
                return parser.PlanetDayNumber;
            }
            catch (MetadataException)
            {
                return ParseProductId(parser.ProductIdString).GetSol();
            }
        }

        public override RoverProductCamera TranslateCamera(RoverProductCamera cam)
        {
            switch (cam)
            {
                //in early datasets ML and MR in RDR product names for M2020 really mean MastcamZ not Mastcam
                //and in any case M2020 has only MastcamZ not Mastcam
                case RoverProductCamera.MastcamLeft: return RoverProductCamera.MastcamZLeft;
                case RoverProductCamera.MastcamRight: return RoverProductCamera.MastcamZRight;
                default: return cam;
            }
        }

        public override double GetSensorPixelSizeMM(RoverProductCamera camera) {
            throw new NotImplementedException("sensor pixels size not implemented for 2020 instruments yet");
        }

        public override double GetFocalLengthMM(RoverProductCamera rovProdCam)
        {
            throw new NotImplementedException("focal lengths not implemented for 2020 instruments yet");
        }

        public override double GetMinimumFocusDistance(PDSMetadata metadata)
        {
            throw new NotImplementedException("min focus distance not implemented for 2020 instruments yet");
        }

        public override double? GetMaximumFocusDistance(PDSMetadata metadata)
        {
            throw new NotImplementedException("max focus distance not implemented for 2020 instruments yet");
        }

        public override bool PreferLinearGeometryProducts()
        {
            return MissionM2020Config.Instance.PreferLinearGeometryProducts;
        }

        public override string GetProductIDString(string product)
        {
            string idStr = StringHelper.GetLastUrlPathSegment(product, stripExtension: true);
            string pat = @"_LOD(\d*)(_\d+)?$";
            var match = Regex.Match(idStr, pat, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                idStr = idStr.Substring(0, idStr.Length - match.Groups[0].Value.Length);
            }
            return idStr;
        }

        public override RoverObservationComparator.CompareResult
            CompareRoverObservations(RoverObservation a, RoverObservation b, params string[] exceptCrit)
        {
            // 0 if a and b are equivalently good
            // negative if a is "better" than b
            // positive if a is "worse than" b
            //https://docs.google.com/document/d/15iZgxqsecD6svOUuiEQm2J10a2ziKYeQQXU_f-VXGZc#heading=h.76imaw5jdp48
            if (IsHazcam(a.Camera) || IsNavcam(a.Camera))
            {
                //EECAM downsampling A,L,M,N, prefer higher
                char edsA = a.Name[EECAM_DOWNSAMPLE_FIELD];
                char edsB = b.Name[EECAM_DOWNSAMPLE_FIELD];
                if (edsA != edsB && !exceptCrit.Contains("eecam_downsample"))
                {
                    return new RoverObservationComparator.CompareResult(edsB - edsA, "eecam_downsample");
                }
                
                //EECAM reconstruction counter 0-9A-Z, prefer higher
                char rcA = a.Name[EECAM_RECONSTRUCTION_FIELD];
                char rcB = b.Name[EECAM_RECONSTRUCTION_FIELD];
                if (rcA != rcB && !exceptCrit.Contains("eecam_recon"))
                {
                    return new RoverObservationComparator.CompareResult(rcB - rcA, "eecam_recon");
                }
            }
            
            //downsample 0-3, prefer lower
            //except keep all mask resolutions
            //because it can happen that the XYZ and RAS products have different downsamples
            char dsA = a.Name[DOWNSAMPLE_FIELD];
            char dsB = b.Name[DOWNSAMPLE_FIELD];
            if (dsA != dsB && a.ObservationType != RoverProductType.RoverMask && !exceptCrit.Contains("downsample"))
            {
                return new RoverObservationComparator.CompareResult(dsA - dsB, "downsample");
            }
            
            //compresion, prefer higher
            int compA = CompressionPreference(a.Name.Substring(COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH));
            int compB = CompressionPreference(b.Name.Substring(COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH));
            if (compA != compB && dsA == dsB && !exceptCrit.Contains("compression"))
            {
                return new RoverObservationComparator.CompareResult(compB - compA, "compression");
            }
            
            return new RoverObservationComparator.CompareResult(0, "none");
        }
        
        public override IEnumerable<RoverProductId>
            FilterProductIdGroups(IEnumerable<RoverProductId> products,
                                  Action<string, List<RoverProductId>, List<RoverProductId>> spew = null)
        {
            spew = spew ?? ((str, orig, filt) => {});

            //if we have multiple resolutions (downsample levels) within a single observation
            //then keep only the highest res (lowest downsample)
            //except keep all mask resolutions
            //because it can happen that the XYZ and RAS products have different downsamples
            var groups = products.GroupBy(id => id.GetPartialId(this, includeProductType: false,
                                                                includeVariants: false, includeVersion: false));
            var highestRes = new List<RoverProductId>();
            foreach (var group in groups)
            {
                var orig = group.ToList();

                if (orig.Count > 0 && orig[0].ProductType == RoverProductType.RoverMask)
                {
                    highestRes.AddRange(orig);
                }
                else
                {
                    //downsample 0-3, prefer lower
                    char minDS = orig.Select(id => id.FullId[DOWNSAMPLE_FIELD]).DefaultIfEmpty('0').Min();
                    var filtered = orig.Where(id => id.FullId[DOWNSAMPLE_FIELD] == minDS).ToList();
                    spew("downsample", orig, filtered);
                    highestRes.AddRange(filtered);
                }
            }

            Func<RoverProductId, bool> isEECAM = id => IsHazcam(id.Camera) || IsNavcam(id.Camera);

            groups = highestRes.GroupBy(id => id.GetPartialId(this, includeVariants: false, includeVersion: false));
            foreach (var group in groups)
            {
                var orig = group.ToList();

                //https://docs.google.com/document/d/15iZgxqsecD6svOUuiEQm2J10a2ziKYeQQXU_f-VXGZc#heading=h.76imaw5jdp48

                //EECAM downsampling A,L,M,N, prefer higher
                //note the SIS changed to allow only A or M here, but this code should remain correct (prefer M over A)
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/852
                //though also see https://github.jpl.nasa.gov/OnSight/Landform/issues/891
                char maxEDS = orig
                    .Where(id => isEECAM(id))
                    .Select(id => id.FullId[EECAM_DOWNSAMPLE_FIELD])
                    .DefaultIfEmpty('0')
                    .Max();
                var filtered = orig.Where(id => !isEECAM(id) || id.FullId[EECAM_DOWNSAMPLE_FIELD] == maxEDS).ToList();
                spew("ECAM downsampling", orig, filtered);
                orig = filtered;

                //EECAM reconstruction counter 0-9A-Z, prefer higher
                //note recon counter is _ for an EECAM tile
                //but those should have already been eliminated by CheckProductID()
                char maxERC = orig
                    .Where(id => isEECAM(id))
                    .Select(id => id.FullId[EECAM_RECONSTRUCTION_FIELD])
                    .DefaultIfEmpty('0')
                    .Max();
                filtered = orig.Where(id => !isEECAM(id) || id.FullId[EECAM_RECONSTRUCTION_FIELD] == maxERC).ToList();
                spew("ECAM recon counter", orig, filtered);
                orig = filtered;

                //compresion, prefer higher
                int maxCP = orig.Select(id => CompressionPreference(id)).DefaultIfEmpty(0).Max();
                filtered = orig.Where(id => CompressionPreference(id) == maxCP).ToList();
                spew("compression", orig, filtered);
                orig = filtered;

                foreach (var id in filtered)
                {
                    yield return id;
                }
            }
        }

        public int CompressionPreference(RoverProductId id)
        {
            int cf = COMPRESSION_FIELD, cfl = COMPRESSION_FIELD_LENGTH;
            return CompressionPreference(id.GetPartialId(cf, cfl));
        }

        public int CompressionPreference(string compression)
        {
            compression = compression.ToUpper();
            if (compression.StartsWith("L")) //lossless
            {
                return 300;
            }
            else if (compression.StartsWith("I")) //ICER
            {
                return 200;
            }
            else if (compression == "A0") //JPEG quality 100
            {
                return 100;
            }
            else if (int.TryParse(compression, out int jpegQuality))
            {
                return jpegQuality;
            }
            else
            {
                return -1;
            }
        }

        public override RoverMasker GetMasker()
        {
            return new M2020RoverMasker(this); //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/554
        }

        public override bool IsHazcam(RoverProductCamera camera)
        {
            return base.IsHazcam(camera) ||
                camera == RoverProductCamera.FrontHazcamLeftB || camera == RoverProductCamera.FrontHazcamRightB;
        }

        public override bool IsMastcam(RoverProductCamera camera)
        {
            return base.IsMastcam(camera) ||
                camera == RoverProductCamera.MastcamZLeft || camera == RoverProductCamera.MastcamZRight;
        }

        public override bool IsArmcam(RoverProductCamera camera)
        {
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/897
            return camera == RoverProductCamera.SHERLOCACI ||
                camera == RoverProductCamera.SHERLOCWATSON ||
                camera == RoverProductCamera.SHERLOCWATSONLeft || camera == RoverProductCamera.SHERLOCWATSONRight;
        }

        public override RoverProductId ParseProductId(string id)
        {
            id = StringHelper.GetLastUrlPathSegment(id, stripExtension: true);

            if (id.Length >= M2020UnifiedMeshProductId.MIN_LENGTH && id.Length <= M2020UnifiedMeshProductId.MAX_LENGTH)
            {
                var unified = M2020UnifiedMeshProductId.Parse(id);
                if (unified != null)
                {
                    return unified;
                }
            }

            switch (id.Length)
            {
                case M2020OPGSProductId.LENGTH: return M2020OPGSProductId.Parse(id);
                default: throw new Exception("unexpected length for M2020 product id");
            }
        }

        public override bool CheckProductId(RoverProductId id, out string reason)
        {
            if (!base.CheckProductId(id, out reason))
            {
                return false;
            }

            if (id is M2020OPGSProductId)
            {
                M2020OPGSProductId opgsId = (M2020OPGSProductId)id;

                bool isEECAM = IsHazcam(id.Camera) || IsNavcam(id.Camera);

                var camspec = opgsId.Camspec.ToUpper();
                if (isEECAM || IsMastcam(id.Camera))
                {
                    var stereoPartner = camspec.Substring(0, 1);
                    if (stereoPartner != "_")
                    {
                        //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/883
                        reason = "stereo partner " + stereoPartner;
                        return false;
                    }
                }

                if (isEECAM && id.FullId[EECAM_RECONSTRUCTION_FIELD] == '_')
                {
                    reason = "EECAM tile";
                    return false;
                }

                //downsample and compression handled in RoverObservationComparator

                if (opgsId.Color == RoverProductColor.Unknown)
                {
                    reason = "color filter " + opgsId.ColorFilter;
                    return false;
                }
            }

            if (id.Producer != RoverProductProducer.OPGS)
            {
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/1172
                return false;
            }

            return true;
        }

        public override IEnumerable<int[]> GetProductIdVariantSpans(RoverProductId id)
        {
            if (id is M2020OPGSProductId)
            {
                yield return new int[] { EECAM_DOWNSAMPLE_FIELD, 1 };
                yield return new int[] { EECAM_RECONSTRUCTION_FIELD, 1 };
                yield return new int[] { DOWNSAMPLE_FIELD, 1 };
                yield return new int[] { COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH };
            }
            yield break;
        }

        public override string GetS3Proxy()
        {
            return MissionM2020Config.Instance.S3Proxy.Replace("{venue}", venue);
        }

        public override Vector2? GetExpectedLandingLonLat()
        {
            return new Vector2(77.403, 18.488); //Jezero crater
        }

        public virtual string GetOrbitalS3Folder()
        {
            return "s3://m20-ids-g-landform/M2020/orbital/";
        }

        public override string GetOrbitalConfigDefaults()
        {
            //PlacesDB orbital index 0
            // - "global" frame, not associated with any specific geotiff
            // - easting/northing reported relative to lon/lat 0/0 which is what GDAL expects
            // - upper_left_{easting,northing}_m are not included in https://PLACES_URL/rmc/ORBITAL(0)/metadata
            
            //PlacesDB orbital index 1
            // - associated with the 25cm basemap CLR (color) and ORR (greyscale) orthophoto geotiffs
            // - easting/northing reported relative to ULC
            // - upper_left_{easting,northing}_m are included in https://PLACES_URL/rmc/ORBITAL(1)/metadata

            //PlacesDB orbital index 2
            // - associated with the 1m DEM geotiff
            // - easting/northing reported relative to ULC
            // - upper_left_{easting,northing}_m are included in https://PLACES_URL/rmc/ORBITAL(2)/metadata

            //since we use GDAL the recommendation (from Bob Deen) is that we actually use index 0
            //the other two are used by other subsystems which don't use GDAL to read the geotiffs
            //the only small tradeoff is that in this setup we can't cross-check the orbital metadata
            //(PlacesDB.CheckOrbital{DEM,Image}Metadata() called from IngestAlignmentInputs.IngestOrbitalAsset())

            //greyscale image: M20_PrimeMission_HiRISE_ORR_25cm.tif
            //color image: M20_PrimeMission_HiRISE_CLR_25cm.tif
            string s3Folder = GetOrbitalS3Folder();
            return "{\n" +
                "\"DEMURL\": \"" + s3Folder + "M20_PrimeMission_HiRISE_DEM_1m.tif\",\n" +
                "\"ImageURL\": \"" + s3Folder + "M20_PrimeMission_HiRISE_CLR_25cm.tif\",\n" +
                "\"StoragePath\": \"M2020/orbital\",\n" +
                "\"DEMMetersPerPixel\": 1,\n" +
                "\"ImageMetersPerPixel\": 0.25,\n" +
                "\"DEMPlacesDBIndex\": 0,\n" +
                "\"ImagePlacesDBIndex\": 0\n" +
                "}";
        }

        public override string GetPlacesConfigDefaults()
        {
            string sfx = venue == "dev" ? "-dev" : "";
            return "{\n" +
                $"\"Url\": \"https://places{sfx}.{venue}.m20.jpl.nasa.gov\",\n" +
                "\"Views\": \"telemetry,best_interp,best_tactical\",\n" +
                "\"AuthCookieName\": \"ssosession\",\n" +
                $"\"AuthCookieFile\": \"~/.cssotoken/{venue}/ssosession\"\n" +
                "}";
        }

        public override string SolToString(int sol)
        {
            return M2020OPGSProductId.SolToString(sol);
        }

        public override RoverProductGeometry GetTacticalMeshGeometry()
        {
            return RoverProductGeometry.Raw;
        }

        public override string GetTacticalMeshFrame(RoverProductId id = null)
        {
            if (id is M2020OPGSProductId)
            {
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/1149
                switch ((id as M2020OPGSProductId).MeshType.ToUpper())
                {
                    case "R": return "rover";
                    default: return base.GetTacticalMeshFrame();
                }
            }
            return base.GetTacticalMeshFrame();
        }

        public override string GetTacticalMeshTriggerRegex()
        {
            //return "auto_obj_lod_fn"; //see ProcessTactical.ParseMeshRegex()
            return "auto_iv";
        }

        // Workaround for datasets where RMC does not properly increment, a common test-ism.
        // May break multiple images with different filters if they have the same timestamp (but does that happen?).
        protected string RoverMotionCounterFromTimeString(PDSParser parser)
        {
            return ((M2020OPGSProductId)ParseProductId(parser.ProductIdString)).GetConcatenatedTimeString();
        }

        public override List<string> GetAllowedProcessingTypes()
        {
            return GetAllowedProcessingTypes(MissionM2020Config.Instance.AllowedProcessingTypes);
        }

        public override List<RoverProductProducer> GetAllowedProducers()
        {
            return GetAllowedProducers(MissionM2020Config.Instance.AllowedProducers);
        }
    }

    public class MissionROASTT19 : MissionM2020 
    {
        public MissionROASTT19(string venue = null) : base(venue) { }

        public override Mission GetMission()
        {
            return Mission.ROASTT19;
        }

        public override string RoverMotionCounter(PDSParser parser)
        {          
            return RoverMotionCounterFromTimeString(parser);
        }

        // ROASTT19: for some images the INSTRUMENT_ID says LEFT when it should say RIGHT, so use PRODUCT_ID instead
        public override RoverProductCamera GetCamera(PDSParser parser)
        {
            return TranslateCamera(ParseProductId(parser.ProductIdString).Camera);
        }

        public override bool PreferLinearGeometryProducts()
        {
            return true;
        }

        public override string GetOrbitalConfigDefaults()
        {
            return null; //don't have orbital for ROASTT19
        }

        public override bool AllowPlacesDB()
        {
            return false; //as of 3/19/20 it doesn't look like this PLACES instance is live anymore
        }

        public override string GetPlacesConfigDefaults()
        {
            return
                "{ " +
                "\"Url\": \"https://places-external-roastt.m20-training.jpl.nasa.gov/m2020-places\", " +
                "\"Views\": \"telemetry,best_interp,best_tactical\",\n" +
                "\"AuthCookieName\": \"ssosession\", " +
                $"\"AuthCookieFile\": \"~/.cssotoken/{venue}/ssosession\"" +
                "}";
        }

        public override RoverProductGeometry GetTacticalMeshGeometry()
        {
            return RoverProductGeometry.Linearized;
        }

        public override string GetTacticalMeshTriggerRegex()
        {
            return "auto_iv"; //see ProcessTactical.ParseMeshRegex()
        }
    }

    public class MissionTT4 : MissionM2020
    {
        public const int SEQUENCE_FIELD = 35; 
        public const int SEQUENCE_FIELD_LENGTH = 9; 

        public MissionTT4(string venue = null) : base(venue) { }

        public override Mission GetMission()
        {
            return Mission.TT4;
        }

        public override bool PreferLinearGeometryProducts()
        {
            return true;
        }

        //TT4: sequence number is bumped for different variants
        public override IEnumerable<int[]> GetProductIdVariantSpans(RoverProductId id)
        {
            foreach (var span in base.GetProductIdVariantSpans(id))
            {
                yield return span;
            }
            yield return new int[] { SEQUENCE_FIELD, SEQUENCE_FIELD_LENGTH };
            yield break;
        }

        public override string GetOrbitalConfigDefaults()
        {
            return null; //don't have orbital for TT4
        }

        public override bool AllowPlacesDB()
        {
            return false; //as of 3/19/20 it is unlikely that this PLACES instance still is populated with TT4 data
        }

        public override string GetPlacesConfigDefaults()
        {
            return "{\n" +
                "\"Url\": \"https://places-sstage.m20.jpl.nasa.gov\",\n" +
                "\"Views\": \"telemetry,best_interp,best_tactical\",\n" +
                "\"AuthCookieName\": \"ssosession\",\n" +
                $"\"AuthCookieFile\": \"~/.cssotoken/{venue}/ssosession\"\n" +
                "}";
        }

        public override RoverProductGeometry GetTacticalMeshGeometry()
        {
            return RoverProductGeometry.Linearized;
        }

        public override string GetTacticalMeshTriggerRegex()
        {
            return "auto_iv"; //see ProcessTactical.ParseMeshRegex()
        }
    }

    public class MissionScarecrowEECAM : MissionM2020
    {
        private class ScarecrowEECAMUnifiedMesh : OPGSProductId
        {
            public const int LENGTH = 10;

            protected ScarecrowEECAMUnifiedMesh(string fullId, int site, int drive)
                : base(fullId, "J", RoverProductType.Points, camera: "NL", geometry: "L",
                       color: "", version: "0", size: "", site: site, drive: drive, spec: "_") 
            { }

            public static ScarecrowEECAMUnifiedMesh Parse(string id)
            {
                id = StringHelper.StripUrlExtension(id);
                if (id.Length != LENGTH)
                {
                    return null;
                }

                string siteStr = id.Substring(3, 3);
                string driveStr = id.Substring(6, 4);

                if (!int.TryParse(siteStr, out int site) || !int.TryParse(driveStr, out int drive))
                {
                    return null;
                }
                
                return new ScarecrowEECAMUnifiedMesh(id, site, drive);
            }

            public override bool IsSingleFrame()
            {
                return false;
            }
            
            public override bool IsSingleCamera()
            {
                return true;
            }
            
            public override bool IsSingleSiteDrive()
            {
                return true;
            }

            protected override RoverProductProducer ParseProducer(string producer, string camera)
            {
                return ParseM2020Producer(producer, camera);
            }

            protected override RoverProductColor ParseColor(string color, string camera)
            {
                return RoverProductColor.Unknown;
            }

            public override string AsThumbnail()
            {
                throw new NotImplementedException();
            }

            protected override RoverProductSize ParseSize(string size)
            {
                return RoverProductSize.Regular;
            }

            public override int GetSol()
            {
                return 0;
            }
        }

        public MissionScarecrowEECAM(string venue = null) : base(venue) { }

        public override RoverProductId ParseProductId(string id)
        {
            id = StringHelper.GetLastUrlPathSegment(id, stripExtension: true);
            if (id.Length == ScarecrowEECAMUnifiedMesh.LENGTH)
            {
                return ScarecrowEECAMUnifiedMesh.Parse(id);
            }
            return base.ParseProductId(id);
        }

        public override bool PreferLinearGeometryProducts()
        {
            return true;
        }

        public override string GetOrbitalConfigDefaults()
        {
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/1004
            //use assets at s3://m20-ids-g-landform/MarsYard_Aerial06062019
            return null;
        }

        public override bool AllowPlacesDB()
        {
            return false; //don't have places for scarecrow-eecam
        }

        public override string GetPlacesConfigDefaults()
        {
            return null;
        }

        public override RoverProductGeometry GetTacticalMeshGeometry()
        {
            return RoverProductGeometry.Linearized;
        }

        public override string GetTacticalMeshTriggerRegex()
        {
            return "auto_iv"; //see ProcessTactical.ParseMeshRegex()
        }
    }

    public class MissionROASTT20 : MissionM2020
    {
        public MissionROASTT20(string venue = null) : base(venue) { }

        public override Mission GetMission()
        {
            return Mission.ROASTT20;
        }

        public override string RoverMotionCounter(PDSParser parser)
        {          
            return RoverMotionCounterFromTimeString(parser);
        }

        //MASTCAM-Z images have 'unk' in image_type metadata
        public override RoverProductSize GetRoverProductSize(PDSParser parser)
        {
            RoverProductSize prodSize = parser.ImageSizeType;
            if(prodSize == RoverProductSize.Unknown)
            {
                RoverProductId prodId = this.ParseProductId(parser.ProductIdString);
                if (prodId != null)
                {
                    OPGSProductId id = prodId as OPGSProductId;
                    prodSize = id.Size; 
                }
            }

            return prodSize;
        }

        public override bool PreferLinearGeometryProducts()
        {
            return true;
        }

        public override string GetS3Proxy()
        {
            return "https://data-roastt.m20-training.jpl.nasa.gov";
        }

        public override string GetOrbitalConfigDefaults()
        {
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/1004
            //use assets at s3://m20-ids-g-landform/ROASTT20/orbital
            return null;
        }

        public override string GetPlacesConfigDefaults()
        {
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/725#issuecomment-267319
            //per Kevin Grimes on 3/18/20 ROASTT20 data will soon move to
            //https://places-roastt.dev.m20.jpl.nasa.gov
            return "{\n" +
                $"\"Url\": \"https://places-rocs.{venue}.m20.jpl.nasa.gov\",\n" +
                "\"Views\": \"telemetry,best_interp,best_tactical\",\n" +
                "\"AuthCookieName\": \"ssosession\",\n" +
                $"\"AuthCookieFile\": \"~/.cssotoken/{venue}/ssosession\"\n" +
                "}";
        }

        public override RoverProductGeometry GetTacticalMeshGeometry()
        {
            return RoverProductGeometry.Linearized;
        }

        public override string GetTacticalMeshTriggerRegex()
        {
            return "auto_iv"; //see ProcessTactical.ParseMeshRegex()
        }
    }

    public class MissionORT11 : MissionM2020
    {
        public MissionORT11(string venue = null) : base(venue ?? "sops") { }

        public override Mission GetMission()
        {
            return Mission.ORT11;
        }

        public override string GetTacticalMeshTriggerRegex()
        {
            return "auto_iv"; //see ProcessTactical.ParseMeshRegex()
        }
    }

    public class MissionTT16 : MissionM2020
    {
        public MissionTT16(string venue = null) : base(venue ?? "dev") { }

        public override Mission GetMission()
        {
            return Mission.TT16;
        }
    
        public override string RoverMotionCounter(PDSParser parser)
        {          
            return RoverMotionCounterFromTimeString(parser);
        }
    }

    public class MissionM20SOPS : MissionM2020
    {
        public MissionM20SOPS(string venue = null) : base(venue ?? "sops") { }

        public override Mission GetMission()
        {
            return Mission.M20SOPS;
        }

        public override string GetOrbitalS3Folder()
        {
            return $"s3://m20-{venue}-ods/ods/surface/strategic/ids/orbital/";
        }
    }
}

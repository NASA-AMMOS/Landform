using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JPLOPS.Util;
using JPLOPS.Cloud;
using JPLOPS.Imaging;
using JPLOPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;

namespace JPLOPS.Pipeline
{
    public class MissionM2020Config : SingletonConfig<MissionM2020Config>
    {
        public const string CONFIG_FILENAME = "mission-m2020"; //config file will be ~/.landform/mission-m2020.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }

        [ConfigEnvironmentVariable("LANDFORM_USE_MASTCAM_FOR_ALIGNMENT")]
        public bool UseMastcamForAlignment { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_MASTCAM_FOR_MESHING")]
        public bool UseMastcamForMeshing { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_REAR_HAZCAM_FOR_ALIGNMENT")]
        public bool UseRearHazcamForAlignment { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_REAR_HAZCAM_FOR_MESHING")]
        public bool UseRearHazcamForMeshing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_PREFER_LINEAR_GEOMETRY_PRODUCTS")]
        public bool PreferLinearGeometryProducts { get; set; } = false;

        //CSSO credentials username parameter in SSM, {venue} will be replaced
        [ConfigEnvironmentVariable("LANDFORM_CSSO_USERNAME_PARAMETER_IN_SSM")]
        public string CSSOUsernameParameterInSSM { get; set; } = "REMOVED";

        //whether CSSO credentials username parameter in SSM is encrypted
        [ConfigEnvironmentVariable("LANDFORM_CSSO_USERNAME_PARAMETER_IN_SSM_ENCRYPTED")]
        public bool CSSOUsernameParameterInSSMEncrypted { get; set; } = true;

        //CSSO credentials password parameter in SSM, {venue} will be replaced
        [ConfigEnvironmentVariable("LANDFORM_CSSO_PASSWORD_PARAMETER_IN_SSM")]
        public string CSSOPasswordParameterInSSM { get; set; } = "REMOVED";

        //whether CSSO credentials password parameter in SSM is encrypted
        [ConfigEnvironmentVariable("LANDFORM_CSSO_PASSWORD_PARAMETER_IN_SSM_ENCRYPTED")]
        public bool CSSOPasswordParameterInSSMEncrypted { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_CSSO_CREDENTIAL_DURATION_SEC")]
        public int CSSOCredentialDurationSec { get; set; } = 8 * 60 * 60; //8h

        //{venue} will be replaced with mission venue
        [ConfigEnvironmentVariable("LANDFORM_S3_DATA_PROXY")]
        public string S3Proxy { get; set; } = "https://data.{venue}.m20.jpl.nasa.gov";

        //comma separated list of processing types to allow
        //sorted in order of preference (best last)
        //https://wiki.jpl.nasa.gov/pages/viewpage.action?spaceKey=MSMFS&title=Special+Character+Flags
        [ConfigEnvironmentVariable("LANDFORM_ALLOWED_PROCESSING_TYPES")]
        public string AllowedProcessingTypes { get; set; } = "_,C,P,S,R"; 

        //comma separated list of producers to allow
        //must match RoverProductProducer enum values
        //sorted in order of preference (best last)
        [ConfigEnvironmentVariable("LANDFORM_ALLOWED_PRODUCERS")]
        public string AllowedProducers { get; set; } = "OPGS";  //"OPGS,ASU"

        //SSM service watchdog process name, empty to disable
        [ConfigEnvironmentVariable("LANDFORM_WATCHDOG_SSM_PROCESS")]
        public string WatchdogSSMProcess { get; set; } = "amazon-ssm-agent"; 

        //SSM service watchdog restart command, {venue} will be replaced, empty to disable
        [ConfigEnvironmentVariable("LANDFORM_WATCHDOG_SSM_COMMAND")]
        public string WatchdogSSMCommand { get; set; } =
            "powershell -Command \"& { Restart-Service AmazonSSMAgent }\"";

        //CloudWatch service watchdog process name, empty to disable
        [ConfigEnvironmentVariable("LANDFORM_WATCHDOG_CLOUDWATCH_PROCESS")]
        public string WatchdogCloudWatchProcess { get; set; } = "amazon-cloudwatch-agent"; 

        //CloudWatch service watchdog restart command, {venue} and {cwagentctl} will be replaced, empty to disable
        [ConfigEnvironmentVariable("LANDFORM_WATCHDOG_CLOUDWATCH_COMMAND")]
        public string WatchdogCloudWatchCommand { get; set; } =
            "powershell -Command \"& {cwagentctl} -a fetch-config -m ec2 -s -c file:C:\\landform\\config_files\\amazon-cloudwatch-agent.json\"";

        //comma separated list of S3 URLs of FDR directories with sol number replaced by #####
        //{venue} will be replaced
        //for testing in dev venue override like this in contextual master EC2 userdata:
        //set LANDFORM_FDR_SEARCH_DIRS=s3://m20-ids-g-landform/M2020/sol/####/ids/fdr/ncam/
        [ConfigEnvironmentVariable("LANDFORM_FDR_SEARCH_DIRS")]
        public string FDRSearchDirs { get; set; } =
            "s3://m20-{venue}-ods/ods/surface/sol/#####/ids/fdr/fcam/," +
            "s3://m20-{venue}-ods/ods/surface/sol/#####/ids/fdr/rcam/," +
            "s3://m20-{venue}-ods/ods/surface/sol/#####/ids/fdr/ncam/";
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

        //https://wiki.jpl.nasa.gov/display/MSMFS/File+and+S3+Object+Path+Conventions
        //https://wiki.jpl.nasa.gov/display/MSMFS/Instruments+That+IDS+Processes
        private readonly string[] MASTCAM_RDR_SUBDIRS = new string[] { "zcam" };
        private readonly string[] ARMCAM_RDR_SUBDIRS = new string[] { "shrlc" };

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

            var cfg = MissionM2020Config.Instance;
            int duration = cfg.CSSOCredentialDurationSec;
            string section = "credss-app";

            awsProfile = awsProfile ?? GetDefaultAWSProfile();
            awsRegion = awsRegion ?? GetDefaultAWSRegion();

            string user = null, pass = null;
            try
            {
                using (var ps = new ParameterStore(awsProfile, awsRegion))
                {
                    logger.LogVerbose("opened parameter store to fetch CSSO credentials, profile={0}, region={1}",
                                      awsProfile, awsRegion);

                    string userKey = cfg.CSSOUsernameParameterInSSM.Replace("{venue}", venue);
                    bool userEncrypted = cfg.CSSOUsernameParameterInSSMEncrypted;
                    if (logger != null)
                    {
                        logger.LogVerbose("fetching CSSO username from {0}, encrypted={1}", userKey, userEncrypted);
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
                        logger.LogVerbose("fetching CSSO password from {0}, encrypted={1}", passKey, passEncrypted);
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
                string tryBin = $"{dir}/Bin/{credssFilename}";
                if (File.Exists(tryBin))
                {
                    credssExe = tryBin;
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

            string cmd = $"DISABLED";

            if (logger != null)
            {
                logger.LogVerbose("{0}running {1} {2}", dryRun ? "dry " : "", credssExe, cmd);
            }

            //avoid plaintexting credentials in log
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

        public override int GetCredentialDurationSec()
        {
            return MissionM2020Config.Instance.CSSOCredentialDurationSec;
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

        public override bool UseMastcamForAlignment()
        {
            return MissionM2020Config.Instance.UseMastcamForAlignment;
        }

        public override bool UseMastcamForMeshing()
        {
            return MissionM2020Config.Instance.UseMastcamForMeshing;
        }

        public override bool UseRearHazcamForAlignment()
        {
            return MissionM2020Config.Instance.UseRearHazcamForAlignment;
        }

        public override bool UseRearHazcamForMeshing()
        {
            return MissionM2020Config.Instance.UseRearHazcamForMeshing;
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
            FilterProductIDGroups(IEnumerable<RoverProductId> products,
                                  Action<string, List<RoverProductId>, List<RoverProductId>> spew = null)
        {
            spew = spew ?? ((str, orig, filt) => {});

            Func<RoverProductId, bool> isMask = id => id.ProductType == RoverProductType.RoverMask;
            Func<RoverProductId, bool> isEECAM = id => IsHazcam(id.Camera) || IsNavcam(id.Camera);

            var empty = new List<RoverProductId>();

            //cull video
            //the idea here is to find relatively large groups of zcam images all with the same sequence ID
            //unfortunately it looks like this is a bogus approach, e.g. in sol 53 there are about 275 images in
            //sequence ZCAM08100 but those don't appear to be video frames
            //instead, see IsVideoProduct() which checks for a corresponding ECV EDR
            //const int MIN_VIDEO_GROUP = 10;
            //if (!AllowVideoProducts())
            //{
            //    var zcamProducts = products
            //        .Where(id => (id is M2020OPGSProductId) && IsMastcam(id.Camera))
            //        .Cast<M2020OPGSProductId>()
            //        .ToList();
            //    if (zcamProducts.Count > 0)
            //    {
            //        var vidSeqs = new HashSet<string>();
            //        var groups = zcamProducts.GroupBy(id => id.Sequence);
            //        foreach (var group in groups)
            //        {
            //            if (group.Select(id => id.GetPartialId(this, includeVersion: false)).Distinct().Count() >
            //                MIN_VIDEO_GROUP)
            //            {
            //                vidSeqs.Add(group.First().Sequence);
            //            }
            //        }
            //        foreach (var group in groups)
            //        {
            //            if (vidSeqs.Contains(group.Key))
            //            {
            //                spew("video sequence " + group.Key, group.Cast<RoverProductId>().ToList(), empty);
            //            }
            //        }
            //        products = zcamProducts
            //            .Where(id => !vidSeqs.Contains(id.Sequence))
            //            .Concat(products.Where(id => !(id is M2020OPGSProductId) || !IsMastcam(id.Camera)))
            //            .ToList();
            //    }
            //}

            //cull orphan masks
            var omFiltered = new List<RoverProductId>();
            foreach (var group in products.GroupBy(id => id.GetPartialId(this, includeProductType: false,
                                                                         includeVersion: false)))
            {
                var orig = group.ToList();
                if (orig.Count > 0 && !orig.All(isMask))
                {
                    omFiltered.AddRange(orig);
                }
                else
                {
                    spew("orphan mask", orig, empty);
                }
            }
            products = omFiltered;

            //if we have multiple resolutions (downsample levels) within a single observation
            //then keep only the highest res (lowest downsample)
            //except keep all mask resolutions
            //because it can happen that the XYZ and RAS products have different downsamples
            var dsFiltered = new List<RoverProductId>();
            foreach (var group in products.GroupBy(id => id.GetPartialId(this, includeProductType: false,
                                                                         includeVariants: false,
                                                                         includeVersion: false)))
            {
                var orig = group.ToList();
                char minDS = orig.Select(id => id.FullId[DOWNSAMPLE_FIELD]).DefaultIfEmpty('0').Min();
                var filtered = orig.Where(id => isMask(id) || id.FullId[DOWNSAMPLE_FIELD] == minDS).ToList();
                spew("downsample", orig, filtered);
                dsFiltered.AddRange(filtered);
            }
            products = dsFiltered;

            foreach (var group in products.GroupBy(id => id.GetPartialId(this, includeVariants: false,
                                                                         includeVersion: false)))
            {
                var orig = group.ToList();

                //https://docs.google.com/document/d/15iZgxqsecD6svOUuiEQm2J10a2ziKYeQQXU_f-VXGZc#heading=h.76imaw5jdp48

                //EECAM downsampling A,L,M,N, prefer higher
                //note the SIS changed to allow only A or M here, but this code should remain correct (prefer M over A)
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
            return new M2020RoverMasker(this);
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
            return camera == RoverProductCamera.SHERLOCACI ||
                camera == RoverProductCamera.SHERLOCWATSON ||
                camera == RoverProductCamera.SHERLOCWATSONLeft || camera == RoverProductCamera.SHERLOCWATSONRight;
        }

        public override string[] GetMastcamRDRSubdirs()
        {
            return MASTCAM_RDR_SUBDIRS;
        }

        public override string[] GetArmcamRDRSubdirs()
        {
            return ARMCAM_RDR_SUBDIRS;
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
                return false;
            }

            return true;
        }

        public override Regex GetVideoURLRegex()
        {
            return new Regex(@"^.*/(Z.0[^/]{20}ECV[^/]{26})\d{2}\.(IMG|VIC)$");
        }

        public override bool IsVideoProduct(RoverProductId id, string url, Func<string, string, bool> videoEDRExists)
        {
            if (IsMastcam(id.Camera) && !string.IsNullOrEmpty(url) && videoEDRExists != null)
            {
                url = StringHelper.NormalizeUrl(url);
                int rdrIdx = url.LastIndexOf("/rdr/");
                if (rdrIdx >= 0 && url.Length > rdrIdx + 5)
                {
                    string idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                    if (idStr == id.FullId && (idStr.StartsWith("ZLF") || idStr.StartsWith("ZRF")) &&
                        id.GetProductTypeSpan(out int pts, out int ptl) && id.GetVersionSpan(out int vs, out int vl) &&
                        ptl == 3 && (vs + vl == idStr.Length) && (pts + ptl <= vs))
                    {
                        url = StringHelper.StripLastUrlPathSegment(url) + "/";
                        url = url.Substring(0, rdrIdx) + "/edr/" + url.Substring(rdrIdx + 5); //"/rdr/" -> "/edr/"
                        idStr = idStr.Substring(0, vs); //strip version
                        idStr = idStr.Substring(0, pts) + "ECV" + idStr.Substring(pts + ptl); //prod type -> "ECV"
                        idStr = idStr.Substring(0, 2) + "0" + idStr.Substring(3); //"Z?F" -> "Z?0"
                        return videoEDRExists(url, idStr);
                    }
                }
            }
            return false;
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
            if (venue == "dev")
            {
                return "s3://BUCKET/M2020/orbital/";
            }
            return $"s3://BUCKET/ods/surface/strategic/ids/orbital/";
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

        protected string GetPlacesConfigDefaults(string url, string views = "best_interp")
        {
            //From RGD: this is basically the same as MSL, except some of the names have changed.
            //There are three views you  might care about (there are a few others you won't):
            //
            //telemetry
            //best_tactical (NB: during early mission this view was borked)
            //best_interp (NB: best and should fall back to others)
            //
            //Telemetry contains whatever the rover sent, period.
            //It has all frames we know anything about, but NO localization whatsoever.
            //
            //Best_tactical contains ONLY the localization points.  So if you do a normal, shallow query, you'll see
            //only those places where the rover was actually localized (generally end-of-drive, although also sometimes
            //mid-drive).  If you do a deep query (deep=true) then it'll go to the parent for answers, which means
            //telemetry.  HOWEVER... those are unlocalized telemetry values, so you'd get a discontinuous path.  Not
            //recommended.  The purpose of best_tactical is to highlight and store the actual localization points.
            //
            //Best_interp contains the interpolated drive path.  That is, all the points from telemetry, interpolated
            //between localization points so we have a continuous drive path.  This is the one you almost certainly want
            //to use.
            //
            //However, neither best_tactical nor best_interp will show a value if localization has not yet been done.
            //If you add deep=true then it will go back to telemetry if there's no answer yet... but you may get a
            //discontinuous drive path that way.
            return "{\n" +
                $"\"Url\": \"{url}\",\n" +
                $"\"View\": \"{views}\",\n" +
                "\"AlwaysCheckRMC\": false,\n" +
                "\"AuthCookieName\": \"ssosession\",\n" +
                $"\"AuthCookieFile\": \"~/.cssotoken/{venue}/ssosession\"\n" +
                "}";
        }

        public override string GetPlacesConfigDefaults()
        {
            string sfx = venue == "dev" ? "-dev" : "";
            return GetPlacesConfigDefaults($"https://places{sfx}.{venue}.m20.jpl.nasa.gov");
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
            var id = (M2020OPGSProductId)ParseProductId(parser.ProductIdString);
            return $"{id.Sol}_{id.Sclk}_{id.SclkMS}";
        }

        public override List<string> GetAllowedProcessingTypes()
        {
            return GetAllowedProcessingTypes(MissionM2020Config.Instance.AllowedProcessingTypes);
        }

        public override List<RoverProductProducer> GetAllowedProducers()
        {
            return GetAllowedProducers(MissionM2020Config.Instance.AllowedProducers);
        }

        public override string GetSSMWatchdogProcess()
        {
            return MissionM2020Config.Instance.WatchdogSSMProcess;
        }

        public override string GetSSMWatchdogCommand()
        {
            //https://docs.aws.amazon.com/systems-manager/latest/userguide/sysman-install-win.html
            return MissionM2020Config.Instance.WatchdogSSMCommand.Replace("{venue}", venue);
        }

        public override string GetCloudWatchWatchdogProcess()
        {
            return MissionM2020Config.Instance.WatchdogCloudWatchProcess;
        }

        public override string GetCloudWatchWatchdogCommand()
        {
            //https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/install-CloudWatch-Agent-on-EC2-Instance-fleet.html#start-CloudWatch-Agent-EC2-fleet
            string cmd = MissionM2020Config.Instance.WatchdogCloudWatchCommand.Replace("{venue}", venue);
            return cmd.Replace("{cwagentctl}",
                               "'C:\\Program Files\\Amazon\\AmazonCloudWatchAgent\\amazon-cloudwatch-agent-ctl.ps1'");
        }

        public override List<string> GetFDRSearchDirs()
        {
            return MissionM2020Config.Instance.FDRSearchDirs.Split(',')
                .Select(d => d.Replace("{venue}", venue))
                .ToList();
        }
    }
}

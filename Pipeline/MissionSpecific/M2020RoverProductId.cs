using System;
using System.Linq;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class M2020OPGSProductId : OPGSProductId
    {
        public const int LENGTH = 54;

        public readonly string ColorFilter, Venue, Sequence, Camspec, Downsample, Compression, MeshType;
        public readonly int Sol;
        public readonly long Sclk;
        public readonly int SclkMS;

        protected M2020OPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                     string color, string version, string size, int site, int drive,
                                     string spec, int sol, string venue, long sclk, int sclkMS,
                                     string sequence, string camspec, string downsample, string compression,
                                     string meshType)
            : base(fullId, producer, productType, camera, geometry, color, version, size, site, drive, spec)
        {
            this.ColorFilter = color;
            this.Sol = sol;
            this.Venue = venue;
            this.Sclk = sclk;
            this.SclkMS = sclkMS;
            this.Sequence = sequence;
            this.Camspec = camspec;
            this.Downsample = downsample;
            this.Compression = compression;
            this.MeshType = meshType;
        }

        public static M2020OPGSProductId Parse(string productId)
        {
            //NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00
            //| |||   ||         ||  |  | \   \  |        |   |  |
            //0 234   89        1920 23 26 27 31 35       44  48 51

            productId = StringHelper.StripUrlExtension(productId);
            if (productId.Length != LENGTH)
            {
                return null;
            }

            string inst = productId.Substring(0, 2);
            string colorFilter = productId.Substring(2, 1);
            string spec = productId.Substring(3, 1);
            string ts0Str = productId.Substring(4, 4);
            string venue = productId.Substring(8, 1);
            string ts1Str = productId.Substring(9, 10);
            string meshType = productId.Substring(19, 1);
            string ts2Str = productId.Substring(20, 3);
            string prodType = productId.Substring(23, 3);
            string geometry = productId.Substring(26, 1);
            string thumb = productId.Substring(27, 1);
            string siteStr = productId.Substring(28, 3);
            string driveStr = productId.Substring(31, 4);
            string sequence = productId.Substring(35, 9);
            string camspec = productId.Substring(44, 4);
            string downsample = productId.Substring(48, 1);
            string compression = productId.Substring(49,2);
            string producer = productId.Substring(51, 1);
            string version = productId.Substring(52, 2);

            int sol = ParseSol(ts0Str);
            int site = ParseSite(siteStr);
            int drive = ParseDrive(driveStr);
            if (sol < 0 || site < 0 || drive < 0)
            {
                return null;
            }

            if (!ParseSclk(ts0Str, ts1Str, ts2Str, out long sclk, out int sclkMS))
            {
                return null;
            }

            return new M2020OPGSProductId(fullId: productId, producer: producer, productType: prodType, camera: inst,
                                          geometry: geometry, color: colorFilter, version: version, size: thumb,
                                          site: site, drive: drive, spec: spec, sol: sol, venue: venue, sclk: sclk,
                                          sclkMS: sclkMS, sequence: sequence, camspec: camspec, downsample: downsample,
                                          compression: compression, meshType: meshType);
        }

        public static bool ParseSclk(string ts0Str, string ts1Str, string ts2Str, out long sclk, out int sclkMS)
        {
            sclk = -1;
            sclkMS = -1;
            if (string.IsNullOrEmpty(ts0Str) || string.IsNullOrEmpty(ts1Str) || string.IsNullOrEmpty(ts2Str))
            {
                return false;
            }
            if (Char.IsLetter(ts0Str, ts0Str.Length - 1)) //ground test in which SCLK is reset
            {
                //MMDDHHmmss
                if (ts1Str.Length == 10 &&
                    int.TryParse(ts1Str.Substring(0, 2), out int MM) &&
                    int.TryParse(ts1Str.Substring(2, 2), out int DD) &&
                    int.TryParse(ts1Str.Substring(4, 2), out int HH) &&
                    int.TryParse(ts1Str.Substring(6, 2), out int mm) &&
                    int.TryParse(ts1Str.Substring(8, 2), out int ss))
                {
                    int yr = 2020;
                    sclk = (long)((new DateTime(yr, MM, DD, HH, mm, ss)).Subtract(new DateTime(yr, 1, 1)).TotalSeconds);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (ts1Str.All(c => c == '_'))
                {
                    sclk = 9999999999L; //9,999,999,999
                }
                else if (!long.TryParse(ts1Str, out sclk))
                {
                    return false;
                }
            }
            if (!int.TryParse(ts2Str, out sclkMS))
            {
                return false;
            }
            return true;
        }

        //parse a 4 character sol string
        //returns integer in range [0,9999], -1 if invalid, 10000 if out of range
        //note: overflow above sol 9999 occurs after about 28 Earth years of operations
        //for cruise and ground tests this will return the day of Earth year (DOY)
        //which can be substituted into paths like s3://BUCKET/ods/VER/YYYY/DOY/...
        public static int ParseSol(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return -1;
            }
            if (str.All(c => c == '_'))
            {
                return 10000;
            }
            int offset = 0;
            if (Char.IsLetter(str, 0)) //cruise or ground test in which SCLK is not reset
            {
                char c = char.ToUpper(str[0]);
                //offset = 365 * (c - 'A'); //just return day of year
                str = str.Substring(1);
            }
            else if (Char.IsLetter(str, str.Length - 1)) //ground test in which SCLK is reset
            {
                char c = char.ToUpper(str[str.Length - 1]);
                //offset = 365 * (c - 'A'); //just return day of year
                str = str.Substring(0, str.Length - 1);
            }
            return Math.Min(int.TryParse(str, out int sol) ? offset + sol : -1, 10000);
        }

        //format sol as a 4 digit number
        //note: if sol is greater than 9999 the return will have more than 4 digits
        public static string SolToString(int sol)
        {
            return string.Format("{0:D4}", sol);
        }

        protected override RoverProductProducer ParseProducer(string producer, string camera)
        {
            return ParseM2020Producer(producer, camera);
        }

        protected override RoverProductSize ParseSize(string size)
        {
            switch (size.ToUpper())
            {
                case "N": case "": return RoverProductSize.Regular;
                case "T": return RoverProductSize.Thumbnail;
                default: return RoverProductSize.Unknown;
            }
        }

        protected override RoverProductColor ParseColor(string color, string camera)
        {
            switch (color.ToUpper())
            {
                case "F": return RoverProductColor.FullColor;
                case "M": return RoverProductColor.Grayscale;
                case "R": return RoverProductColor.Red;
                case "G": return RoverProductColor.Green;
                case "B": return RoverProductColor.Blue;
                default: return RoverProductColor.Unknown;
            }
        }

        public override bool GetVersionSpan(out int start, out int length)
        {
            start = 52;
            length = 2;
            return true;
        }

        public override bool GetProductTypeSpan(out int start, out int length)
        {
            start = 23;
            length = 3;
            return true;
        }

        public override bool GetGeometrySpan(out int start, out int length)
        {
            start = 26;
            length = 1;
            return true;
        }

        public override bool GetColorFilterSpan(out int start, out int length)
        {
            start = 2;
            length = 1;
            return true;
        }

        public override bool GetInstrumentSpan(out int start, out int length)
        {
            start = 0;
            length = 2;
            return true;
        }

        public override bool GetStereoPartnerSpan(out int start, out int length)
        {
            //Note: PIXL MCC does not have a stereo partner field in its product ID
            //but we don't support that instrument
            start = 44;
            length = 1;
            return true;
        }

        public override bool GetSizeSpan(out int start, out int length)
        {
            start = 27;
            length = 1;
            return true;
        }

        public override bool GetMeshTypeSpan(out int start, out int length)
        {
            start = 19;
            length = 1;
            return true;
        }

        public override bool GetSpecialProcessingSpan(out int start, out int length)
        {
            start = 3;
            length = 1;
            return true;
        }

        public override bool GetProducerSpan(out int start, out int length)
        {
            start = 51;
            length = 1;
            return true;
        }

        public override bool HasSol()
        {
            return true;
        }

        public override int GetSol()
        {
            return Sol;
        }

        public override bool HasSclk()
        {
            return true;
        }

        public override double GetSclk()
        {
            return Sclk + (SclkMS * 0.001);
        }
    }

    public class M2020UnifiedMeshProductId : UnifiedMeshProductIdBase
    {
        public const int MIN_LENGTH = 40;
        public const int MAX_LENGTH = 52;

        public readonly string MeshType; //T: tactical, C: contextual, H: helicopter, O: other
        public readonly string Frame; //S: site, L: local, R: rover, O: other
        public readonly string Resolution; //ECAM tile pixel avaraging: 1: 1x1, 2: 2x2, 4: 4x4, M: multi-resolution
        public readonly int Pyramid; //2^Pyramid downsampling, 0 for full resolution

        //_: Flight (surface or cruise), A: AVSTB, F: FSWTB, M: MSTB, R: ROASTT, S: Scarecrow, V: VSTB
        public readonly string Venue;

        protected M2020UnifiedMeshProductId(string fullId, string producer,
                                            string meshProductType, string textureProductType,
                                            string cameras, string geometry, string version,
                                            int site, int drive, string spec, string eye, int sol,
                                            bool multiSol, bool multiSite, bool multiDrive, string meshId,
                                            string meshType, string frame, string resolution, int pyramid)
            : base(fullId, producer, meshProductType, textureProductType, cameras, geometry, version, site, drive,
                   spec, eye, sol, multiSol, multiSite, multiDrive, meshId)
        {
            this.MeshType = meshType;
            this.Frame = frame;
            this.Resolution = resolution;
            this.Pyramid = pyramid;
        }

        public static M2020UnifiedMeshProductId Parse(string productId)
        {
            productId = StringHelper.StripUrlExtension(productId);
            if (productId.Length < MIN_LENGTH || productId.Length > MAX_LENGTH)
            {
                return null;
            }

            int us = productId.IndexOf('_');
            if (us < 0)
            {
                return null;
            }
            
            string inst = productId.Substring(0, us);
            string eye = productId.Substring(us + 1, 1);
            string meshType = productId.Substring(us + 2, 1);
            string spec = productId.Substring(us + 3, 1);
            string solStr = productId.Substring(us + 4, 4);
            string multiSolStr = productId.Substring(us + 8, 1);
            string meshProductType = productId.Substring(us + 9, 3);
            string geom = productId.Substring(us + 12, 1);
            string frame = productId.Substring(us + 13, 1);
            string resolution = productId.Substring(us + 14, 1);
            string pyramidStr = productId.Substring(us + 15, 1);
            string venue = productId.Substring(us + 16, 1);
            string textureProductType = productId.Substring(us + 17, 3);
            string siteStr = productId.Substring(us + 20, 3);
            string multiSiteStr = productId.Substring(us + 23, 1);
            string driveStr = productId.Substring(us + 24, 4);
            string multiDriveStr = productId.Substring(us + 28, 1);
            string meshId = productId.Substring(us + 29, 7);
            string producer = productId.Substring(us + 36, 1);
            string ver = productId.Substring(us + 37, 2);

            int sol = M2020OPGSProductId.ParseSol(solStr);
            int site = ParseSite(siteStr);
            int drive = ParseDrive(driveStr);
            if (sol < 0 || site < 0 || drive < 0)
            {
                return null;
            }

            if (!int.TryParse(pyramidStr, out int pyramid))
            {
                return null;
            }

            if (!ParseFlag(multiSolStr, out bool multiSol) ||
                !ParseFlag(multiSiteStr, out bool multiSite) ||
                !ParseFlag(multiDriveStr, out bool multiDrive))
            {
                return null;
            }

            return new M2020UnifiedMeshProductId(fullId: productId, producer: venue, meshProductType: meshProductType,
                                                 textureProductType: textureProductType,
                                                 cameras: inst, geometry: geom, version: ver,
                                                 site: site, drive: drive, spec: spec, eye: eye, sol: sol,
                                                 multiSol: multiSol, multiSite: multiSite, multiDrive: multiDrive,
                                                 meshId: meshId, meshType: meshType, frame: frame,
                                                 resolution: resolution, pyramid: pyramid);
        }

        public override bool GetVersionSpan(out int start, out int length)
        {
            return GetSpan(37, 2, out start, out length);
        }

        public override bool GetProductTypeSpan(out int start, out int length)
        {
            return GetSpan(9, 3, out start, out length);
        }

        public override bool GetGeometrySpan(out int start, out int length)
        {
            return GetSpan(12, 1, out start, out length);
        }

        public override bool GetSizeSpan(out int start, out int length)
        {
            return GetSpan(15, 1, out start, out length);
        }

        public override bool GetSpecialProcessingSpan(out int start, out int length)
        {
            return GetSpan(3, 1, out start, out length);
        }

        public override bool GetProducerSpan(out int start, out int length)
        {
            return GetSpan(36, 1, out start, out length);
        }

        protected override RoverProductProducer ParseProducer(string producer, string camera)
        {
            return ParseM2020Producer(producer, camera);
        }

        protected override RoverProductCamera ParseCamera(char camera, char eyeChar)
        {
            var eye = ParseEye(eyeChar);
            switch (camera)
            {
                case 'F': return
                    eye == RoverStereoEye.Left ? RoverProductCamera.FrontHazcamLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.FrontHazcamRight :
                    RoverProductCamera.FrontHazcam;
                case 'B': return
                    eye == RoverStereoEye.Left ? RoverProductCamera.FrontHazcamLeftB :
                    eye == RoverStereoEye.Right ? RoverProductCamera.FrontHazcamRightB :
                    RoverProductCamera.FrontHazcamB;
                case 'R': return
                    eye == RoverStereoEye.Left ? RoverProductCamera.RearHazcamLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.RearHazcamRight : RoverProductCamera.RearHazcam;
                case 'N': return
                    eye == RoverStereoEye.Left ? RoverProductCamera.NavcamLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.NavcamRight :
                    RoverProductCamera.Navcam;
                case 'Z': return
                    eye == RoverStereoEye.Left ? RoverProductCamera.MastcamZLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.MastcamZRight :
                    RoverProductCamera.MastcamZ;
                case 'I': return
                    eye == RoverStereoEye.Left ? RoverProductCamera.SHERLOCWATSONLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.SHERLOCWATSONRight :
                    RoverProductCamera.SHERLOCWATSONRight;
                case 'C': return RoverProductCamera.SHERLOCACI;
                case 'O': return RoverProductCamera.Unknown; //orbiter
                case 'L': return RoverProductCamera.Unknown; //supercam RMI
                case 'P': return RoverProductCamera.Unknown; //PIXL
                case 'E': return RoverProductCamera.Unknown; //EDL camera
                case 'H': return RoverProductCamera.Unknown; //Mars Helicopter Scout Cam
                case 'V': return RoverProductCamera.Unknown; //Mars Helicopter Navigation Cam
                default: return RoverProductCamera.Unknown;
            }
        }
    }
}

using System;
using System.Linq;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class MSLOPGSProductId : OPGSProductId
    {
        public const int LENGTH = 36;

        public readonly string Config, Seqnum;
        public readonly long Sclk;

        protected MSLOPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                   string config, string version, string size, int site, int drive,
                                   string spec, long sclk, string seqnum)
            : base(fullId, producer, productType, camera, geometry, config, version, size, site, drive, spec)
        {
            this.Config = config;
            this.Sclk = sclk;
            this.Seqnum = seqnum;
        }

        public static MSLOPGSProductId Parse(string productId)
        {
            productId = StringHelper.StripUrlExtension(productId);
            if (productId.Length != LENGTH)
            {
                return null;
            }

            string inst = productId.Substring(0, 2);
            string config = productId.Substring(2, 1);
            string spec = productId.Substring(3, 1);
            string sclkStr = productId.Substring(4, 9);
            string prodType = productId.Substring(13, 3);
            string geom = productId.Substring(16, 1);
            string samp = productId.Substring(17, 1);
            string siteStr = productId.Substring(18, 3);
            string driveStr = productId.Substring(21, 4);
            string seqnum = productId.Substring(25, 9);
            string venue = productId.Substring(34, 1);
            string ver = productId.Substring(35, 1);

            int site = ParseSite(siteStr);
            int drive = ParseDrive(driveStr);
            if (site < 0 || drive < 0)
            {
                return null;
            }

            if (!ParseSclk(sclkStr, out long sclk))
            {
                return null;
            }

            return new MSLOPGSProductId(fullId: productId, producer: venue, productType: prodType, camera: inst,
                                        geometry: geom, config: config, version: ver, size: samp,
                                        site: site, drive: drive, spec: spec, sclk: sclk, seqnum: seqnum);
        }

        public static bool ParseSclk(string str, out long sclk)
        {
            sclk = -1;
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }
            if (Char.IsLetter(str, 0)) //1,099,999,999 - 3,599,999,999
            {
                char c = char.ToUpper(str[0]);
                str = str.Substring(1);
                if (str.Length != 8 || !long.TryParse(str, out sclk))
                {
                    return false;
                }
                sclk += (10 + (c - 'A')) * 100000000L;
            }
            else //0 - 999,999,999
            {
                if (!long.TryParse(str, out sclk))
                {
                    return false;
                }
            }
            return true;
        }

        protected override RoverProductProducer ParseProducer(string producer, string camera)
        {
            return ParseMSLProducer(producer);
        }

        protected override RoverProductColor ParseColor(string color, string camera)
        {
            var cam = RoverCamera.FromRDRInstrumentID(camera);
            if (RoverCamera.IsCamera(RoverProductCamera.Hazcam, cam) ||
                RoverCamera.IsCamera(RoverProductCamera.Navcam, cam))
            {
                return RoverProductColor.Grayscale;
            }
            else if (RoverCamera.IsCamera(RoverProductCamera.Mastcam, cam) ||
                     RoverCamera.IsCamera(RoverProductCamera.MAHLI, cam))
            {
                switch (color)
                {
                    case "F": return RoverProductColor.FullColor;
                    case "R": return RoverProductColor.Red;
                    case "G": return RoverProductColor.Green;
                    case "B": return RoverProductColor.Blue;
                    default: return RoverProductColor.Unknown;
                }
            }
            else
            {
                return RoverProductColor.Unknown;
            }
        }

        public override bool GetVersionSpan(out int start, out int length)
        {
            start = 35;
            length = 1;
            return true;
        }

        public override bool GetProductTypeSpan(out int start, out int length)
        {
            start = 13;
            length = 3;
            return true;
        }

        public override bool GetGeometrySpan(out int start, out int length)
        {
            start = 16;
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

        public override bool GetSizeSpan(out int start, out int length)
        {
            start = 17;
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
            start = 34;
            length = 1;
            return true;
        }

        public override bool HasSclk()
        {
            return true;
        }

        public override double GetSclk()
        {
            return Sclk;
        }
    }

    public class MSLUnifiedMeshProductId : UnifiedMeshProductIdBase
    {
        public const int MIN_LENGTH = 32;
        public const int MAX_LENGTH = 36;

        //F: full, S: subframe, D: downsample, M: mixed, T: thumbnail, B: bayer subsample, Y: bayer thumb, N: non-raster
        public readonly string Samp;

        protected MSLUnifiedMeshProductId(string fullId, string producer,
                                          string meshProductType, string textureProductType,
                                          string cameras, string geometry, string version, string samp,
                                          int site, int drive, string spec, string eye, int sol,
                                          bool multiSol, bool multiSite, bool multiDrive, string meshId)
            : base(fullId, producer, meshProductType, textureProductType, cameras, geometry,
                   version, site, drive, spec, eye, sol, multiSol, multiSite, multiDrive, meshId)
        {
            this.Samp = samp;
        }

        public static MSLUnifiedMeshProductId Parse(string productId)
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
            string solStr = productId.Substring(us + 2, 4);
            string multiSolStr = productId.Substring(us + 6, 1);
            string prodType = productId.Substring(us + 7, 3);
            string geom = productId.Substring(us + 10, 1);
            string samp = productId.Substring(us + 11, 1);
            string spec = productId.Substring(us + 12, 1);
            string siteStr = productId.Substring(us + 13, 3);
            string multiSiteStr = productId.Substring(us + 16, 1);
            string driveStr = productId.Substring(us + 17, 4);
            string multiDriveStr = productId.Substring(us + 21, 1);
            string meshId = productId.Substring(us + 22, 7);
            string venue = productId.Substring(us + 29, 1);
            string ver = productId.Substring(us + 30, 1);

            int sol = ParseSol(solStr);
            int site = ParseSite(siteStr);
            int drive = ParseDrive(driveStr);
            if (sol < 0 || site < 0 || drive < 0)
            {
                return null;
            }

            if (!ParseFlag(multiSolStr, out bool multiSol) ||
                !ParseFlag(multiSiteStr, out bool multiSite) ||
                !ParseFlag(multiDriveStr, out bool multiDrive))
            {
                return null;
            }

            return new MSLUnifiedMeshProductId(fullId: productId, producer: venue,
                                               meshProductType: "XYZ", textureProductType: prodType,
                                               cameras: inst, geometry: geom, version: ver, samp: samp,
                                               site: site, drive: drive, spec: spec, eye: eye, sol: sol,
                                               multiSol: multiSol, multiSite: multiSite, multiDrive: multiDrive,
                                               meshId: meshId);
        }

        //parse a 4 character sol string
        //returns integer in range [0,33999], -1 if invalid, 34000 if out of range
        //note: overflow above sol 9999 occurs after about 28 Earth years of operations
        //for testbed activities this will return the day of Earth year (DOY)
        //which can be substituted into paths like s3://BUCKET/ods/VER/YYYY/DOY/...
        public static int ParseSol(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return -1;
            }
            if (str.All(c => c == '_'))
            {
                return 34000;
            }
            if (Char.IsLetter(str, 0))
            {
                char c = char.ToUpper(str[0]);
                if (!int.TryParse(str.Substring(1), out int s))
                {
                    return -1;
                }
                if (c == 'Y' || c == 'Z') //testbed activity
                {
                    return /* 365 * (c - 'Y') + */ s; //just return day of year
                }
                else
                {
                    return 10000 + (c - 'A') * 1000 + s; //10000-33999
                }
            }
            return int.TryParse(str, out int sol) ? sol : -1; //0-9999
        }

        //format sol as a 4 digit number
        //note: if sol is greater than 9999 the return will have more than 4 digits
        public static string SolToString(int sol)
        {
            return string.Format("{0:D4}", sol);
        }

        public override bool GetVersionSpan(out int start, out int length)
        {
            return GetSpan(30, 1, out start, out length);
        }

        public override bool GetProductTypeSpan(out int start, out int length)
        {
            return GetSpan(7, 3, out start, out length);
        }

        public override bool GetGeometrySpan(out int start, out int length)
        {
            return GetSpan(10, 1, out start, out length);
        }

        public override bool GetSizeSpan(out int start, out int length)
        {
            return GetSpan(11, 1, out start, out length);
        }

        public override bool GetSpecialProcessingSpan(out int start, out int length)
        {
            return GetSpan(12, 1, out start, out length);
        }

        public override bool GetProducerSpan(out int start, out int length)
        {
            return GetSpan(29, 1, out start, out length);
        }

        protected override RoverProductProducer ParseProducer(string producer, string camera)
        {
            return ParseMSLProducer(producer);
        }

        protected override RoverProductCamera ParseCamera(char camera, char eyeChar)
        {
            var eye = ParseEye(eyeChar);
            switch (camera)
            {
                case 'F': return eye == RoverStereoEye.Left ? RoverProductCamera.FrontHazcamLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.FrontHazcamRight : RoverProductCamera.FrontHazcam;
                case 'R': return eye == RoverStereoEye.Left ? RoverProductCamera.RearHazcamLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.RearHazcamRight : RoverProductCamera.RearHazcam;
                case 'N': return eye == RoverStereoEye.Left ? RoverProductCamera.NavcamLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.NavcamRight : RoverProductCamera.Navcam;
                case 'M': return eye == RoverStereoEye.Left ? RoverProductCamera.MastcamLeft :
                    eye == RoverStereoEye.Right ? RoverProductCamera.MastcamRight : RoverProductCamera.Mastcam;
                case 'H': return RoverProductCamera.MAHLI;
                case 'O': return RoverProductCamera.Unknown; //orbiter
                case 'A': return RoverProductCamera.Unknown; //all six instruments
                default: return RoverProductCamera.Unknown;
            }
        }
    }

    public class MSLMSSSProductId : RoverProductId
    {
        public const int LENGTH = 30;

        public readonly string FullSeqId, SeqLine, CdpidCounter, CdpidComplete, GopCounter, ProcessingCode; 
        public readonly int Sol;
        public readonly bool Decompressed, RadiometricallyCalibrated, ColorCorrected;

        protected MSLMSSSProductId(string fullId, string camera, string geometry, string color, string version,
                                   int sol, string fullSeqId, string seqLine,
                                   string cdpidCounter, string cdpidComplete, string gopCounter, string processingCode)
            : base(fullId, null, RoverProductType.Image, camera, geometry, color, version)
        {
            this.Sol = sol;
            this.FullSeqId = fullSeqId;
            this.SeqLine = seqLine;
            this.CdpidCounter = cdpidCounter;
            this.CdpidComplete = cdpidComplete;
            this.GopCounter = gopCounter;
            this.ProcessingCode = processingCode;

            processingCode = processingCode.ToUpper();
            this.Decompressed = processingCode.Contains("D");
            this.RadiometricallyCalibrated = processingCode.Contains("R");
            this.ColorCorrected = processingCode.Contains("C");
        }

        public static MSLMSSSProductId Parse(string productId)
        {
            productId = StringHelper.StripUrlExtension(productId);
            if (productId.Length != LENGTH)
            {
                return null;
            }

            string solStr = productId.Substring(0, 4);
            string inst = productId.Substring(4, 2);
            string fullSeqId = productId.Substring(6, 6);
            string seqLine = productId.Substring(12, 3);
            string cdpidCounter = productId.Substring(15, 2);
            string cdpidComplete = productId.Substring(17, 5);
            string productType = productId.Substring(22, 1);
            string gopCounter = productId.Substring(23, 1);
            string version = productId.Substring(24, 1);
            string processingCode = productId.Substring(26, 4);

            if (!int.TryParse(solStr, out int sol))
            {
                return null;
            }

            return new MSLMSSSProductId(fullId: productId, camera: inst, geometry: processingCode, color: productType,
                                        version: version, sol: sol, fullSeqId: fullSeqId, seqLine: seqLine,
                                        cdpidCounter: cdpidCounter, cdpidComplete: cdpidComplete,
                                        gopCounter: gopCounter, processingCode: processingCode);
        }

        protected override RoverProductProducer ParseProducer(string producer, string camera)
        {
            return RoverProductProducer.MSSS;
        }

        protected override RoverProductType ParseProductType(string productType)
        {
            if (productType != null && productType.ToUpper() == RoverProduct.GetImageRDRType())
            {
                return RoverProductType.Image;
            }
            throw new NotImplementedException();
        }

        protected override RoverProductGeometry ParseGeometry(string geometry)
        {
            return geometry.ToUpper().Contains("L") ? RoverProductGeometry.Linearized : RoverProductGeometry.Raw;
        }

        protected override RoverProductColor ParseColor(string color, string camera)
        {
            switch (color.ToUpper())
            {
                case "D": return RoverProductColor.Grayscale;
                case "E": case "F": return RoverProductColor.FullColor;
                default: return RoverProductColor.Unknown;
            }
        }

        public override bool GetVersionSpan(out int start, out int length)
        {
            start = 24;
            length = 1;
            return true;
        }

        public override bool GetProductTypeSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public override bool GetGeometrySpan(out int start, out int length)
        {
            start = 26;
            length = 4;
            return true;
        }

        public override bool GetColorFilterSpan(out int start, out int length)
        {
            start = 22;
            length = 1;
            return true;
        }

        public override bool GetInstrumentSpan(out int start, out int length)
        {
            start = 4;
            length = 2;
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
    }
}

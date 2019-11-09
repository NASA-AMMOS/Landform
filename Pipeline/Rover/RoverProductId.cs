using System;
using System.Collections.Generic;
using System.Linq;
using OPS.Util;

namespace OPS.Pipeline
{
    public abstract class RoverProductId
    {
        public readonly string FullId;
        public readonly RoverProductProducer Producer;
        public readonly RoverProductType ProductType;
        public readonly RoverProductCamera Camera;
        public readonly RoverProductGeometry Geometry;
        public readonly RoverProductColor Color;
        public readonly int Version;

        protected RoverProductId(string fullId, RoverProductProducer producer, string productType, string camera,
                                 string geometry, string color, string version)
            //this doesn't work because can't call instance method ParseProductType() here
            //: this(fullId, producer, ParseProductType(productType), camera, geometry, color, version)
        {
            //this doesn't work because we want the class fields to be readonly
            //Init(fullId, producer, ParseProductType(productType), camera, geometry, color, version);

            //sigh
            this.FullId = fullId;
            this.Producer = producer;
            this.ProductType = ParseProductType(productType);
            this.Camera = ParseCamera(camera);
            this.Geometry = ParseGeometry(geometry);
            this.Color = ParseColor(color, camera);
            this.Version = ParseVersion(version);
        }

        protected RoverProductId(string fullId, RoverProductProducer producer, RoverProductType productType,
                                 string camera, string geometry, string color, string version)
        {
            this.FullId = fullId;
            this.Producer = producer;
            this.ProductType = productType;
            this.Camera = ParseCamera(camera);
            this.Geometry = ParseGeometry(geometry);
            this.Color = ParseColor(color, camera);
            this.Version = ParseVersion(version);
        }

        public static RoverProductId Parse(string id, MissionSpecific mission = null, bool throwOnFail = true)
        {
            id = StringHelper.GetLastUrlPathSegment(id, stripExtension: true);
            try
            {
                if (mission != null)
                {
                    return mission.ParseProductId(id);
                }
                else
                {
                    //MSL unified mesh IDs can be from 32 to 36 chars long
                    //Unfortunately regular MSL IDs are 36 chars long - first try as unified
                    //also, TODO for now the M2020 SIS for unified mesh product IDs is incomplete
                    //and M2020 datasets we're working with so far that have unified meshes seem to use the MSL format
                    //https://github.jpl.nasa.gov/OnSight/Landform/issues/793
                    if (id.Length >= MSLUnifiedMeshProductId.MIN_LENGTH &&
                        id.Length <= MSLUnifiedMeshProductId.MAX_LENGTH)
                    {
                        var unified = MSLUnifiedMeshProductId.Parse(id);
                        if (unified != null)
                        {
                            return unified;
                        }
                    }
                    
                    switch (id.Length)
                    {
                        case MSLOPGSProductId.LENGTH: return MSLOPGSProductId.Parse(id);
                        case MSLMSSSProductId.LENGTH: return MSLMSSSProductId.Parse(id);
                        case M2020OPGSProductId.LENGTH: return M2020OPGSProductId.Parse(id);
                        default: throw new Exception("unexpected length");
                    }
                }
            }
            catch (Exception ex)
            {
                if (throwOnFail)
                {
                    throw new Exception(string.Format("failed to parse product ID \"{0}\" (length {1}): {2}",
                                                      id, id.Length, ex.Message));
                }
                return null;
            }
        }

        public override string ToString()
        {
            return FullId;
        }

        public override int GetHashCode()
        {
            return FullId.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is RoverProductId && (obj as RoverProductId).FullId == FullId; 
        }

        public virtual bool IsSingleFrame()
        {
            return true;
        }

        public virtual bool IsSingleCamera()
        {
            return true;
        }

        public virtual bool IsSingleSiteDrive()
        {
            return true;
        }

        protected abstract RoverProductType ParseProductType(string productType);

        protected virtual RoverProductCamera ParseCamera(string camera)
        {
            return RoverCamera.FromRDRInstrumentID(camera);
        }

        protected abstract RoverProductGeometry ParseGeometry(string geometry);

        protected abstract RoverProductColor ParseColor(string color, string camera);

        /// <summary>
        /// MSL OPGS version is one digit in the range 1-9A-Z, or _ for 
        /// MSL MSSS version is one digit in the range 0-9A-Z, or _ for overflow
        /// M2020 OPGS version is two digits in the range '00'-'99''A0'-'ZZ' or '__' for overflow
        /// </summary>
        protected virtual int ParseVersion(string version)
        {
            int multiplier = 1;
            int value = 0;
            for (int i = version.Length - 1; i >= 0; i--)
            {
                char c = version[i];
                int placeVal = 0;
                if (c == '_') //technically the SIS implies that if any digit is '_' they all should be, but whatever
                {
                    placeVal = 36;
                }
                else if (char.IsDigit(c)) //'0' is invalid for MER OPGS, but valid for MER MSSS and M2020, so whatever
                {
                    placeVal = c - '0'; //0-9
                }
                else if (char.IsUpper(c))
                {
                    placeVal = 10 + c - 'A'; //10-35
                }
                else if (char.IsLower(c)) //SIS implies version should be upper case, but whatever
                {
                    placeVal = 10 + c - 'a'; //10-35
                }
                else
                {
                    throw new ArgumentException("error parsing rover product ID version '" + version + "'");
                }
                value += multiplier * placeVal;
                multiplier *= 10;
            }
            return value;
        }

        public virtual bool GetVersionSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual bool GetProductTypeSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual bool GetGeometrySpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual bool GetColorFilterSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual bool GetInstrumentSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public string GetPartialId(int start, int length)
        {
            return FullId.Substring(start, length);
        }

        public virtual string GetPartialId(bool includeVersion = true, bool includeProductType = true,
                                           bool includeGeometry = true, bool includeColorFilter = true,
                                           bool includeInstrument = true, bool includeVariants = true)
        {
            return GetPartialId(null, includeVersion, includeProductType, includeGeometry, includeColorFilter,
                                includeInstrument, includeVariants);
        }

        public virtual string GetPartialId(MissionSpecific mission, bool includeVersion = true,
                                           bool includeProductType = true, bool includeGeometry = true,
                                           bool includeColorFilter = true, bool includeInstrument = true,
                                           bool includeVariants = true)
        {
            string ret = FullId;
            int start, length;
            var spans = new List<int[]>();
            if (!includeVariants)
            {
                if (mission != null)
                {
                    spans.AddRange(mission.GetProductIdVariantSpans(this));
                }
                else
                {
                    includeVersion = false;
                }
            }
            if (!includeVersion && GetVersionSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeProductType && GetProductTypeSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeGeometry && GetGeometrySpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeColorFilter && GetColorFilterSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeInstrument && GetInstrumentSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            return StringHelper.RemoveMultiple(FullId, spans);
        }

        public virtual int GetSol()
        {
            throw new NotImplementedException();
        }
    }

    public abstract class OPGSProductId : RoverProductId
    {
        public readonly RoverProductSize Size;
        public readonly SiteDrive SiteDrive;

        protected OPGSProductId(string fullId, RoverProductProducer producer, string productType, string camera,
                                string geometry, string color, string version, string size, int site, int drive)
            : base(fullId, producer, productType, camera, geometry, color, version)
        {
            this.Size = ParseSize(size);
            this.SiteDrive = new SiteDrive(site, drive);
        }

        protected OPGSProductId(string fullId, RoverProductProducer producer, RoverProductType productType,
                                string camera, string geometry, string color, string version, string size,
                                int site, int drive)
            : base(fullId, producer, productType, camera, geometry, color, version)
        {
            this.Size = ParseSize(size);
            this.SiteDrive = new SiteDrive(site, drive);
        }

        public virtual bool GetSizeSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual string AsThumbnail()
        {
            if (!GetSizeSpan(out int start, out int length))
            {
                throw new NotImplementedException();
            }
            return FullId.Substring(0, start) + GetThumbnailString() + FullId.Substring(start + length);
        }

        protected virtual string GetThumbnailString()
        {
            throw new NotImplementedException();
        }

        protected abstract RoverProductSize ParseSize(string size);

        protected override RoverProductType ParseProductType(string productType)
        {
            return RoverProduct.FromRDRProductType(productType);
        }

        protected override RoverProductGeometry ParseGeometry(string geometry)
        {
            switch (geometry.ToUpper())
            {
                case "L": return RoverProductGeometry.Linearized;
                case "_": return RoverProductGeometry.Raw;
                default: return RoverProductGeometry.Unknown;
            }
        }
    }

    public abstract class MSLOPGSProductIdBase : OPGSProductId
    {
        public readonly string Spec;

        protected MSLOPGSProductIdBase(string fullId, string producer, string productType, string camera,
                                       string geometry, string color, string version, string size, int site, int drive,
                                       string spec)
            : base(fullId, ParseProducer(producer), productType, camera, geometry, color, version, size, site, drive)
        {
            this.Spec = spec;
        }

        protected MSLOPGSProductIdBase(string fullId, string producer, RoverProductType productType, string camera,
                                       string geometry, string color, string version, string size, int site, int drive,
                                       string spec)
            : base(fullId, ParseProducer(producer), productType, camera, geometry, color, version, size, site, drive)
        {
            this.Spec = spec;
        }

        protected override RoverProductSize ParseSize(string size)
        {
            switch (size.ToUpper())
            {
                case "F": case "S": return RoverProductSize.Regular;
                case "T": return RoverProductSize.Thumbnail;
                default: return RoverProductSize.Unknown;
            }
        }

        protected override string GetThumbnailString()
        {
            return "T";
        }

        protected static RoverProductProducer ParseProducer(string producer)
        {
            switch (producer.ToUpper())
            {
                case "M": return RoverProductProducer.OPGS;
                default: return RoverProductProducer.Unknown;
            }
        }
    }

    public class MSLOPGSProductId : MSLOPGSProductIdBase
    {
        public const int LENGTH = 36;

        public readonly string Config, Seqnum;
        public readonly int Sclk;

        protected MSLOPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                   string config, string version, string size, int site, int drive,
                                   string spec, int sclk, string seqnum)
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

            if (!int.TryParse(sclkStr, out int sclk) ||
                !int.TryParse(siteStr, out int site) ||
                !int.TryParse(driveStr, out int drive))
            {
                return null;
            }

            return new MSLOPGSProductId(fullId: productId, producer: venue, productType: prodType, camera: inst,
                                        geometry: geom, config: config, version: ver, size: samp,
                                        site: site, drive: drive, spec: spec, sclk: sclk, seqnum: seqnum);
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
    }

    public class MSLUnifiedMeshProductId : MSLOPGSProductIdBase
    {
        public const int MIN_LENGTH = 32;
        public const int MAX_LENGTH = 36;

        public readonly RoverProductCamera[] Cameras;
        public readonly RoverProductType TextureProductType;
        public readonly RoverStereoEye StereoEye;
        public readonly int Sol;
        public readonly bool MultiSol, MultiSite, MultiDrive;
        public readonly string MeshId;

        protected MSLUnifiedMeshProductId(string fullId, string producer, string textureProductType,
                                          string cameras, string geometry, string version, string size,
                                          int site, int drive, string spec, string eye, int sol,
                                          bool multiSol, bool multiSite, bool multiDrive, string meshId)
            : base(fullId, producer, RoverProductType.Points, cameras + eye, geometry, /* color */ "", version, size,
                   site, drive, spec)
        {
            this.Cameras = ParseCameras(cameras, eye);
            this.TextureProductType = ParseProductType(textureProductType);
            this.StereoEye = ParseEye(eye[0]);
            this.Sol = sol;
            this.MultiSol = multiSol;
            this.MultiSite = multiSite;
            this.MultiDrive = multiDrive;
            this.MeshId = meshId;
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

            if (!int.TryParse(solStr, out int sol) || //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/794
                !int.TryParse(siteStr, out int site) ||
                !int.TryParse(driveStr, out int drive))
            {
                return null;
            }

            bool parseFlag(string flag, out bool value)
            {
                value = false;
                switch (flag.ToUpper())
                {
                    case "_": return true;
                    case "X": value = true; return true;
                    default: return false;
                }
            }

            if (!parseFlag(multiSolStr, out bool multiSol) ||
                !parseFlag(multiSiteStr, out bool multiSite) ||
                !parseFlag(multiDriveStr, out bool multiDrive))
            {
                return null;
            }

            return new MSLUnifiedMeshProductId(fullId: productId, producer: venue, textureProductType: prodType,
                                               cameras: inst, geometry: geom, version: ver, size: samp,
                                               site: site, drive: drive, spec: spec, eye: eye, sol: sol,
                                               multiSol: multiSol, multiSite: multiSite, multiDrive: multiDrive,
                                               meshId: meshId);
        }

        public override bool IsSingleFrame()
        {
            return false;
        }

        public override bool IsSingleCamera()
        {
            return Cameras.Length == 1;
        }

        public override bool IsSingleSiteDrive()
        {
            return !MultiSite && !MultiDrive;
        }

        private RoverProductCamera[] ParseCameras(string cameras, string eye)
        {
            var ret = new List<RoverProductCamera>();
            foreach (char camera in (cameras ?? ""))
            {
                ret.Add(ParseCamera(camera, eye[0]));
            }
            return ret.ToArray();
        }

        protected override RoverProductCamera ParseCamera(string cameras)
        {
            if (string.IsNullOrEmpty(cameras) || cameras.Length < 2)
            {
                return RoverProductCamera.Unknown;
            }
            return ParseCamera(cameras[0], cameras[cameras.Length - 1]);
        }

        protected override RoverProductColor ParseColor(string color, string camera)
        {
            return RoverProductColor.Unknown;
        }

        public override bool GetVersionSpan(out int start, out int length)
        {
            start = length = -1;
            int us = FullId.IndexOf('_');
            if (us < 0)
            {
                return false;
            }
            start = us + 30;
            length = 1;
            return true;
        }

        public override bool GetProductTypeSpan(out int start, out int length)
        {
            //prodType in a unified mesh ID is actually the type of the texture product
            start = length = -1;
            return false;
        }

        public override bool GetGeometrySpan(out int start, out int length)
        {
            start = length = -1;
            int us = FullId.IndexOf('_');
            if (us < 0)
            {
                return false;
            }
            start = us + 10;
            length = 1;
            return true;
        }

        public override bool GetColorFilterSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public override bool GetInstrumentSpan(out int start, out int length)
        {
            start = length = -1;
            int us = FullId.IndexOf('_');
            if (us < 0)
            {
                return false;
            }
            start = 0;
            length = us + 2;
            return true;
        }

        public override bool GetSizeSpan(out int start, out int length)
        {
            start = length = -1;
            int us = FullId.IndexOf('_');
            if (us < 0)
            {
                return false;
            }
            start = us + 11;
            length = 1;
            return true;
        }

        public override int GetSol()
        {
            return Sol;
        }

        private RoverProductCamera ParseCamera(char camera, char eyeChar)
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

        private RoverStereoEye ParseEye(char eye)
        {
            switch (eye)
            {
                case 'L': return RoverStereoEye.Left;
                case 'R': return RoverStereoEye.Right;
                case 'M': return RoverStereoEye.Mono;
                case 'N': return RoverStereoEye.Any; //not applicable
                case 'X': return RoverStereoEye.Any; //mixed
                default: return RoverStereoEye.Any;
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
            : base(fullId, RoverProductProducer.MSSS, RoverProductType.Image, camera, geometry, color, version)
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

        protected override RoverProductType ParseProductType(string productType)
        {
            if (productType != null && productType.ToUpper() == "RAS")
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

        public override int GetSol()
        {
            return Sol;
        }
    }

    public class M2020OPGSProductId : OPGSProductId
    {
        public const int LENGTH = 54;

        public readonly string ColorFilter, Spec, Venue, Sequence, Camspec, Downsample, Compression;
        public readonly int Ts0, Ts1, Ts2;

        protected M2020OPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                     string color, string version, string size, int site, int drive,
                                     string spec, int ts0, string venue, int ts1, int ts2,
                                     string sequence, string camspec, string downsample, string compression)
            : base(fullId, ParseProducer(producer), productType, camera, geometry, color, version, size, site, drive)
        {
            this.ColorFilter = color;
            this.Spec = spec;
            this.Ts0 = ts0;
            this.Venue = venue;
            this.Ts1 = ts1;
            this.Ts2 = ts2;
            this.Sequence = sequence;
            this.Camspec = camspec;
            this.Downsample = downsample;
            this.Compression = compression;
        }

        public static M2020OPGSProductId Parse(string productId)
        {
            //NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00
            //| |||   ||         ||  |  | \   \  |        |   |  |
            //0 234   89        1920 23 26 27 31 35       44  48 51

            productId = StringHelper.StripUrlExtension(productId);
            if (productId.Length != LENGTH || productId[19] != '_')
            {
                return null;
            }

            string inst = productId.Substring(0, 2);
            string colorFilter = productId.Substring(2, 1);
            string spec = productId.Substring(3, 1);
            string ts0Str = productId.Substring(4, 4);
            string venue = productId.Substring(8, 1);
            string ts1Str = productId.Substring(9, 10);
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

            if (!int.TryParse(ts0Str, out int ts0) ||
                !int.TryParse(ts1Str, out int ts1) ||
                !int.TryParse(ts2Str, out int ts2) ||
                !int.TryParse(siteStr, out int site) |
                !int.TryParse(driveStr, out int drive))
            {
                return null;
            }

            return new M2020OPGSProductId(fullId: productId, producer: producer, productType: prodType, camera: inst,
                                          geometry: geometry, color: colorFilter, version: version, size: thumb,
                                          site: site, drive: drive, spec: spec, ts0: ts0, venue: venue, ts1: ts1,
                                          ts2: ts2, sequence: sequence, camspec: camspec, downsample: downsample,
                                          compression: compression);
        }

        public string GetConcatenatedTimeString()
        {
            return Ts0 + "_" + Ts1 + "_" + Ts2;
        }

        protected static RoverProductProducer ParseProducer(string producer)
        {
            switch (producer.ToUpper())
            {
                case "J": return RoverProductProducer.OPGS;
                default: return RoverProductProducer.Unknown;
            }
        }

        protected override RoverProductSize ParseSize(string size)
        {
            switch (size.ToUpper())
            {
                case "N": return RoverProductSize.Regular;
                case "T": return RoverProductSize.Thumbnail;
                default: return RoverProductSize.Unknown;
            }
        }

        protected override string GetThumbnailString()
        {
            return "T";
        }

        protected override RoverProductGeometry ParseGeometry(string geometry)
        {
            switch (geometry.ToUpper())
            {
                case "L": case "A": return RoverProductGeometry.Linearized;
                case "_": return RoverProductGeometry.Raw;
                default: return RoverProductGeometry.Unknown;
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

        public override bool GetSizeSpan(out int start, out int length)
        {
            start = 27;
            length = 1;
            return true;
        }

        public override int GetSol()
        {
            return Ts0;
        }
    }
}

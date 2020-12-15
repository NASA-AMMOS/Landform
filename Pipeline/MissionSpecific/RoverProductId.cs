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
            id = StringHelper.GetLastUrlPathSegment(id, stripExtension: true); //ok if id null or empty

            if (string.IsNullOrEmpty(id))
            {
                if (throwOnFail)
                {
                    throw new ArgumentException("null or empty product ID");
                }
                return null;
            }

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
                    if (id.Length >= MSLUnifiedMeshProductId.MIN_LENGTH &&
                        id.Length <= MSLUnifiedMeshProductId.MAX_LENGTH)
                    {
                        var unified = MSLUnifiedMeshProductId.Parse(id);
                        if (unified != null)
                        {
                            return unified;
                        }
                    }

                    //M2020 unified mesh IDs can be from 40 to 52 chars long
                    if (id.Length >= M2020UnifiedMeshProductId.MIN_LENGTH &&
                        id.Length <= M2020UnifiedMeshProductId.MAX_LENGTH)
                    {
                        var unified = M2020UnifiedMeshProductId.Parse(id);
                        if (unified != null)
                        {
                            return unified;
                        }
                    }
                    
                    switch (id.Length)
                    {
                        case MSLOPGSProductId.LENGTH: return MSLOPGSProductId.Parse(id); //36
                        case MSLMSSSProductId.LENGTH: return MSLMSSSProductId.Parse(id); //30
                        case M2020OPGSProductId.LENGTH: return M2020OPGSProductId.Parse(id); //54
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
        /// MSL OPGS version is one digit in the range 1-9A-Z, or _ for overflow
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

        public virtual bool GetStereoEyeSpan(out int start, out int length)
        {
            start = length = -1;
            if (RoverStereoPair.IsStereo(Camera) && GetInstrumentSpan(out start, out length))
            {
                start++;
                length = 1;
                return true;
            }
            return false;
        }

        public virtual bool GetStereoPartnerSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual bool GetSizeSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual bool GetMeshTypeSpan(out int start, out int length)
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
                                           bool includeInstrument = true, bool includeVariants = true,
                                           bool includeStereoEye = true, bool includeStereoPartner = true,
                                           bool includeSize = true, bool includeMeshType = true)
        {
            return GetPartialId(null,
                                includeVersion, includeProductType, includeGeometry, includeColorFilter,
                                includeInstrument, includeVariants, includeStereoEye, includeStereoPartner,
                                includeSize, includeMeshType);
        }

        public virtual string GetPartialId(MissionSpecific mission,
                                           bool includeVersion = true, bool includeProductType = true,
                                           bool includeGeometry = true, bool includeColorFilter = true,
                                           bool includeInstrument = true, bool includeVariants = true,
                                           bool includeStereoEye = true, bool includeStereoPartner = true,
                                           bool includeSize = true, bool includeMeshType = true)
        {
            string ret = FullId;
            int start, length;
            var spans = new List<int[]>();
            if (!includeVariants && mission != null)
            {
                spans.AddRange(mission.GetProductIdVariantSpans(this));
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
            if (includeInstrument && !includeStereoEye && GetStereoEyeSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeStereoPartner && GetStereoPartnerSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeSize && GetSizeSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeMeshType && GetMeshTypeSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            return StringHelper.RemoveMultiple(FullId, spans);
        }

        //enumerate all possible IDs matching this one with lesser or equal versions
        //in order of descending version (higher versions first)
        public IEnumerable<string> DescendingVersions(int offset = 0)
        {
            if (!GetVersionSpan(out int vs, out int vl))
            {
                yield return FullId;
                yield break;
            }
            string pfx = FullId.Substring(0, vs);
            string suffix = FullId.Substring(vs + vl);
            string fmt = "d" + vl;
            for (int v = int.Parse(FullId.Substring(vs, vl)) + offset; v >= 0; v--)
            {
                yield return pfx + v.ToString(fmt) + suffix;
            }
        }

        public virtual bool HasSol()
        {
            return false;
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
        public readonly String Spec;

        protected OPGSProductId(string fullId, RoverProductProducer producer, string productType, string camera,
                                string geometry, string color, string version, string size, int site, int drive,
                                string spec)
            : base(fullId, producer, productType, camera, geometry, color, version)
        {
            this.Size = ParseSize(size);
            this.SiteDrive = new SiteDrive(site, drive);
            this.Spec = spec;
        }

        protected OPGSProductId(string fullId, RoverProductProducer producer, RoverProductType productType,
                                string camera, string geometry, string color, string version, string size,
                                int site, int drive, string spec)
            : base(fullId, producer, productType, camera, geometry, color, version)
        {
            this.Size = ParseSize(size);
            this.SiteDrive = new SiteDrive(site, drive);
            this.Spec = spec;
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
            return "T";
        }

        protected static RoverProductProducer ParseMSLProducer(string producer)
        {
            switch (producer.ToUpper())
            {
                case "M": return RoverProductProducer.OPGS;
                default: return RoverProductProducer.Unknown;
            }
        }

        protected static RoverProductProducer ParseM2020Producer(string producer)
        {
            switch (producer.ToUpper())
            {
                case "J": return RoverProductProducer.OPGS;
                default: return RoverProductProducer.Unknown;
            }
        }

        protected virtual RoverProductSize ParseSize(string size)
        {
            switch (size.ToUpper())
            {
                case "F": case "S": case "": return RoverProductSize.Regular;
                case "T": return RoverProductSize.Thumbnail;
                default: return RoverProductSize.Unknown;
            }
        }

        protected override RoverProductType ParseProductType(string productType)
        {
            return RoverProduct.FromRDRProductType(productType);
        }

        protected override RoverProductGeometry ParseGeometry(string geometry)
        {
            if (string.IsNullOrEmpty(geometry) || geometry.Length != 1) {
                return RoverProductGeometry.Unknown;
            }

            //MSL cam SIS: If value is any alpha character "A - Z", then product is "linearized" using one of the two
            //modes (nominal or actual) ... If value is not any alpha character, then product is "non-linearized".

            //M20 cam SIS: _ : Non-linearized (raw geometry), L : Product has been linearized with nominal stereo
            //partner, A : Product has been linearized with an actual stereo partner

            return char.IsLetter(geometry[0]) ? RoverProductGeometry.Linearized : RoverProductGeometry.Raw;
        }

        //parse 3 character site string
        //returns an integer in the range [0,32767], -1 if invalid, 32768 if out of range
        public static int ParseSite(string str)
        {
            if (string.IsNullOrEmpty(str) || str.Length != 3)
            {
                return -1;
            }
            if (str.All(c => c == '_'))
            {
                return 32768;
            }
            if (char.IsLetter(str[0]) && char.IsDigit(str[1]) && char.IsDigit(str[2])) //1000-3599
            {
                if (int.TryParse(str.Substring(1), out int s))
                {
                    char c = char.ToUpper(str[0]);
                    return 1000 + (c - 'A') * 100 + s;
                }
                return -1;
            }
            if (char.IsLetter(str[0]) && char.IsLetter(str[1]) && char.IsDigit(str[2])) //3600-10359
            {
                if (int.TryParse(str.Substring(2), out int s))
                {
                    char c0 = char.ToUpper(str[0]);
                    char c1 = char.ToUpper(str[1]);
                    return 3600 + ((c0 - 'A') * 26 + (c1 - 'A')) * 10 + s;
                }
            }
            if (char.IsLetter(str[0]) && char.IsLetter(str[1]) && char.IsLetter(str[2])) //10360-27935
            {
                char c0 = char.ToUpper(str[0]);
                char c1 = char.ToUpper(str[1]);
                char c2 = char.ToUpper(str[2]);
                return 10360 + (c0 - 'A') * 26 * 26 + (c1 - 'A') * 26 + (c2 - 'A');
            }
            if (char.IsDigit(str[0]) && char.IsLetter(str[1]) && char.IsLetter(str[2])) //27936-32767
            {
                char c0 = str[0];
                char c1 = char.ToUpper(str[1]);
                char c2 = char.ToUpper(str[2]);
                return 27936 + (c0 - '0') * 26 * 26 + (c1 - 'A') * 26 + (c2 - 'A');
            }
            return int.TryParse(str, out int site) ? site : -1; //0-999
        }

        //parse 4 character drive string
        //returns an integer in the range [0,65535], -1 if invalid, 65536 if out of range
        public static int ParseDrive(string str)
        { 
            if (string.IsNullOrEmpty(str) || str.Length != 4)
            {
                return -1;
            }
            if (str.All(c => c == '_'))
            {
                return 65536;
            }
            if (char.IsLetter(str[0]) && char.IsDigit(str[1]) && char.IsDigit(str[2]) && char.IsDigit(str[3]))
            {
                //10000-35999
                char c = char.ToUpper(str[0]);
                if (int.TryParse(str.Substring(1), out int d))
                {
                    return 10000 + (c - 'A') * 1000 + d;
                }
            }
            if (char.IsLetter(str[0]) && char.IsLetter(str[1]) && char.IsDigit(str[2]) && char.IsDigit(str[3]))
            {
                //36000-65535
                char c0 = char.ToUpper(str[0]);
                char c1 = char.ToUpper(str[1]);
                if (int.TryParse(str.Substring(2), out int d))
                {
                    return 36000 + ((c0 - 'A') * 26 + (c1 - 'A')) * 100 + d;
                }
            }
            return int.TryParse(str, out int drive) ? drive : -1; //0-9999
        }

        //returns 3 character site string for input site in the range [0,32767]
        //returns 3 underscores if out of range
        public static string SiteToString(int site)
        {
            if (site < 0 || site > 32767)
            {
                return "___";
            }
            if (site >= 10360)
            {
                int s = site - (site >= 27936 ? 27936 : 10360);
                char c = site >= 27936 ? '0' : 'A';
                int s0 = s / (26 * 26);
                int s1 = (s - s0 * (26 * 26)) / 26;
                int s2 = s - s0 * (26 * 26) - s1 * 26;
                return string.Format("{0}{1}{2}", (char)(c + s0), (char)('A' + s1), (char)('A' + s2));
            }
            if (site >= 3600)
            {
                int d = (site / 10) - 360;
                int s0 = d / 26;
                int s1 = d - s0 * 26;
                int s = site - (3600 + (s0 * 26 + s1) * 10);
                return string.Format("{0}{1}{2:D1}", (char)('A' + s0), (char)('A' + s1), s);
            }
            if (site >= 1000)
            {
                int h = (site / 100) - 10;
                int s = site - (1000 + h * 100);
                return string.Format("{0}{1:D2}", (char)('A' + h), s);
            }
            return string.Format("{0:D3}", site);
        }

        //returns 4 character drive string for input drive in the range [0,65535]
        //returns 4 underscores if out of range
        public static string DriveToString(int drive)
        {
            if (drive < 0 || drive > 65535)
            {
                return "____";
            }
            if (drive >= 36000)
            {
                int h = (drive / 100) - 360;
                int h0 = h / 26;
                int h1 = h - h0 * 26;
                int d = drive - (36000 + (h0 * 26 + h1) * 100);
                return string.Format("{0}{1}{2:D2}", (char)('A' + h0), (char)('A' + h1), d);
            }
            if (drive >= 10000)
            {
                int k = (drive / 1000) - 10;
                int d = drive - (10000 + k * 1000);
                return string.Format("{0}{1:D3}", (char)('A' + k), d);
            }
            return string.Format("{0:D4}", drive);
        }
    }

    public class MSLOPGSProductId : OPGSProductId
    {
        public const int LENGTH = 36;

        public readonly string Config, Seqnum;
        public readonly int Sclk;

        protected MSLOPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                   string config, string version, string size, int site, int drive,
                                   string spec, int sclk, string seqnum)
            : base(fullId, ParseMSLProducer(producer), productType, camera, geometry, config, version, size,
                   site, drive, spec)
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

            if (!int.TryParse(sclkStr, out int sclk))
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

    public abstract class UnifiedMeshProductIdBase : OPGSProductId
    {
        public const RoverProductType OVERRIDE_PRODUCT_TYPE = RoverProductType.Points;

        public readonly RoverProductCamera[] Cameras;
        public readonly RoverProductType MeshProductType;
        public readonly RoverProductType TextureProductType;
        public readonly RoverStereoEye StereoEye;
        public readonly int Sol;
        public readonly bool MultiSol, MultiSite, MultiDrive;
        public readonly string MeshId;

        protected UnifiedMeshProductIdBase(string fullId, RoverProductProducer producer,
                                           string meshProductType, string textureProductType,
                                           string cameras, string geometry, string version,
                                           int site, int drive, string spec, string eye, int sol,
                                           bool multiSol, bool multiSite, bool multiDrive, string meshId)
            : base(fullId, producer, OVERRIDE_PRODUCT_TYPE, cameras + eye, geometry, /* color */ "", version,
                   /* size */ "", site, drive, spec)
        {
            this.Cameras = ParseCameras(cameras, eye);
            this.MeshProductType = ParseProductType(meshProductType);
            this.TextureProductType = ParseProductType(textureProductType);
            this.StereoEye = ParseEye(eye[0]);
            this.Sol = sol;
            this.MultiSol = multiSol;
            this.MultiSite = multiSite;
            this.MultiDrive = multiDrive;
            this.MeshId = meshId;
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

        protected virtual RoverProductCamera[] ParseCameras(string cameras, string eye)
        {
            var ret = new List<RoverProductCamera>();
            foreach (char camera in (cameras ?? ""))
            {
                ret.Add(ParseCamera(camera, eye[0]));
            }
            return ret.ToArray();
        }

        //needed to satisfy base class constructor
        //this will be passed the full cameras string with the eye string appended
        //it will just parse the first camera
        protected override RoverProductCamera ParseCamera(string camera)
        {
            if (string.IsNullOrEmpty(camera) || camera.Length < 2)
            {
                return RoverProductCamera.Unknown;
            }
            return ParseCamera(camera[0], camera[camera.Length - 1]);
        }

        protected override RoverProductColor ParseColor(string color, string camera)
        {
            return RoverProductColor.Unknown;
        }

        protected bool GetSpan(int startAfterFirstUnderscore, int len, out int start, out int length)
        {
            start = length = -1;
            int us = FullId.IndexOf('_');
            if (us < 0)
            {
                return false;
            }
            start = us + startAfterFirstUnderscore;
            length = len;
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
            length = us + 2; //all the inst chars, plus the underscore, plus the eye char
            return true;
        }

        public override bool GetStereoEyeSpan(out int start, out int length)
        {
            return GetSpan(1, 1, out start, out length);
        }

        public override bool HasSol()
        {
            return true;
        }

        public override int GetSol()
        {
            return Sol;
        }

        protected abstract RoverProductCamera ParseCamera(char camera, char eyeChar);

        protected virtual RoverStereoEye ParseEye(char eye)
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

        protected static bool ParseFlag(string flag, out bool value)
        {
            value = false;
            switch (flag.ToUpper())
            {
                case "_": return true;
                case "X": value = true; return true;
                default: return false;
            }
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
            : base(fullId, ParseMSLProducer(producer), meshProductType, textureProductType, cameras, geometry,
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

        public override bool HasSol()
        {
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

        public readonly string ColorFilter, Venue, Sequence, Camspec, Downsample, Compression, MeshType;
        public readonly int Ts0, Ts1, Ts2;

        protected M2020OPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                     string color, string version, string size, int site, int drive,
                                     string spec, int ts0, string venue, int ts1, int ts2,
                                     string sequence, string camspec, string downsample, string compression,
                                     string meshType)
            : base(fullId, ParseM2020Producer(producer), productType, camera, geometry, color, version, size,
                   site, drive, spec)
        {
            this.ColorFilter = color;
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

            int ts0 = ParseSol(ts0Str);
            int site = ParseSite(siteStr);
            int drive = ParseDrive(driveStr);
            if (ts0 < 0 || site < 0 || drive < 0)
            {
                return null;
            }

            if (!int.TryParse(ts1Str, out int ts1) || !int.TryParse(ts2Str, out int ts2))
            {
                return null;
            }

            return new M2020OPGSProductId(fullId: productId, producer: producer, productType: prodType, camera: inst,
                                          geometry: geometry, color: colorFilter, version: version, size: thumb,
                                          site: site, drive: drive, spec: spec, ts0: ts0, venue: venue, ts1: ts1,
                                          ts2: ts2, sequence: sequence, camspec: camspec, downsample: downsample,
                                          compression: compression, meshType: meshType);
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

        public string GetConcatenatedTimeString()
        {
            return Ts0 + "_" + Ts1 + "_" + Ts2;
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

        public override bool HasSol()
        {
            return true;
        }

        public override int GetSol()
        {
            return Ts0;
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
            : base(fullId, ParseM2020Producer(producer), meshProductType, textureProductType, cameras, geometry,
                   version, site, drive, spec, eye, sol, multiSol, multiSite, multiDrive, meshId)
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

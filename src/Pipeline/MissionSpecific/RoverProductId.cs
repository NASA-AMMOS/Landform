using System;
using System.Collections.Generic;
using System.Linq;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class RoverProductIdTemporalComparer : IComparer<RoverProductId>
    {
        private readonly int flip;

        public RoverProductIdTemporalComparer(bool reverse) {
            flip = reverse ? -1 : 1;
        }

        //-1 if a is earlier in time than b
        //+1 if a is later in time than b
        //0 if a and b are at the same time or are incommensurate in time
        public int Compare(RoverProductId a, RoverProductId b)
        {
            if (a.HasSclk() && b.HasSclk())
            {
                return flip * Math.Sign(a.GetSclk() - b.GetSclk());
            }
            else if (a.HasSol() && b.HasSol())
            {
                return flip * Math.Sign(a.GetSol() - b.GetSol());
            }
            return 0;
        }
    }

    public abstract class RoverProductId
    {
        public readonly string FullId;
        public readonly RoverProductProducer Producer;
        public readonly RoverProductType ProductType;
        public readonly RoverProductCamera Camera;
        public readonly RoverProductGeometry Geometry;
        public readonly RoverProductColor Color;
        public readonly int Version;

        protected RoverProductId(string fullId, string producer, string productType, string camera,
                                 string geometry, string color, string version)
            //this doesn't work because can't call instance method ParseProductType() here
            //: this(fullId, producer, ParseProductType(productType), camera, geometry, color, version)
        {
            //this doesn't work because we want the class fields to be readonly
            //Init(fullId, producer, ParseProductType(productType), camera, geometry, color, version);

            //sigh
            this.FullId = fullId;
            this.Producer = ParseProducer(producer, camera);
            this.ProductType = ParseProductType(productType);
            this.Camera = ParseCamera(camera);
            this.Geometry = ParseGeometry(geometry);
            this.Color = ParseColor(color, camera);
            this.Version = ParseVersion(version);
        }

        protected RoverProductId(string fullId, string producer, RoverProductType productType,
                                 string camera, string geometry, string color, string version)
        {
            this.FullId = fullId;
            this.Producer = ParseProducer(producer, camera);
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

        protected abstract RoverProductProducer ParseProducer(string producer, string camera);

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

        public virtual bool GetSpecialProcessingSpan(out int start, out int length)
        {
            start = length = -1;
            return false;
        }

        public virtual bool GetProducerSpan(out int start, out int length)
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
                                           bool includeSize = true, bool includeMeshType = true,
                                           bool includeSpecialProcessing = true, bool includeProducer = true)
        {
            return GetPartialId(null,
                                includeVersion, includeProductType, includeGeometry, includeColorFilter,
                                includeInstrument, includeVariants, includeStereoEye, includeStereoPartner,
                                includeSize, includeMeshType, includeSpecialProcessing, includeProducer);
        }

        public virtual string GetPartialId(MissionSpecific mission,
                                           bool includeVersion = true, bool includeProductType = true,
                                           bool includeGeometry = true, bool includeColorFilter = true,
                                           bool includeInstrument = true, bool includeVariants = true,
                                           bool includeStereoEye = true, bool includeStereoPartner = true,
                                           bool includeSize = true, bool includeMeshType = true,
                                           bool includeSpecialProcessing = true, bool includeProducer = true)
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
            if (!includeSpecialProcessing && GetSpecialProcessingSpan(out start, out length))
            {
                spans.Add(new int[] { start, length });
            }
            if (!includeProducer && GetProducerSpan(out start, out length))
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

        public virtual bool HasSclk()
        {
            return false;
        }

        //in seconds
        public virtual double GetSclk()
        {
            throw new NotImplementedException();
        }
    }

    public abstract class OPGSProductId : RoverProductId
    {
        public readonly RoverProductSize Size;
        public readonly SiteDrive SiteDrive;
        public readonly String Spec;

        protected OPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                string color, string version, string size, int site, int drive, string spec)
            : base(fullId, producer, productType, camera, geometry, color, version)
        {
            this.Size = ParseSize(size);
            this.SiteDrive = new SiteDrive(site, drive);
            this.Spec = spec;
        }

        protected OPGSProductId(string fullId, string producer, RoverProductType productType, string camera,
                                string geometry, string color, string version, string size, int site, int drive,
                                string spec)
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

        protected RoverProductProducer ParseMSLProducer(string producer)
        {
            switch (producer.ToUpper())
            {
                case "M": return RoverProductProducer.OPGS;
                default: return RoverProductProducer.Unknown;
            }
        }

        protected RoverProductProducer ParseM2020Producer(string producer, string camera)
        {
            switch (producer.ToUpper())
            {
                case "J": return RoverProductProducer.OPGS;
                case "A": return RoverProductProducer.ASU;
                case "P":
                {
                    switch (ParseCamera(camera))
                    {
                        case RoverProductCamera.MastcamZLeft: case RoverProductCamera.MastcamZRight:
                            return RoverProductProducer.ASU;
                        case RoverProductCamera.SupercamRMI: return RoverProductProducer.IRAP;
                        case RoverProductCamera.MEDASkycam: return RoverProductProducer.SMES;
                        default: return RoverProductProducer.OPGS;
                    }
                }
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

        protected UnifiedMeshProductIdBase(string fullId, string producer,
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
}

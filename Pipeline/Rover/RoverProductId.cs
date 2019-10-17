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
        public readonly int Version;

        protected RoverProductId(string fullId, RoverProductProducer producer, string productType, string camera,
                                 string geometry, string version)
        {
            this.FullId = fullId;
            this.Producer = producer;
            this.ProductType = ParseProductType(productType);
            this.Camera = ParseCamera(camera);
            this.Geometry = ParseGeometry(geometry);
            this.Version = ParseVersion(version);
        }

        protected RoverProductId(string fullId, RoverProductProducer producer, RoverProductType productType,
                                 string camera, string geometry, string version)
        {
            this.FullId = fullId;
            this.Producer = producer;
            this.ProductType = productType;
            this.Camera = ParseCamera(camera);
            this.Geometry = ParseGeometry(geometry);
            this.Version = ParseVersion(version);
        }

        public static RoverProductId Parse(string productId)
        {
            string basename = StringHelper.StripUrlExtension(productId);
            switch (basename.Length)
            {
                case MSLOPGSProductId.LENGTH: return MSLOPGSProductId.Parse(basename);
                case MSLMSSSProductId.LENGTH: return MSLMSSSProductId.Parse(basename);
                case M2020OPGSProductId.LENGTH: return M2020OPGSProductId.Parse(basename);
                default: return null;
            }
        }

        public override string ToString()
        {
            return FullId;
        }

        protected abstract RoverProductType ParseProductType(string productType);

        protected virtual RoverProductCamera ParseCamera(string camera)
        {
            return RoverCamera.FromRDRInstrumentID(camera);
        }

        protected abstract RoverProductGeometry ParseGeometry(string geometry);

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
    }

    public abstract class OPGSProductId : RoverProductId
    {
        public readonly RoverProductSize Size;
        public readonly SiteDrive SiteDrive;

        protected OPGSProductId(string fullId, RoverProductProducer producer, string productType, string camera,
                                string geometry, string version, string size, int site, int drive)
            : base(fullId, producer, productType, camera, geometry, version)
        {
            this.Size = ParseSize(size);
            this.SiteDrive = new SiteDrive(site, drive);
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

    public class MSLOPGSProductId : OPGSProductId
    {
        public const int LENGTH = 36;

        public readonly string Config, Spec, Seqnum;
        public readonly int Sclk;

        protected MSLOPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                   string version, string size, int site, int drive,
                                   string config, string spec, int sclk, string seqnum)
            : base(fullId, ParseProducer(producer), productType, camera, geometry, version, size, site, drive)
        {
            this.Config = config;
            this.Spec = spec;
            this.Sclk = sclk;
            this.Seqnum = seqnum;
        }

        public static new MSLOPGSProductId Parse(string productId)
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
                                        geometry: geom, version: ver, size: samp, site: site, drive: drive,
                                        config: config, spec: spec, sclk: sclk, seqnum: seqnum);
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

        protected static RoverProductProducer ParseProducer(string producer)
        {
            switch (producer.ToUpper())
            {
                case "M": return RoverProductProducer.OPGS;
                default: return RoverProductProducer.Unknown;
            }
        }
    }

    public enum MSSSProductType
    {
        JPEGGrayscale,
        JPEGColor,
        Unknown
    }

    public class MSLMSSSProductId : RoverProductId
    {
        public const int LENGTH = 30;

        public readonly MSSSProductType MSSSProductType;
        public readonly string FullSeqId, SeqLine, CdpidCounter, CdpidComplete, GopCounter, ProcessingCode; 
        public readonly int Sol;
        public readonly bool Decompressed, RadiometricallyCalibrated, ColorCorrected;

        protected MSLMSSSProductId(string fullId, string productType, string camera, string geometry, string version, 
                                   int sol, string fullSeqId, string seqLine,
                                   string cdpidCounter, string cdpidComplete, string gopCounter, string processingCode)
            : base(fullId, RoverProductProducer.MSSS, RoverProductType.Image, camera, geometry, version)
        {
            this.MSSSProductType = ParseMSSSProductType(productType);
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

        public static new MSLMSSSProductId Parse(string productId)
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

            return new MSLMSSSProductId(fullId: productId, productType: productType, camera: inst,
                                        geometry: processingCode, version: version, sol: sol,
                                        fullSeqId: fullSeqId, seqLine: seqLine,
                                        cdpidCounter: cdpidCounter, cdpidComplete: cdpidComplete,
                                        gopCounter: gopCounter, processingCode: processingCode);
        }

        protected override RoverProductType ParseProductType(string productType)
        {
            throw new NotImplementedException();
        }

        protected override RoverProductGeometry ParseGeometry(string geometry)
        {
            return geometry.ToUpper().Contains("L") ? RoverProductGeometry.Linearized : RoverProductGeometry.Raw;
        }

        protected MSSSProductType ParseMSSSProductType(string productType)
        {
            switch (productType.ToUpper())
            {
                case "D": return MSSSProductType.JPEGGrayscale;
                case "E": case "F": return MSSSProductType.JPEGColor;
                default: return MSSSProductType.Unknown;
            }
        }
    }

    public class M2020OPGSProductId : OPGSProductId
    {
        public const int LENGTH = 54;

        public readonly string ColorFilter, Spec, Venue, Sequence, Camspec, Downsample, Compression;
        public readonly int Ts0, Ts1, Ts2;

        protected M2020OPGSProductId(string fullId, string producer, string productType, string camera, string geometry,
                                     string version, string size, int site, int drive,
                                     string colorFilter, string spec, int ts0, string venue, int ts1, int ts2,
                                     string sequence, string camspec, string downsample, string compression)
            : base(fullId, ParseProducer(producer), productType, camera, geometry, version, size, site, drive)
        {
            this.ColorFilter = colorFilter;
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

        public static new M2020OPGSProductId Parse(string productId)
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
                                          geometry: geometry, version: version, size: thumb, site: site, drive: drive,
                                          colorFilter: colorFilter, spec: spec, ts0: ts0, venue: venue, ts1: ts1,
                                          ts2: ts2, sequence: sequence, camspec: camspec, downsample: downsample,
                                          compression: compression);
        }

        public string GetConcatenatedTimeString()
        {
            return Ts0 + "_" + Ts1 + "_" + Ts2;
        }

        public int GetDayNumber()
        {
            return Ts0;
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
                default: return RoverProductSize.Thumbnail;
            }
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
    }
}

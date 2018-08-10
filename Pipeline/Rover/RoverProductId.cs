using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    
    public class RoverProductId
    {
        public string fullIdString = null;
        protected string inst = null,
                         version = null;
        protected static Dictionary<string, RoverProductCamera> instToCamera;

        static RoverProductId()
        {
            instToCamera = new Dictionary<string, RoverProductCamera>();
            instToCamera.Add("FL", RoverProductCamera.FrontHazcamLeft);
            instToCamera.Add("FR", RoverProductCamera.FrontHazcamRight);
            instToCamera.Add("MH", RoverProductCamera.MAHLI);
            instToCamera.Add("ML", RoverProductCamera.MastcamLeft);
            instToCamera.Add("MR", RoverProductCamera.MastcamRight);
            instToCamera.Add("NL", RoverProductCamera.NavcamLeft);
            instToCamera.Add("NR", RoverProductCamera.NavcamRight);
            instToCamera.Add("RL", RoverProductCamera.RearHazcamLeft);
            instToCamera.Add("RR", RoverProductCamera.RearHazcamRight);
        }

        public virtual RoverProductProducer Producer
        {
            get
            {
                return RoverProductProducer.Unknown;
            }
        }

        public virtual RoverProductGeometry Geometry
        {
            get
            {
                return RoverProductGeometry.Unknown;
            }
        }

        public virtual RoverProductType ProductType
        {
            get
            {
                return RoverProductType.Unknown;
            }
        }

        public string Version
        {
            get { return version; }
        }

        public RoverProductCamera Camera
        {
            get
            {
                if (instToCamera.ContainsKey(inst))
                {
                    return instToCamera[inst];
                }
                return RoverProductCamera.Unknown;
            }
        }

        
        public static RoverProductId ParseFromString(string productId)
        {
            RoverProductId result = OPGSProductId.ParseFromOPGSName(productId);
            if(result == null)
            {
                result = MSSSProductId.ParseFromMSSS(productId);
            }
            return result;
        }
    }

    public class OPGSProductId : RoverProductId
    {
        protected string config = null,
                         spec = null,
                         sclk = null,
                         prodid = null,
                         geometry = null,
                         samp = null,
                         site = null,
                         drive = null,
                         seqnum = null,
                         venue = null;

        protected static Dictionary<string, RoverProductType> prodToType;

        static OPGSProductId()
        {
            prodToType = new Dictionary<string, RoverProductType>();
            prodToType.Add("RAS", RoverProductType.Image);
            prodToType.Add("RNG", RoverProductType.Range);
            prodToType.Add("XYZ", RoverProductType.XYZ);
            prodToType.Add("UVW", RoverProductType.NormalMap); //TODO: check
        }

        public override RoverProductProducer Producer
        {
            get
            {
                return RoverProductProducer.OPGS;
            }
        }

        public override RoverProductGeometry Geometry
        {
            get
            {
                if (geometry.ToUpper().Equals("L"))
                {
                    return RoverProductGeometry.Linearized;
                }
                if (geometry.Equals("_"))
                {
                    return RoverProductGeometry.Raw;
                }
                return RoverProductGeometry.Unknown;
            }
        }

        public override RoverProductType ProductType
        {
            get
            {
                if(prodToType.ContainsKey(prodid))
                {
                    return prodToType[prodid];
                }
                return RoverProductType.Unknown;
            }
        }

        public RoverProductSize Size
        {
            get
            {
                if(samp.ToUpper().Equals("F") || samp.ToUpper().Equals("S"))
                {
                    return RoverProductSize.Regular;
                }
                if(samp.ToUpper().Equals("T"))
                {
                    return RoverProductSize.Thumbnail;
                }
                return RoverProductSize.Unknown;
            }
        }

        public static OPGSProductId ParseFromOPGSName(string productId)
        {
            if(productId.EndsWith(".IMG"))
            {
                productId= productId.Replace(".IMG", "");
            }
            if(productId.Length != 36)
            {
                return null;
            }
            OPGSProductId id = new OPGSProductId();
            id.fullIdString = productId;

            id.inst = productId.Substring(0, 2);
            id.config = productId.Substring(2, 1);
            id.spec = productId.Substring(3, 1);
            id.sclk = productId.Substring(4, 9);
            id.prodid = productId.Substring(13, 3);
            id.geometry = productId.Substring(16, 1);
            id.samp = productId.Substring(17, 1);
            id.site = productId.Substring(18, 3);
            id.drive = productId.Substring(21, 4);
            id.seqnum = productId.Substring(25, 9);
            id.venue = productId.Substring(34, 1);
            id.version = productId.Substring(35, 1);
            return id;
        }
    }

    public enum MSSSProductType
    {
        JPEGGrayscale,
        JPEGColor,
        Unknown
    }

    public class MSSSProductId : RoverProductId
    {

        protected string sol,
                         fullSeqId,
                         seqLine,
                         cdpidCounter,
                         cdpidComplete,
                         productType,
                         gopCounter,
                         processingCode; 
        public override RoverProductProducer Producer
        {
            get
            {
                return RoverProductProducer.MSSS;
            }
        }
                
        public bool Decompressed
        {
            get
            {
                return processingCode.ToUpper().Contains("D");
            }
        }
        public bool RadiometricallyCalibrated
        {
            get
            {
                return processingCode.ToUpper().Contains("R");
            }
        }

        public bool ColorCorrected
        {
            get
            {
                return processingCode.ToUpper().Contains("C");
            }
        }

        public override RoverProductGeometry Geometry
        {
            get
            {
                if (processingCode.ToUpper().Contains("L"))
                {
                    return RoverProductGeometry.Linearized;
                }
                return RoverProductGeometry.Raw;
            }
        }

        public override RoverProductType ProductType
        {
            get
            {
                return RoverProductType.Image;
            }
        }

        public MSSSProductType MSSSProductType
        {
            get
            {
                string t = this.productType.ToUpper();
                if (t == "D")
                {
                    return MSSSProductType.JPEGGrayscale;
                }
                else if (t=="E" || t == "F")
                {
                    return MSSSProductType.JPEGColor;
                }
                return MSSSProductType.Unknown;
            }
        }

        public static MSSSProductId ParseFromMSSS(string productId)
        {
            if (productId.EndsWith(".IMG"))
            {
                productId = productId.Replace(".IMG", "");
            }
            if (productId.Length != 30)
            {
                return null;
            }
            MSSSProductId id = new MSSSProductId();
            id.fullIdString = productId;
            id.sol = productId.Substring(0, 4);
            id.inst = productId.Substring(4, 2);
            id.fullSeqId = productId.Substring(6, 6);
            id.seqLine = productId.Substring(12, 3);
            id.cdpidCounter = productId.Substring(15, 2);
            id.cdpidComplete = productId.Substring(17, 5);
            id.productType = productId.Substring(22, 1);
            id.gopCounter = productId.Substring(23, 1);
            id.version = productId.Substring(24, 1);
            id.processingCode = productId.Substring(26, 4);
            return id;
        }
    }
}

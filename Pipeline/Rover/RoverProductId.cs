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
        protected string inst = null, version = null;
        protected static Dictionary<string, RoverProductCamera> instToCamera;

        static RoverProductId()
        {
            instToCamera = new Dictionary<string, RoverProductCamera>();

            //common
            instToCamera.Add("FL", RoverProductCamera.FrontHazcamLeft);
            instToCamera.Add("FR", RoverProductCamera.FrontHazcamRight);
            instToCamera.Add("ML", RoverProductCamera.MastcamLeft);
            instToCamera.Add("MR", RoverProductCamera.MastcamRight);
            instToCamera.Add("NL", RoverProductCamera.NavcamLeft);
            instToCamera.Add("NR", RoverProductCamera.NavcamRight);
            instToCamera.Add("RL", RoverProductCamera.RearHazcamLeft);
            instToCamera.Add("RR", RoverProductCamera.RearHazcamRight);

            //MSL
            instToCamera.Add("MH", RoverProductCamera.MAHLI);

            //M2020
            instToCamera.Add("BL", RoverProductCamera.FrontHazcamLeftB);
            instToCamera.Add("BR", RoverProductCamera.FrontHazcamRightB);
            //ML and MR are re-used as MastcamZLeft and MastcamZRight
            instToCamera.Add("CC", RoverProductCamera.CacheCam); 
            instToCamera.Add("EA", RoverProductCamera.EDLPUCA); 
            instToCamera.Add("EB", RoverProductCamera.EDLPUCB);
            instToCamera.Add("EC", RoverProductCamera.EDLPUCC);
            instToCamera.Add("ED", RoverProductCamera.EDLRDC);
            instToCamera.Add("EL", RoverProductCamera.EDLLVS);
            instToCamera.Add("ES", RoverProductCamera.EDLDSD);
            instToCamera.Add("EU", RoverProductCamera.EDLRUC);
            instToCamera.Add("HN", RoverProductCamera.HeliNav);
            instToCamera.Add("HS", RoverProductCamera.HeliScout);
            instToCamera.Add("MS", RoverProductCamera.MEDASkyCam);
            instToCamera.Add("PC", RoverProductCamera.PIXELMCC);
            instToCamera.Add("SC", RoverProductCamera.SHERLOCACI);
            instToCamera.Add("IL", RoverProductCamera.SHERLOCWATSONLeft);
            instToCamera.Add("IR", RoverProductCamera.SHERLOCWATSONRight);
            instToCamera.Add("SR", RoverProductCamera.SuperCamRMI);
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
            RoverProductId result = MSLOPGSProductId.ParseFromOPGSName(productId);
            if (result == null)
            {
                result = MSSSProductId.ParseFromMSSS(productId);
            }
            if(result == null)
            {
                result = M2020OPGSProductId.ParseFromM2020Name(productId);
            }
            return result;
        }
    }


    public class OPGSProductId : RoverProductId
    {
        protected static Dictionary<string, RoverProductType> prodToType;
        protected string prodType = null,
                         geometry = null;


        static OPGSProductId()
        {
            prodToType = new Dictionary<string, RoverProductType>();
            prodToType.Add("RAS", RoverProductType.Image);
            prodToType.Add("RNG", RoverProductType.Range);
            prodToType.Add("XYZ", RoverProductType.XYZ);
            prodToType.Add("UVW", RoverProductType.NormalMap);
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
                if (prodToType.ContainsKey(prodType))
                {
                    return prodToType[prodType];
                }
                return RoverProductType.Unknown;
            }
        }

        public virtual RoverProductSize Size
        {
            get { return RoverProductSize.Unknown; }
        }
    }

    public class M2020OPGSProductId : OPGSProductId
    {

        protected string colorFilter = null,
                 spec = null,
                 ts0 = null,
                 venue = null,
                 ts1 = null,
                 ts2 = null,
                 thumb = null,
                 site = null,
                 drive = null,
                 sequence = null,
                 camspec = null,
                 downsample = null,
                 compression = null,
                 producer = null;

        public static M2020OPGSProductId ParseFromM2020Name(string productId)
        {
            //NLF_0102R0102125109_000UVWLN0010030NCAM03102_0A00AAJ01.IMG
            //|  |    |          |   |  |    |   |        |   |  |
            //0  3    8          19  23 26   31  35       44  48 51
            if (productId.EndsWith(".IMG"))
            {
                productId = productId.Replace(".IMG", "");
            }
            if (productId.Length != 54)
            {
                return null;
            }
            M2020OPGSProductId id = new M2020OPGSProductId();
            id.fullIdString = productId;

            id.inst = productId.Substring(0, 2);
            id.colorFilter = productId.Substring(2, 1);
            id.spec = productId.Substring(3, 1);
            id.ts0 = productId.Substring(4, 4);
            id.venue = productId.Substring(8, 1);
            id.ts1 = productId.Substring(9, 10);
            if(productId[19] != '_')
            {
                return null;
            }
            id.ts2 = productId.Substring(20, 3);
            id.prodType = productId.Substring(23, 3);
            id.geometry = productId.Substring(26, 1);
            id.thumb = productId.Substring(27, 1);
            id.site = productId.Substring(28, 3);
            id.drive = productId.Substring(31, 4);
            id.sequence = productId.Substring(35, 9);
            id.camspec = productId.Substring(44, 4);
            id.downsample = productId.Substring(48, 1);
            id.compression = productId.Substring(49,2);
            id.producer = productId.Substring(51, 1);
            id.version = productId.Substring(52, 2);

            // TODO: handle BL and BR front hazcam codes
            return id;
        }

        //ROASTT: spacecraft clock not incrementing
        public string GetConcatenatedTimeString()
        {
            return ts0 + "_" + ts1 + "_" + ts2;
        }

        //ROASTT: some images have invalid planet day number in PDS metadata
        public int GetDayNumber()
        {
            return int.Parse(ts0);
        }

        public override RoverProductProducer Producer
        {
            get
            {
                if (this.producer == "J")
                {
                    return RoverProductProducer.OPGS;
                }
                return RoverProductProducer.Unknown;
            }
        }

        public override RoverProductSize Size
        {
            get
            {
                if (thumb.ToUpper().Equals("N"))
                {
                    return RoverProductSize.Regular;
                }
                else
                {
                    return RoverProductSize.Thumbnail;
                }
            }
        }
    }

    public class MSLOPGSProductId : OPGSProductId
    {
        protected string config = null,
                         spec = null,
                         sclk = null,
                         samp = null,
                         site = null,
                         drive = null,
                         seqnum = null,
                         venue = null;
                

        public override RoverProductProducer Producer
        {
            get
            {
                return RoverProductProducer.OPGS;
            }
        }       

        public override RoverProductSize Size
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

        public static MSLOPGSProductId ParseFromOPGSName(string productId)
        {
            if (productId.EndsWith(".IMG"))
            {
                productId = productId.Replace(".IMG", "");
            }
            if (productId.Length != 36)
            {
                return null;
            }
            MSLOPGSProductId id = new MSLOPGSProductId();
            id.fullIdString = productId;

            id.inst = productId.Substring(0, 2);
            id.config = productId.Substring(2, 1);
            id.spec = productId.Substring(3, 1);
            id.sclk = productId.Substring(4, 9);
            id.prodType = productId.Substring(13, 3);
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

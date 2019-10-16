using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public enum RoverProductCamera
    {
        //common
        Unknown,
        Hazcam,
        FrontHazcam, FrontHazcamLeft, FrontHazcamRight,
        RearHazcam, RearHazcamLeft, RearHazcamRight,
        Navcam, NavcamLeft, NavcamRight,
        Mastcam, MastcamLeft, MastcamRight,

        //MSL
        MAHLI,

        //M2020
        FrontHazcamB, FrontHazcamLeftB, FrontHazcamRightB,
        MastcamZ, MastcamZLeft, MastcamZRight,
        CacheCam,
        EDLPUCA, EDLPUCB, EDLPUCC, EDLRDC, EDLLVS, EDLDSD, EDLRUC,
        HeliNav, HeliScout,
        MEDASkyCam,
        PIXELMCC,
        SHERLOCACI,
        SHERLOCWATSON, SHERLOCWATSONLeft, SHERLOCWATSONRight,
        SuperCamRMI
    }

    /// <summary>
    /// Also See Mission.{IsHazcam,IsMastcam,IsNavcam}()
    /// </summary>
    public static class RoverCamera
    {
        private static Dictionary<string, RoverProductCamera> pdsCameraTypes =
            new Dictionary<string, RoverProductCamera>()
        {
            { "FHAZ_LEFT", RoverProductCamera.FrontHazcamLeft },
            { "FHAZ_RIGHT", RoverProductCamera.FrontHazcamRight },
            { "RHAZ_LEFT", RoverProductCamera.RearHazcamLeft },
            { "RHAZ_RIGHT", RoverProductCamera.RearHazcamRight },
            { "NAV_LEFT", RoverProductCamera.NavcamLeft }, //MSL
            { "NAV_RIGHT", RoverProductCamera.NavcamRight }, //MSL
            { "NAVCAM_LEFT", RoverProductCamera.NavcamLeft }, //M2020
            { "NAVCAM_RIGHT", RoverProductCamera.NavcamRight }, //M2020
            { "MAST_LEFT", RoverProductCamera.MastcamLeft }, //MSL
            { "MAST_RIGHT", RoverProductCamera.MastcamRight }, //MSL
            { "MCZ_LEFT", RoverProductCamera.MastcamZLeft }, //M2020
            { "MCZ_RIGHT", RoverProductCamera.MastcamZRight }, //M2020
            { "MAHLI", RoverProductCamera.MAHLI } //MSL
            //TODO additional M2020 types
        };

        private static Dictionary<string, RoverProductCamera> rdrCameraTypes =
            new Dictionary<string, RoverProductCamera>()
        {
            { "FL", RoverProductCamera.FrontHazcamLeft },
            { "FR", RoverProductCamera.FrontHazcamRight },
            { "RL", RoverProductCamera.RearHazcamLeft },
            { "RR", RoverProductCamera.RearHazcamRight },
            { "NL", RoverProductCamera.NavcamLeft },
            { "NR", RoverProductCamera.NavcamRight },
            { "ML", RoverProductCamera.MastcamLeft }, //MastcamZLeft for M2020, see MissionM2020.TranslateCamera()
            { "MR", RoverProductCamera.MastcamRight }, //MastcamZRight for M2020, see MissionM2020.TranslateCamera()
            { "MH", RoverProductCamera.MAHLI }, //MSL
            { "BL", RoverProductCamera.FrontHazcamLeftB }, //M2020
            { "BR", RoverProductCamera.FrontHazcamRightB }, //M2020
            { "CC", RoverProductCamera.CacheCam }, //M2020
            { "EA", RoverProductCamera.EDLPUCA }, //M2020
            { "EB", RoverProductCamera.EDLPUCB }, //M2020
            { "EC", RoverProductCamera.EDLPUCC }, //M2020
            { "ED", RoverProductCamera.EDLRDC }, //M2020
            { "EL", RoverProductCamera.EDLLVS }, //M2020
            { "ES", RoverProductCamera.EDLDSD }, //M2020
            { "EU", RoverProductCamera.EDLRUC }, //M2020
            { "HN", RoverProductCamera.HeliNav }, //M2020
            { "HS", RoverProductCamera.HeliScout }, //M2020
            { "MS", RoverProductCamera.MEDASkyCam }, //M2020
            { "PC", RoverProductCamera.PIXELMCC }, //M2020
            { "SC", RoverProductCamera.SHERLOCACI }, //M2020
            { "IL", RoverProductCamera.SHERLOCWATSONLeft }, //M2020
            { "IR", RoverProductCamera.SHERLOCWATSONRight }, //M2020
            { "SR", RoverProductCamera.SuperCamRMI } //M2020
        };

        public static RoverProductCamera FromPDSInstrumentID(string id)
        {
            if (pdsCameraTypes.ContainsKey(id))
            {
                return pdsCameraTypes[id];
            }
            return RoverProductCamera.Unknown;
        }

        public static RoverProductCamera FromRDRInstrumentID(string id)
        {
            if (rdrCameraTypes.ContainsKey(id))
            {
                return rdrCameraTypes[id];
            }
            return RoverProductCamera.Unknown;
        }

        public static bool IsCamera(RoverProductCamera camType, RoverProductCamera cam)
        {
            switch (camType)
            {
                case RoverProductCamera.Hazcam:
                    {
                        return cam == RoverProductCamera.Hazcam || 
                            cam == RoverProductCamera.FrontHazcam ||
                            cam == RoverProductCamera.FrontHazcamLeft || cam == RoverProductCamera.FrontHazcamRight ||
                            cam == RoverProductCamera.RearHazcam ||
                            cam == RoverProductCamera.RearHazcamLeft || cam == RoverProductCamera.RearHazcamRight ||
                            cam == RoverProductCamera.FrontHazcamB ||
                            cam == RoverProductCamera.FrontHazcamLeftB || cam == RoverProductCamera.FrontHazcamRightB;
                    }
                case RoverProductCamera.FrontHazcam:
                    {
                        return cam == RoverProductCamera.FrontHazcam || cam == RoverProductCamera.FrontHazcamB ||
                            cam == RoverProductCamera.FrontHazcamLeft || cam == RoverProductCamera.FrontHazcamRight ||
                            cam == RoverProductCamera.FrontHazcamLeftB || cam == RoverProductCamera.FrontHazcamRightB;
                    }
                case RoverProductCamera.RearHazcam:
                    {
                        return cam == RoverProductCamera.RearHazcam ||
                            cam == RoverProductCamera.RearHazcamLeft || cam == RoverProductCamera.RearHazcamRight;
                    }
                case RoverProductCamera.Mastcam:
                    {
                        return cam == RoverProductCamera.Mastcam ||
                            cam == RoverProductCamera.MastcamLeft || cam == RoverProductCamera.MastcamRight ||
                            cam == RoverProductCamera.MastcamZ ||
                            cam == RoverProductCamera.MastcamZLeft || cam == RoverProductCamera.MastcamZRight;
                    }
                case RoverProductCamera.Navcam:
                    {
                        return cam == RoverProductCamera.Navcam ||
                            cam == RoverProductCamera.NavcamLeft || cam == RoverProductCamera.NavcamRight;
                    }
                default: return camType == cam;
            }
        }

        public static bool IsCamera(string camType, string cam)
        {
            return IsCamera((RoverProductCamera)Enum.Parse(typeof(RoverProductCamera), camType, ignoreCase: true),
                            (RoverProductCamera)Enum.Parse(typeof(RoverProductCamera), cam, ignoreCase: true));
        }

        public static bool IsCamera(string camType, RoverProductCamera cam)
        {
            return IsCamera((RoverProductCamera)Enum.Parse(typeof(RoverProductCamera), camType, ignoreCase: true), cam);
        }

        public static bool IsCamera(RoverProductCamera camType, string cam)
        {
            return IsCamera(camType, (RoverProductCamera)Enum.Parse(typeof(RoverProductCamera), cam, ignoreCase: true));
        }
    }

    public enum RoverProductGeometry
    {
        Unknown,
        Raw,
        Linearized
    }

    public enum RoverProductSize
    {
        Unknown,
        Regular,
        Thumbnail
    }

    public enum RoverProductType
    {
        Unknown,
        Image,
        RoverMask,
        Range,
        XYZ,
        NormalMap,
        RangeErrorMap,
        XYZErrorMap
    }

    public static class RoverProduct
    {
        private static Dictionary<string, RoverProductType> pdsDerivedImageTypes =
            new Dictionary<string, RoverProductType>()
        {
            { "IMAGE", RoverProductType.Image },
            { "MASK", RoverProductType.RoverMask },
            { "RANGE_MAP", RoverProductType.Range },
            { "XYZ_MAP", RoverProductType.XYZ },
            { "UVW_MAP", RoverProductType.NormalMap },
            { "RANGE_ERROR_MAP", RoverProductType.RangeErrorMap },
            { "XYZ_ERROR_MAP", RoverProductType.XYZErrorMap },
        };

        private static Dictionary<string, RoverProductType> rdrProductTypes =
            new Dictionary<string, RoverProductType>()
        {
            { "RAS", RoverProductType.Image },
            { "MXY", RoverProductType.RoverMask },
            { "RNG", RoverProductType.Range },
            { "XYZ", RoverProductType.XYZ },
            { "UVW", RoverProductType.NormalMap },
            { "RNE", RoverProductType.RangeErrorMap },
            { "XYE", RoverProductType.XYZErrorMap },
        };

        public static RoverProductType FromPDSDerivedImageType(string pdsType)
        {
            if (pdsDerivedImageTypes.ContainsKey(pdsType))
            {
                return pdsDerivedImageTypes[pdsType];
            }
            return RoverProductType.Unknown;
        }

        public static RoverProductType FromRDRProductType(string rdrType)
        {
            if (rdrProductTypes.ContainsKey(rdrType))
            {
                return rdrProductTypes[rdrType];
            }
            return RoverProductType.Unknown;
        }

        public static bool IsMask(RoverProductType prodType)
        {
            return prodType == RoverProductType.RoverMask;
        }

        public static bool IsErrorMap(RoverProductType prodType)
        {
            return prodType == RoverProductType.RangeErrorMap || prodType == RoverProductType.XYZErrorMap;
        }

        public static bool IsRaster(RoverProductType prodType)
        {
            return prodType == RoverProductType.Image || prodType == RoverProductType.RoverMask;
        }

        public static bool IsGeometry(RoverProductType prodType)
        {
            return prodType == RoverProductType.RoverMask ||
                prodType == RoverProductType.Range || prodType == RoverProductType.XYZ ||
                prodType == RoverProductType.NormalMap ||
                prodType == RoverProductType.RangeErrorMap || prodType == RoverProductType.XYZErrorMap;
        }
    }

    public enum RoverProductProducer
    {
        Unknown,
        OPGS,
        MSSS
    }

    public enum RoverStereoEye
    {
        Left,
        Right,
        Mono,
        Any
    }

    public static class RoverStereoPair
    {
        public static readonly RoverProductCamera[] LeftCams = new RoverProductCamera[]
            {
                RoverProductCamera.FrontHazcamLeft,
                RoverProductCamera.RearHazcamLeft,
                RoverProductCamera.NavcamLeft,
                RoverProductCamera.MastcamLeft,
                RoverProductCamera.FrontHazcamLeftB,
                RoverProductCamera.MastcamZLeft,
                RoverProductCamera.SHERLOCWATSONLeft
            };

        public static readonly RoverProductCamera[] RightCams = new RoverProductCamera[]
            {
                RoverProductCamera.FrontHazcamRight,
                RoverProductCamera.RearHazcamRight,
                RoverProductCamera.NavcamRight,
                RoverProductCamera.MastcamRight,
                RoverProductCamera.FrontHazcamRightB,
                RoverProductCamera.MastcamZRight,
                RoverProductCamera.SHERLOCWATSONRight
            };

        public static readonly RoverProductCamera[] StereoCams = new RoverProductCamera[]
            {
                RoverProductCamera.FrontHazcam,
                RoverProductCamera.RearHazcam,
                RoverProductCamera.Navcam,
                RoverProductCamera.Mastcam,
                RoverProductCamera.FrontHazcamB,
                RoverProductCamera.MastcamZ,
                RoverProductCamera.SHERLOCWATSON
            };

        public static bool IsStereo(RoverProductCamera cam)
        {
            return LeftCams.Contains(cam) || RightCams.Contains(cam) || StereoCams.Contains(cam);
        }

        public static bool IsStereoLeft(RoverProductCamera cam)
        {
            return LeftCams.Contains(cam);
        }

        public static bool IsStereoRight(RoverProductCamera cam)
        {
            return RightCams.Contains(cam);
        }

        public static bool IsStereoEye(RoverProductCamera cam, RoverStereoEye eye)
        {
            switch (eye)
            {
                case RoverStereoEye.Left: return IsStereoLeft(cam);
                case RoverStereoEye.Right: return IsStereoRight(cam);
                case RoverStereoEye.Mono: return !IsStereo(cam);
                default: return true;
            } 
        }

        public static RoverStereoEye OtherEye(RoverStereoEye eye)
        {
            switch (eye)
            {
                case RoverStereoEye.Left: return RoverStereoEye.Right;
                case RoverStereoEye.Right: return RoverStereoEye.Left;
                case RoverStereoEye.Mono: return RoverStereoEye.Mono;
                default: return RoverStereoEye.Any;
            } 
        }

        public static RoverProductCamera GetOtherEye(RoverProductCamera cam)
        {
            int index = Array.IndexOf(LeftCams, cam);
            if (index >= 0)
            {
                return RightCams[index];
            }

            index = Array.IndexOf(RightCams, cam);
            if (index >= 0)
            {
                return LeftCams[index];
            }

            throw new ArgumentException("not a stereo camera: " + cam);
        }

        public static RoverProductCamera GetStereoCamera(RoverProductCamera cam)
        {
            int index = Array.IndexOf(LeftCams, cam);
            if (index >= 0)
            {
                return StereoCams[index];
            }

            index = Array.IndexOf(RightCams, cam);
            if (index >= 0)
            {
                return StereoCams[index];
            }

            if (StereoCams.Contains(cam))
            {
                return cam;
            }

            throw new ArgumentException("not a stereo camera: " + cam);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        SHERLOCACI, SHERLOCWATSON, SHERLOCWATSONLeft, SHERLOCWATSONRight
    }

    /// <summary>
    /// Also See Mission.{IsHazcam,IsMastcam,IsNavcam,IsArmcam}()
    /// </summary>
    public static class RoverCamera
    {
        private static Dictionary<string, RoverProductCamera> pdsCameraTypes =
            new Dictionary<string, RoverProductCamera>()
        {
            { "FRONT_HAZCAM_LEFT_A", RoverProductCamera.FrontHazcamLeft },     //M2020
            { "FRONT_HAZCAM_RIGHT_A", RoverProductCamera.FrontHazcamRight },   //M2020
            { "FRONT_HAZCAM_LEFT_B", RoverProductCamera.FrontHazcamLeft },     //M2020
            { "FRONT_HAZCAM_RIGHT_B", RoverProductCamera.FrontHazcamRight },   //M2020
            { "REAR_HAZCAM_LEFT", RoverProductCamera.RearHazcamLeft },         //M2020
            { "REAR_HAZCAM_RIGHT", RoverProductCamera.RearHazcamRight },       //M2020
            { "FHAZ_LEFT_A", RoverProductCamera.FrontHazcamLeft }, //MSL and early M2020 datasets
            { "FHAZ_LEFT_B ", RoverProductCamera.FrontHazcamLeft }, //MSL and early M2020 datasets
            { "FHAZ_RIGHT_A", RoverProductCamera.FrontHazcamRight }, //MSL and early M2020 datasets
            { "FHAZ_RIGHT_B", RoverProductCamera.FrontHazcamRight }, //MSL and early M2020 datasets
            { "RHAZ_LEFT_A", RoverProductCamera.RearHazcamLeft }, //MSL and early M2020 datasets
            { "RHAZ_LEFT_B", RoverProductCamera.RearHazcamLeft }, //MSL and early M2020 datasets
            { "RHAZ_RIGHT_A", RoverProductCamera.RearHazcamRight }, //MSL and early M2020 datasets
            { "RHAZ_RIGHT_B", RoverProductCamera.RearHazcamRight }, //MSL and early M2020 datasets
            { "NAV_LEFT_A", RoverProductCamera.NavcamLeft }, //MSL
            { "NAV_LEFT_B", RoverProductCamera.NavcamLeft }, //MSL
            { "NAV_RIGHT_A", RoverProductCamera.NavcamRight }, //MSL
            { "NAV_RIGHT_B", RoverProductCamera.NavcamRight }, //MSL
            { "NAVCAM_LEFT", RoverProductCamera.NavcamLeft }, //M2020
            { "NAVCAM_RIGHT", RoverProductCamera.NavcamRight }, //M2020
            { "MAST_LEFT", RoverProductCamera.MastcamLeft }, //MSL
            { "MAST_RIGHT", RoverProductCamera.MastcamRight }, //MSL
            { "MCZ_LEFT", RoverProductCamera.MastcamZLeft }, //M2020
            { "MCZ_RIGHT", RoverProductCamera.MastcamZRight }, //M2020
            { "MAHLI", RoverProductCamera.MAHLI } //MSL
            //TODO M2020 types for SHERLOC-WATSON
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/897
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
            { "ZL", RoverProductCamera.MastcamZLeft }, //M2020
            { "ZR", RoverProductCamera.MastcamZRight }, //M2020
            { "MH", RoverProductCamera.MAHLI }, //MSL
            { "BL", RoverProductCamera.FrontHazcamLeftB }, //M2020
            { "BR", RoverProductCamera.FrontHazcamRightB }, //M2020
            { "SC", RoverProductCamera.SHERLOCACI }, //M2020
            { "SI", RoverProductCamera.SHERLOCWATSON }, //M2020
            { "SL", RoverProductCamera.SHERLOCWATSONLeft }, //M2020
            { "SR", RoverProductCamera.SHERLOCWATSONRight }, //M2020
        };

        private static ConcurrentDictionary<RoverProductCamera, string> invRDRCameraTypes =
            new ConcurrentDictionary<RoverProductCamera, string>();

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

        public static string ToRDRInstrumentID(RoverProductCamera cam)
        {
            return invRDRCameraTypes
                .GetOrAdd(cam, _ => rdrCameraTypes.Where(e => e.Value == cam).Select(e => e.Key).First());
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

        public static RoverProductCamera[] ParseList(string cams)
        {
            return (cams ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => (RoverProductCamera)Enum.Parse(typeof(RoverProductCamera), s, ignoreCase: true))
                .Cast<RoverProductCamera>()
                .ToArray();
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
        Points,
        Normals,
        RangeError
    }

    public enum RoverProductColor
    {
        Unknown,
        FullColor,
        Grayscale,
        Red,
        Green,
        Blue
    }

    public static class RoverProduct
    {
        private static Dictionary<string, RoverProductType> pdsDerivedImageTypes =
            new Dictionary<string, RoverProductType>()
        {
            { "IMAGE", RoverProductType.Image },
            { "MASK", RoverProductType.RoverMask },
            { "RANGE_MAP", RoverProductType.Range },
            { "XYZ_MAP", RoverProductType.Points },
            { "UVW_MAP", RoverProductType.Normals },
            { "RANGE_ERROR_MAP", RoverProductType.RangeError },
        };

        private static Dictionary<string, RoverProductType> rdrProductTypes =
            new Dictionary<string, RoverProductType>()
        {
            { "RAS", RoverProductType.Image },
            { "MXY", RoverProductType.RoverMask },
            { "RNG", RoverProductType.Range },
            { "XYZ", RoverProductType.Points },
            { "UVW", RoverProductType.Normals },
            { "RNE", RoverProductType.RangeError },
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
            return prodType == RoverProductType.RangeError;
        }

        public static bool IsRaster(RoverProductType prodType)
        {
            return prodType == RoverProductType.Image || prodType == RoverProductType.RoverMask;
        }

        public static bool IsGeometry(RoverProductType prodType)
        {
            return prodType == RoverProductType.RoverMask || prodType == RoverProductType.RangeError ||
                prodType == RoverProductType.Range || prodType == RoverProductType.Points ||
                prodType == RoverProductType.Normals;
        }

        public static bool IsMonochrome(RoverProductColor color)
        {
            return color == RoverProductColor.Grayscale ||
                color == RoverProductColor.Red ||
                color == RoverProductColor.Green ||
                color == RoverProductColor.Blue;
        }

        //https://github.jpl.nasa.gov/OnSight/Landform/issues/783#issuecomment-234441
        public static int BandPreference(RoverProductColor color)
        {
            switch (color)
            {
                case RoverProductColor.FullColor: return 0;
                case RoverProductColor.Grayscale: return 1;
                case RoverProductColor.Green: return 2;
                case RoverProductColor.Red: return 3;
                case RoverProductColor.Blue: return 4;
                default: return 5;
            }
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

            return cam;
        }

        public static RoverStereoEye ParseEyeForGeometry(string eye, MissionSpecific mission)
        {
            if (mission != null && eye.ToLower() == "auto")
            {
                return mission.PreferEyeForGeometry();
            }
            else if (Enum.TryParse<RoverStereoEye>(eye, true, out RoverStereoEye ret))
            {
                return ret;
            }
            else
            {
                throw new ArgumentException("unknown stereo eye: " + eye);
            }
        }
    }
}

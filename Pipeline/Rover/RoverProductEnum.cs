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
        Range,
        RoverMask,
        ReachabilityMap,
        XYZ,
        RangeErrorMap,
        NormalMap,
        XYZErrorMap
    }

    public enum RoverProductProducer
    {
        Unknown,
        OPGS,
        MSSS
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

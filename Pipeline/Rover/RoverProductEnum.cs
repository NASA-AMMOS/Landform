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
        FrontHazcamLeft, FrontHazcamRight,
        RearHazcamLeft, RearHazcamRight,
        NavcamLeft, NavcamRight,
        MastcamLeft, MastcamRight,

        //MSL
        MAHLI,

        //M2020
        FrontHazcamLeftB, FrontHazcamRightB,
        MastcamZLeft, MastcamZRight,
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
}

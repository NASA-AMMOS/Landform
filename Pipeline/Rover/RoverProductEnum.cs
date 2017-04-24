using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public enum RoverProductCamera
    {
        Unknown,
        FrontHazcamLeft,
        FrontHazcamRight,
        RearHazcamLeft,
        RearHazcamRight,
        NavcamLeft,
        NavcamRight,
        MastcamLeft,
        MastcamRight,
        MAHLI
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment.BundleAdjusterStructures
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct ProblemDefinition
    {
        public UInt64 magic;

        public UInt32 numTransforms;
        public UInt32 numCameraModels;
        public UInt32 numPoints;
        public UInt32 numProjections;
        public UInt32 numPriors;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct RigidTransform
    {
        public double tx, ty, tz;
        public double rx, ry, rz;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct TransformPrior
    {
        public UInt32 transformId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public double[] translationMean;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public double[] translationCovariance;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public double[] rotationMean;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public double[] rotationCovariance;
    }

    public enum CameraModelType : byte
    {
        CAHV = 1,
        CAHVOR = 2,
        CAHVORE = 3,
        PHOTOMETRIC = 4
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct CameraModel
    {
        public CameraModelType type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3 * 7 + 1)]
        public double[] parameters;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Point
    {
        public double x, y, z, w;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Projection
    {
        public UInt32 cameraModelIdx;
        public UInt32 transformIdx;
        public UInt32 pointIdx;
        public double x, y, z, d;
    }
}

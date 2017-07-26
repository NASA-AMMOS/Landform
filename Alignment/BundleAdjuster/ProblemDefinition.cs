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
        public static readonly UInt64 MAGIC_NUMBER = 0x42756E646C653121;

        public UInt64 Magic;

        public UInt32 NumTransforms;
        public UInt32 NumCameraModels;
        public UInt32 NumPoints;
        public UInt32 NumProjections;
        public UInt32 NumPriors;
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
        public CameraModelType Type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3 * 7 + 1)]
        public double[] Parameters;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Point
    {
        public double X, Y, Z, W;

        public Point(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Projection
    {
        public UInt32 CameraModelIdx;
        public UInt32 TransformIdx;
        public UInt32 PointIdx;
        public double X, Y, D;

        public Projection(int cameraModelIdx, int transformIdx, int pointIdx, double x, double y, double d)
        {
            CameraModelIdx = (uint)cameraModelIdx;
            TransformIdx = (uint)transformIdx;
            PointIdx = (uint)pointIdx;
            X = x;
            Y = y;
            D = d;
        }
    }
}

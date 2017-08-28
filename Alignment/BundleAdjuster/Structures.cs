using Microsoft.Xna.Framework;
using OPS.Imaging;
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

        public RigidTransform(Matrix mat)
        {
            Vector3 t, s;
            Quaternion r;
            mat.Decompose(out s, out r, out t);
            tx = t.X;
            ty = t.Y;
            tz = t.Z;
            Vector3 rVec = r.ToRodriguesVector();
            rx = rVec.X;
            ry = rVec.Y;
            rz = rVec.Z;
        }

        public Vector3 Translation
        {
            get { return new Vector3(tx, ty, tz); }
        }
        public Vector3 RodriguesVector
        {
            get { return new Vector3(rx, ry, rz); }
        }
        public Quaternion Rotation
        {
            get
            {
                Vector3 rv = RodriguesVector;
                double theta = rv.Length();
                return Quaternion.CreateFromAxisAngle(rv / theta, theta);
            }
        }
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

        public TransformPrior(uint transformId, Matrix translationMat, Matrix rotationMat)
        {
            this.transformId = transformId;
            translationMean = (-translationMat.Translation).ToDoubleArray();
            translationCovariance = new double[]
            {
                translationMat[0, 0], translationMat[0, 1], translationMat[0, 2],
                translationMat[1, 0], translationMat[1, 1], translationMat[1, 2],
                translationMat[2, 0], translationMat[2, 1], translationMat[2, 2]
            };
            rotationMean = (-rotationMat.Translation).ToDoubleArray();
            rotationCovariance = new double[]
            {
                rotationMat[0, 0], rotationMat[0, 1], rotationMat[0, 2],
                rotationMat[1, 0], rotationMat[1, 1], rotationMat[1, 2],
                rotationMat[2, 0], rotationMat[2, 1], rotationMat[2, 2]
            };
        }
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

        public CameraModel(CAHV c)
        {
            Vector3 C = Vector3.Zero,
                    A = Vector3.Zero,
                    H = Vector3.Zero,
                    V = Vector3.Zero,
                    O = Vector3.Zero,
                    R = Vector3.Zero,
                    E = Vector3.Zero;
            double linearity = 1;
            Type = CameraModelType.CAHV;

            if (c is CAHV)
            {
                C = ((CAHVOR)c).C;
                A = ((CAHVOR)c).A;
                H = ((CAHVOR)c).H;
                V = ((CAHVOR)c).V;
            }
            if (c is CAHVOR)
            {
                O = ((CAHVOR)c).O;
                R = ((CAHVOR)c).R;
                Type = CameraModelType.CAHVOR;
            }
            if (c is CAHVORE)
            {
                E = ((CAHVORE)c).E;
                linearity = ((CAHVORE)c).linearityMode.Linearity;
                Type = CameraModelType.CAHVORE;
            }
            
            Parameters = new double[]
            {
                C.X, C.Y, C.Z,
                A.X, A.Y, A.Z,
                H.X, H.Y, H.Z,
                V.X, V.Y, V.Z,
                O.X, O.Y, O.Z,
                R.X, R.Y, R.Z,
                E.X, E.Y, E.Z,
                linearity
            };
        }

        public CAHV Model
        {
            get
            {
                if (Type == CameraModelType.PHOTOMETRIC)
                {
                    throw new NotImplementedException("photometric camera model");
                }

                Vector3 C = new Vector3(Parameters[0], Parameters[1], Parameters[2]);
                Vector3 A = new Vector3(Parameters[3], Parameters[4], Parameters[5]);
                Vector3 H = new Vector3(Parameters[6], Parameters[7], Parameters[8]);
                Vector3 V = new Vector3(Parameters[9], Parameters[10], Parameters[11]);
                if (Type == CameraModelType.CAHV)
                {
                    return new CAHV(C, A, H, V);
                }

                Vector3 O = new Vector3(Parameters[12], Parameters[13], Parameters[14]);
                Vector3 R = new Vector3(Parameters[15], Parameters[16], Parameters[17]);
                if (Type == CameraModelType.CAHVOR)
                {
                    return new CAHVOR(C, A, H, V, O, R);
                }

                Vector3 E = new Vector3(Parameters[18], Parameters[19], Parameters[20]);
                double linearity = Parameters[21];
                return new CAHVORE(C, A, H, V, O, R, E, new LinearityMode(linearity));
            }
        }
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

        public Vector3 Position
        {
            get
            {
                return new Vector3(X / W, Y / W, Z / W);
            }
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

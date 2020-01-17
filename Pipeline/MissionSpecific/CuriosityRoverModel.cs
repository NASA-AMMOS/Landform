using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Geometry;

namespace OPS.Pipeline
{
    /// <summary>
    /// Geometric rover model. Instances of this class are safe to share between threads.
    /// </summary>
    public class CuriosityRoverModel : RoverModel
    {
        private readonly string ROVER_BODY_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/Body.obj";
        private readonly string LEFT_ROCKER_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/LeftRocker.obj";
        private readonly string RIGHT_ROCKER_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/RightRocker.obj";
        private readonly string LEFT_BOGIE_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/LeftBogie.obj";
        private readonly string RIGHT_BOGIE_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/RightBogie.obj";
        private readonly string ARM1_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/Arm01.obj";
        private readonly string ARM2_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/Arm02.obj";
        private readonly string ARM3_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/Arm03.obj";
        private readonly string ARM4_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/Arm04.obj";
        private readonly string ARM5_FILE = "MissionSpecific/Resources/CuriosityMaskGeometry/Arm05.obj";

        private readonly Mesh Body;
        private readonly Mesh LeftRocker;
        private readonly Mesh RightRocker;
        private readonly Mesh LeftBogie;
        private readonly Mesh RightBogie;
        private readonly Mesh Arm1;
        private readonly Mesh Arm2;
        private readonly Mesh Arm3;
        private readonly Mesh Arm4;
        private readonly Mesh Arm5;

        public CuriosityRoverModel()
        {
            var ap = PathHelper.GetApplicationPath();
            Body = OBJSerializer.Read(Path.Combine(ap, ROVER_BODY_FILE));
            LeftRocker = OBJSerializer.Read(Path.Combine(ap, LEFT_ROCKER_FILE));
            RightRocker = OBJSerializer.Read(Path.Combine(ap, RIGHT_ROCKER_FILE));
            LeftBogie = OBJSerializer.Read(Path.Combine(ap, LEFT_BOGIE_FILE));
            RightBogie = OBJSerializer.Read(Path.Combine(ap, RIGHT_BOGIE_FILE));
            Arm1 = OBJSerializer.Read(Path.Combine(ap, ARM1_FILE));
            Arm2 = OBJSerializer.Read(Path.Combine(ap, ARM2_FILE));
            Arm3 = OBJSerializer.Read(Path.Combine(ap, ARM3_FILE));
            Arm4 = OBJSerializer.Read(Path.Combine(ap, ARM4_FILE));
            Arm5 = OBJSerializer.Read(Path.Combine(ap, ARM5_FILE));
        }

        public Mesh BuildMesh(RoverArticulation pose, bool includeBody = true)
        {
            return BuildMesh(pose as MSLRoverArticulation, includeBody);
        }

        public Mesh BuildMesh(MSLRoverArticulation pose, bool includeBody = true)
        {
            Mesh leftRocker = Mesh.Transformed(LeftRocker, Matrix.Invert(FrameToLeftRocker(0)) * FrameToLeftRocker(pose.LeftRockerAngle));
            Mesh rightRocker = Mesh.Transformed(RightRocker, Matrix.Invert(FrameToRightRocker(0)) * FrameToRightRocker(pose.RightRockerAngle));

            Mesh leftBogie = Mesh.Transformed(LeftBogie, Matrix.Invert(FrameToLeftBogie(0, 0)) * FrameToLeftBogie(pose.LeftRockerAngle, pose.LeftBogieAngle));
            Mesh rightBogie = Mesh.Transformed(RightBogie, Matrix.Invert(FrameToRightBogie(0, 0)) * FrameToRightBogie(pose.RightRockerAngle, pose.RightBogieAngle));

            Mesh arm = BuildArm(pose);

            Matrix mechToNavFrame = Matrix.CreateTranslation(mechFrameToNavFrame);
            Mesh res;
            if (includeBody)
            {
                res = Mesh.Merge(Body, leftRocker, leftBogie, rightRocker, rightBogie, arm);
            }
            else
            {
                res = Mesh.Merge(leftRocker, leftBogie, rightRocker, rightBogie, arm);
            }
            res.Transform(mechToNavFrame);
            return res;
        }

        private Mesh BuildArm(MSLRoverArticulation pose)
        {
            Mesh arm1 = Mesh.Transformed(Arm1, Matrix.Invert(RVRdARM2(0)) * RVRdARM2(pose.ArmAngle1));
            Mesh arm2 = Mesh.Transformed(Arm2, Matrix.Invert(RVRdARM3(0, 0)) * RVRdARM3(pose.ArmAngle1, pose.ArmAngle2));
            Mesh arm3 = Mesh.Transformed(Arm3, Matrix.Invert(RVRdARM4(0, 0, 0)) * RVRdARM4(pose.ArmAngle1, pose.ArmAngle2, pose.ArmAngle3));
            Mesh arm4 = Mesh.Transformed(Arm4, Matrix.Invert(RVRdARM5(0, 0, 0, 0)) * RVRdARM5(pose.ArmAngle1, pose.ArmAngle2, pose.ArmAngle3, pose.ArmAngle4));
            Mesh arm5 = Mesh.Transformed(Arm5, Matrix.Invert(RVRdARM6(0, 0, 0, 0, 0)) * RVRdARM6(pose.ArmAngle1, pose.ArmAngle2, pose.ArmAngle3, pose.ArmAngle4, pose.ArmAngle5));

            return Mesh.Merge(arm1, arm2, arm3, arm4, arm5);
        }

        #region Composite tranformation matrices

        private Matrix FrameToLeftRocker(double angle)
        {
            return RMcLR(angle) * Matrix.CreateTranslation(RMtLR);
        }

        private Matrix FrameToRightRocker(double angle)
        {
            return RMcRR(angle) * Matrix.CreateTranslation(RMtRR);
        }

        private Matrix FrameToLeftBogie(double rockerAngle, double bogieAngle)
        {
            return LRcLB(bogieAngle) * Matrix.CreateTranslation(LRtLB) * FrameToLeftRocker(rockerAngle);
        }

        private Matrix FrameToRightBogie(double rockerAngle, double bogieAngle)
        {
            return RRcRB(bogieAngle) * Matrix.CreateTranslation(RRtRB) * FrameToRightRocker(rockerAngle);
        }

        private Matrix RVRdARM1()
        {
            return ARM0dARM1() * RVRdARM0();
        }

        private Matrix RVRdARM2(double a1)
        {
            return ARM1dARM2(a1) * RVRdARM1();
        }

        private Matrix RVRdARM3(double a1, double a2)
        {
            return ARM2dARM3(a2) * RVRdARM2(a1);
        }

        private Matrix RVRdARM4(double a1, double a2, double a3)
        {
            return ARM3dARM4(a3) * RVRdARM3(a1, a2);
        }

        private Matrix RVRdARM5(double a1, double a2, double a3, double a4)
        {
            return ARM4dARM5(a4) * RVRdARM4(a1, a2, a3);
        }

        private Matrix RVRdARM6(double a1, double a2, double a3, double a4, double a5)
        {
            return ARM5dARM6(a5) * RVRdARM5(a1, a2, a3, a4);
        }

        #endregion Composite transformation matrices

        #region Rover Offset Vectors

        // Offset vectors taken from document "Mars Science Laboratory Pointing,
        //Positioning, Phasing, and Coordinate Systems (PPPCS) Document, Volume 9,  
        // Surface Remote Sensing and Navigation Phasing", JPL D-34651, MSL-476-1306,
        // Table 17.

        /// <summary>
        /// Rover mech frame to left rocker.
        /// </summary>
        private readonly Vector3 RMtLR = new Vector3(0.214, -0.79959, 0.24050);

        /// <summary>
        /// Left rocker frame to left bogie.
        /// </summary>
        private readonly Vector3 LRtLB = new Vector3(-0.754, -0.230, -0.120);

        /// <summary>
        /// Left rocker to left front wheel.
        /// </summary>
        private readonly Vector3 LRtLFW = new Vector3(0.881, -0.6300, -0.262910);

        /// <summary>
        /// Left bogie to left middle wheel.
        /// </summary>
        private readonly Vector3 LBtLMW = new Vector3(0.44998, -0.40000, -0.26491);

        /// <summary>
        /// Left bogie to left rear wheel.
        /// </summary>
        private readonly Vector3 LBtLRW = new Vector3(-0.62500, -0.40000, -0.14291);

        /// <summary>
        /// Rover mech frame to right rocker.
        /// </summary>
        private readonly Vector3 RMtRR = new Vector3(0.214, 0.79959, 0.24050);

        /// <summary>
        /// Right rocker to right bogie.
        /// </summary>
        private readonly Vector3 RRtRB = new Vector3(-0.754, -0.230, 0.120);

        /// <summary>
        /// Right rocker to right front wheel.
        /// </summary>
        private readonly Vector3 RRtRFW = new Vector3(0.881, -0.6300, 0.262910);

        /// <summary>
        /// Right bogie to right middle wheel.
        /// </summary>
        private readonly Vector3 RBtRMW = new Vector3(0.44998, -0.40000, 0.26491);

        /// <summary>
        /// Right bogie to right rear wheel.
        /// </summary>
        private readonly Vector3 RBtRRW = new Vector3(-0.62500, -0.40000, 0.14291);

        /// <summary>
        /// Offset from rover mechanical frame to rover navigation frame. From "Mars Science
        /// Laboratory: Pointing, Positioning, Phasing & Coordinate Systems (PPPCS) Document,
        /// Volume 1" ((D-34642), section 2.10.5.1.
        /// </summary>
        private readonly Vector3 mechFrameToNavFrame = new Vector3(0.09002, 0, -1.1205);

        #endregion Rover Offset Vectors

        #region Rover Rotation Matrices

        // Rotation matrices taken from document "Mars Science Laboratory Pointing,
        //Positioning, Phasing, and Coordinate Systems (PPPCS) Document, Volume 9,  
        // Surface Remote Sensing and Navigation Phasing", JPL D-34651, MSL-476-1306,
        // Table 16.

        /// <summary>
        /// Rover mech frame to left rocker frame.
        /// </summary>
        private Matrix RMcLR(double angle = 0)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(angle), -Math.Sin(angle), 0, 0,
                0, 0, 1, 0,
                -Math.Sin(angle), -Math.Cos(angle), 0, 0,
                0, 0, 0, 1));
        }

        /// <summary>
        /// Left rocker frame to left bogie frame.
        /// </summary>
        private Matrix LRcLB(double angle = 0)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(angle), -Math.Sin(angle), 0, 0,
                Math.Sin(angle), Math.Cos(angle), 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1));
        }

        /// <summary>
        /// Rover mech frame to right rocker frame.
        /// </summary>
        private Matrix RMcRR(double angle = 0)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(angle), -Math.Sin(angle), 0, 0,
                0, 0, 1, 0,
                -Math.Sin(angle), -Math.Cos(angle), 0, 0,
                0, 0, 0, 1));
        }

        /// <summary>
        /// Right rocker frame to right bogie frame.
        /// </summary>
        private Matrix RRcRB(double angle = 0)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(angle), -Math.Sin(angle), 0, 0,
                Math.Sin(angle), Math.Cos(angle), 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1));
        }


        // Rover arm stuff
        private Matrix RVRdARM0()
        {
            return Matrix.Transpose(new Matrix(
                1, 0, 0, 0.82785f,
                0, 1, 0, -0.561f,
                0, 0, 1, 0.225f,
                0, 0, 0, 1));
        }

        private Matrix ARM0dARM1()
        {
            return Matrix.Transpose(new Matrix(
                1, 0, 0, 0.285,
                0, 1, 0, 0.1095,
                0, 0, 1, 0.041,
                0, 0, 0, 1));
        }

        private Matrix ARM1dARM2(double a1)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(a1), 0, Math.Sin(a1), 0.156 * Math.Cos(a1),
                Math.Sin(a1), 0, -Math.Cos(a1), 0.156 * Math.Sin(a1),
                0, 1, 0, 0,
                0, 0, 0, 1));
        }

        private Matrix ARM2dARM3(double a2)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(a2), -Math.Sin(a2), 0, 0.827 * Math.Cos(a2),
                Math.Sin(a2), Math.Cos(a2), 0, 0.827 * Math.Sin(a2),
                0, 0, 1, 0,
                0, 0, 0, 1));
        }

        private Matrix ARM3dARM4(double a3)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(a3), -Math.Sin(a3), 0, 0.785 * Math.Cos(a3),
                Math.Sin(a3), Math.Cos(a3), 0, 0.785 * Math.Sin(a3),
                0, 0, 1, 0,
                0, 0, 0, 1));
        }

        private Matrix ARM4dARM5(double a4)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(a4), 0, -Math.Sin(a4), -0.144 * Math.Cos(a4),
                Math.Sin(a4), 0, Math.Cos(a4), -0.144 * Math.Sin(a4),
                0, -1, 0, -0.1664,
                0, 0, 0, 1));
        }

        private Matrix ARM5dARM6(double a5)
        {
            return Matrix.Transpose(new Matrix(
                Math.Cos(a5), -Math.Sin(a5), 0, 0,
                Math.Sin(a5), Math.Cos(a5), 0, 0,
                0, 0, 1, -0.1697,
                0, 0, 0, 1));
        }

        #endregion Rover Rotation Matrices
    }
}

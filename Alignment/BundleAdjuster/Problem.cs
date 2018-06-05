using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OPS.Alignment.BundleAdjusterStructures;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class BundleAdjusterProblem
    {
        public List<RigidTransform> Transforms;
        public List<BundleAdjusterStructures.CameraModel> CameraModels;
        public List<Point> Points;
        public List<Projection> Projections;
        public List<TransformPrior> TransformPriors;

        public BundleAdjusterProblem()
        {
            Transforms = new List<RigidTransform>();
            CameraModels = new List<BundleAdjusterStructures.CameraModel>();
            Points = new List<Point>();
            Projections = new List<Projection>();
            TransformPriors = new List<TransformPrior>();
        }
        
        public int AddTransform(Matrix m, bool _fixed=false)
        {
            int res = Transforms.Count;
            Transforms.Add(new RigidTransform(m, _fixed));
            return res;
        }

        public double EvaluateError(Projection p)
        {
            var cmod = CameraModels[(int)p.CameraModelIdx].Model;
            var worldPt = Points[(int)p.PointIdx].Position;

            var cameraPt = worldPt;
            for (int i = 7; i >= 0; i--)
            {
                if (p.TransformIndices[i] == 0xFFFFFFFFU)
                {
                    continue;
                }
                cameraPt = Vector3.Transform(cameraPt, Matrix.Invert(Transforms[(int)p.TransformIndices[i]].Matrix));
            }
            
            var projected = cmod.Project(cameraPt, out double range);
            return (projected - new Vector2(p.X, p.Y)).LengthSquared();
        }

        public int AddCameraModel(Imaging.CameraModel c)
        {
            int res = CameraModels.Count;
            CameraModels.Add(new BundleAdjusterStructures.CameraModel(c));
            return res;
        }

        public int AddPrior(int transformIdx, UncertainRigidTransform prior)
        {
            int res = TransformPriors.Count;
            TransformPriors.Add(new TransformPrior((uint)transformIdx, prior));
            return res;
        }

        public int AddPoint(Vector4 homogenousPos)
        {
            int res = Points.Count;
            Points.Add(new Point(homogenousPos.X, homogenousPos.Y, homogenousPos.Z, homogenousPos.W));
            return res;
        }
        public int AddPoint(Vector3 worldPos)
        {
            return AddPoint(new Vector4(worldPos, 1));
        }

        public int AddProjection(int cameraModelIdx, int[] transformIndices, int pointIdx, Vector2 pos, double d = double.NaN)
        {
            int res = Projections.Count;
            Projections.Add(new Projection(cameraModelIdx, pointIdx, transformIndices, pos.X, pos.Y, d));
            return res;
        }

        static T ReadStruct<T>(BinaryReader br)
        {
            int len = Marshal.SizeOf(typeof(T));
            byte[] data = br.ReadBytes(len);
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            T res = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            handle.Free();
            return res;
        }

        static void WriteStruct<T>(BinaryWriter bw, T obj)
        {
            int len = Marshal.SizeOf(obj);
            byte[] data = new byte[len];

            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr<T>(obj, ptr, false);
            Marshal.Copy(ptr, data, 0, len);

            bw.Write(data);
        }
        
        public void Write(BinaryWriter bw)
        {
            ProblemDefinition def = new ProblemDefinition();
            def.Magic = ProblemDefinition.MAGIC_NUMBER;
            def.NumTransforms = (uint)Transforms.Count;
            def.NumCameraModels = (uint)CameraModels.Count;
            def.NumPoints = (uint)Points.Count;
            def.NumProjections = (uint)Projections.Count;
            def.NumPriors = (uint)TransformPriors.Count;

            WriteStruct(bw, def);
            foreach (var t in Transforms)
            {
                WriteStruct(bw, t);
            }
            foreach (var c in CameraModels)
            {
                WriteStruct(bw, c);
            }
            foreach (var p in Points)
            {
                WriteStruct(bw, p);
            }
            foreach (var p in Projections)
            {
                WriteStruct(bw, p);
            }
            foreach (var p in TransformPriors)
            {
                WriteStruct(bw, p);
            }
        }

        public static BundleAdjusterProblem Read(BinaryReader br)
        {
            BundleAdjusterProblem res = new BundleAdjusterProblem();

            ProblemDefinition header = ReadStruct<ProblemDefinition>(br);
            if (header.Magic != ProblemDefinition.MAGIC_NUMBER)
            {
                throw new InvalidDataException("wrong magic number");
            }

            for (int i = 0; i < header.NumTransforms; i++)
            {
                res.Transforms.Add(ReadStruct<RigidTransform>(br));
            }
            for (int i = 0; i < header.NumCameraModels; i++)
            {
                res.CameraModels.Add(ReadStruct<BundleAdjusterStructures.CameraModel>(br));
            }
            for (int i = 0; i < header.NumPoints; i++)
            {
                res.Points.Add(ReadStruct<Point>(br));
            }
            for (int i = 0; i < header.NumProjections; i++)
            {
                res.Projections.Add(ReadStruct<Projection>(br));
            }
            for (int i = 0; i < header.NumPriors; i++)
            {
                res.TransformPriors.Add(ReadStruct<TransformPrior>(br));
            }

            return res;
        }
    }
}

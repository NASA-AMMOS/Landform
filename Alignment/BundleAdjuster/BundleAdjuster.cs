using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Alignment.BundleAdjusterStructures;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using OPS.Util;
using System.IO;
using System.Runtime.InteropServices;

namespace OPS.Alignment
{
    public class BundleAdjuster
    {
        public class ImageData
        {
            public int CameraModel;
            public int Transform;
            public object UserData;

            internal ImageData(int cameraModel, int transform, object data)
            {
                CameraModel = cameraModel;
                Transform = transform;
                UserData = data;
            }
        }

        public List<OPS.Imaging.CameraModel> CameraModels;
        public List<Projection> Projections;
        public List<Point> Points;
        public List<RigidTransform> Transforms;
        public List<TransformPrior> Priors;
        public List<ImageData> Images;

        public BundleAdjuster()
        {

        }

        public int AddImage(int cameraModel, int transform, object data = null)
        {
            int res = Images.Count;
            Images.Add(new ImageData(cameraModel, transform, data));
            return res;
        }

        public int AddCorrespondence(int image0, int image1, Vector2 pos0, Vector2 pos1, double d0 = double.NaN, double d1 = double.NaN)
        {
            int res = Points.Count;
            var i0 = Images[image0];
            var i1 = Images[image1];

            // TODO: add parameter for point guess or do ray closest intersection thing
            Points.Add(new Point(0, 0, 0, 0));

            Projections.Add(new Projection(
                i0.CameraModel,
                i0.Transform,
                res,
                pos0.X, pos0.Y, d0
                ));
            Projections.Add(new Projection(
                i1.CameraModel,
                i1.Transform,
                res,
                pos1.X, pos1.Y, d1
                ));
            return res;
        }

        public int AddPrior(int transformIdx, Matrix translationCovariance, Matrix rotationCovariance)
        {
            int res = Priors.Count;

            TransformPrior p = new TransformPrior();
            p.transformId = (uint)transformIdx;
            p.translationMean = (-translationCovariance.Translation).ToDoubleArray();
            p.translationCovariance = new double[]
            {
                translationCovariance[0, 0], translationCovariance[0, 1], translationCovariance[0, 2],
                translationCovariance[1, 0], translationCovariance[1, 1], translationCovariance[1, 2],
                translationCovariance[2, 0], translationCovariance[2, 1], translationCovariance[2, 2]
            };
            p.rotationMean = (-translationCovariance.Translation).ToDoubleArray();
            p.rotationCovariance = new double[]
            {
                rotationCovariance[0, 0], rotationCovariance[0, 1], rotationCovariance[0, 2],
                rotationCovariance[1, 0], rotationCovariance[1, 1], rotationCovariance[1, 2],
                rotationCovariance[2, 0], rotationCovariance[2, 1], rotationCovariance[2, 2]
            };
            Priors.Add(p);

            return res;
        }

        static byte[] StructToBytes<T>(T obj)
        {
            int len = Marshal.SizeOf(obj);
            byte[] res = new byte[len];

            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr<T>(obj, ptr, false);
            Marshal.Copy(ptr, res, 0, len);

            return res;
        }

        public void Run()
        {
            ProblemDefinition def = new ProblemDefinition();
            def.Magic = ProblemDefinition.MAGIC_NUMBER;
            def.NumTransforms = (uint)Transforms.Count;
            def.NumCameraModels = (uint)CameraModels.Count;
            def.NumPoints = (uint)Points.Count;
            def.NumProjections = (uint)Projections.Count;
            def.NumPriors = (uint)Priors.Count;

            TemporaryFile.GetAndDelete(".bin", (inputFile) =>
            {
                // Serialize out problem definition
                using (var f = File.OpenWrite(inputFile))
                {
                    using (BinaryWriter bw = new BinaryWriter(f))
                    {
                        bw.Write(StructToBytes(def));
                        foreach (var t in Transforms)
                        {
                            bw.Write(StructToBytes(t));
                        }
                        foreach (var c in CameraModels)
                        {
                            Vector3 C = Vector3.Zero,
                                    A = Vector3.Zero,
                                    H = Vector3.Zero,
                                    V = Vector3.Zero,
                                    O = Vector3.Zero,
                                    R = Vector3.Zero,
                                    E = Vector3.Zero;
                            double linearity = 1;
                            CameraModelType type = CameraModelType.CAHV;

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
                                type = CameraModelType.CAHVOR;
                            }
                            if (c is CAHVORE)
                            {
                                E = ((CAHVORE)c).E;
                                linearity = ((CAHVORE)c).linearityMode.Linearity;
                                type = CameraModelType.CAHVORE;
                            }

                            BundleAdjusterStructures.CameraModel cm = new BundleAdjusterStructures.CameraModel();
                            cm.Type = type;
                            cm.Parameters = new double[]
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
                            bw.Write(StructToBytes(cm));
                        }
                        foreach (var p in Points)
                        {
                            bw.Write(StructToBytes(p));
                        }
                        foreach (var p in Projections)
                        {
                            bw.Write(StructToBytes(p));
                        }
                        foreach (var p in Priors)
                        {
                            bw.Write(StructToBytes(p));
                        }
                    }
                }

                TemporaryFile.GetAndDelete(".bin", (outputFile) =>
                {
                    ProgramRunner pr = new ProgramRunner("CeresBundler.exe", "\"" + inputFile + "\" \"" + outputFile + "\"");
                    pr.Run();
                });
            });
        }
    }
}

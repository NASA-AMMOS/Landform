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
            public int CameraModelIdx;
            public int Transform;
            public object UserData;

            internal ImageData(int cameraModel, int transform, object data)
            {
                CameraModelIdx = cameraModel;
                Transform = transform;
                UserData = data;
            }
        }
        
        public List<ImageData> Images;
        public BundleAdjusterProblem Problem;

        public BundleAdjuster()
        {
            Images = new List<ImageData>();
            Problem = new BundleAdjusterProblem();
        }

        public int AddImage(int cameraModel, int transform, object data = null)
        {
            int res = Images.Count;
            Images.Add(new ImageData(cameraModel, transform, data));
            return res;
        }

        public int AddTransform(Matrix transform)
        {
            return Problem.AddTransform(transform);
        }

        public int AddCorrespondence(int image0, int image1, Vector2 pos0, Vector2 pos1, double d0 = double.NaN, double d1 = double.NaN)
        {
            var i0 = Images[image0];
            var i1 = Images[image1];

            // TODO: add parameter for point guess or do ray closest intersection thing
            int pointIdx = Problem.AddPoint(Vector3.Zero);

            Problem.AddProjection(
                i0.CameraModelIdx,
                i0.Transform,
                pointIdx,
                pos0, d0
                );
            Problem.AddProjection(
                i1.CameraModelIdx,
                i1.Transform,
                pointIdx,
                pos1, d1
                );
            return pointIdx;
        }

        public int AddPrior(int transformIdx, Matrix translationCovariance, Matrix rotationCovariance)
        {
            return Problem.AddPrior(transformIdx, translationCovariance, rotationCovariance);
        }

        public void Run()
        {
            TemporaryFile.GetAndDelete(".bin", (inputFile) =>
            {
                // Serialize out problem definition
                using (var f = File.OpenWrite(inputFile))
                {
                    using (BinaryWriter bw = new BinaryWriter(f))
                    {
                        Problem.Write(bw);
                    }
                }

                TemporaryFile.GetAndDelete(".bin", (outputFile) =>
                {
                    ProgramRunner pr = new ProgramRunner("CeresBundler.exe", "\"" + inputFile + "\" \"" + outputFile + "\"");
                    pr.Run();

                    using (var f = File.OpenRead(outputFile))
                    {
                        using (var br = new BinaryReader(f))
                        {
                            var res = BundleAdjusterProblem.Read(br);
                            Problem.Transforms = res.Transforms;
                            Problem.Points = res.Points;
                            Problem.CameraModels = res.CameraModels;
                        }
                    }
                });
            });
        }
    }
}

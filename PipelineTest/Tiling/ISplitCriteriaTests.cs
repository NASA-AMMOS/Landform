using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using OPS.RayTrace;

namespace PipelineTest
{
    [TestClass()]
    public class ISplitCriteriaTests
    {
        [TestMethod()]
        public void FaceShouldSplitTest()
        {
            BoundingBox box = new BoundingBox(-1 * Vector3.One, Vector3.One);
            MeshOperator op = new MeshOperator(box.ToMesh());

            ITileSplitCriteria split = new FaceSplitCriteria(7);
            Assert.IsTrue(split.ShouldSplit(op, box));

            BoundingBox quarterBox = new BoundingBox(Vector3.Zero, Vector3.One);
            Assert.IsFalse(split.ShouldSplit(op, quarterBox));
        }

        [TestMethod()]
        public void TextureShouldSplitTest()
        {
            int destTextureResolution = 128;
            int srcTextureResolution = 256;
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcTextureResolution));
        }

        [TestMethod()]
        public void TextureShouldntSplitTest()
        {
            int destTextureResolution = 128;
            int srcTextureResolution = 64;
            Assert.IsFalse(StandardTexSplit(destTextureResolution, srcTextureResolution));
        }

        private static bool StandardTexSplit(int destTextureResolution, int srcTextureResolution)
        {
            BoundingBox box = new BoundingBox(-1 * Vector3.One, Vector3.One);
            Mesh mesh = box.ToMesh();

            mesh = UVAtlas.Atlas(mesh, destTextureResolution, destTextureResolution);
            MeshOperator op = new MeshOperator(mesh);
            SceneCaster sc = new SceneCaster();
            sc.AddMesh(mesh, null, Matrix.Identity);
            sc.Build();
            
            double focalLength = srcTextureResolution / 2.0;
            Vector3 camC = new Vector3(-3, 0, 0);
            Vector3 camA = new Vector3(1, 0, 0);
            Vector3 camH = new Vector3(0, 1, 0) * focalLength + camA * srcTextureResolution / 2.0;
            Vector3 camV = new Vector3(0, 0, 1) * focalLength + camA * srcTextureResolution / 2.0;
            CAHV cahv = new CAHV(camC, camA, camH, camV);
            ConvexHull camHull = ConvexHull.FromParams(cahv, srcTextureResolution, srcTextureResolution, 0.1, 4);

            CameraInstance[] cameraInstances = new CameraInstance[]
            {
                new CameraInstance()
                {
                    cameraToMesh = Matrix.Identity,
                    meshToCamera = Matrix.Identity,
                    cameraModel = cahv,
                    hullInMesh = camHull,
                    widthPixels = srcTextureResolution,
                    heightPixels = srcTextureResolution
                }
            };

            SplitByTextureOpts opts = new SplitByTextureOpts()
            {
                pctPixelsToTest = 0.5,
                pctSampledPixelsServiced = 0.75,
                subsamplingTriggeringSplit = 2.0,
                tileResolution = destTextureResolution,
                cameraInstances = cameraInstances,
                scInMesh = sc
            };

            ITileSplitCriteria split = new TextureSplitCriteria(opts);
            return split.ShouldSplit(op, box);            
        }
    }
}
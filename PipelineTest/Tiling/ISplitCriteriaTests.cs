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
            int destTextureResolution = 4;
            int srcTextureResolution = 8;
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcTextureResolution));

            destTextureResolution = 3;
            srcTextureResolution = 5;
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcTextureResolution));
        }

        [TestMethod()]
        public void TextureShouldntSplitTest()
        {
            int destTextureResolution = 4;
            int srcTextureResolution = 2;
            Assert.IsFalse(StandardTexSplit(destTextureResolution, srcTextureResolution));

            destTextureResolution = 4;
            srcTextureResolution = 3;
            Assert.IsFalse(StandardTexSplit(destTextureResolution, srcTextureResolution));

            destTextureResolution = 4;
            srcTextureResolution = 4;
            Assert.IsFalse(StandardTexSplit(destTextureResolution, srcTextureResolution));
        }

        private static bool StandardTexSplit(int destTextureResolution, int srcTextureResolution)
        {
            //uv origin: lower left
            Vertex ul = new Vertex(new Vector3(0, -1, -1), new Vector3(-1, 0, 0), new Vector4(1, 0, 0, 1), new Vector2(0,1));
            Vertex ll = new Vertex(new Vector3(0, -1, 1), new Vector3(-1, 0, 0), new Vector4(0, 0, 1, 1), new Vector2(0,0));
            Vertex ur = new Vertex(new Vector3(0, 1, -1), new Vector3(-1, 0, 0), new Vector4(0, 1, 0, 1), new Vector2(1, 1));
            Vertex lr = new Vertex(new Vector3(0, 1, 1), new Vector3(-1, 0, 0), new Vector4(1, 0, 1, 1), new Vector2(1,0));

            Triangle tri0 = new Triangle(ul, ll, ur);
            Triangle tri1 = new Triangle(ll, ur, lr);

            Mesh mesh0 = new Mesh(new List<Triangle>() { tri0, tri1 }, true, true, true);

            //Issue #559:convex hull of a quad fails currently, duplicating the mesh to add some thickness
            Mesh mesh1 = Mesh.Transformed(mesh0, Matrix.CreateTranslation(new Vector3(0.25, 0, 0)));

            Mesh mesh = Mesh.Merge(new Mesh[] { mesh0, mesh1 });
            BoundingBox box = new BoundingBox(-1 * Vector3.One, Vector3.One);

            MeshOperator op = new MeshOperator(mesh);
            SceneCaster sc = new SceneCaster();
            sc.AddMesh(mesh, null, Matrix.Identity);
            sc.Build();

            double focalLength = srcTextureResolution;
            Vector3 camC = new Vector3(-2, 0, 0);
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
                pctSampledPixelsSatisfied = 0.75,
                subsamplingTriggeringSplit = 2.0,
                tileResolution = destTextureResolution,
                cameraInstances = cameraInstances,
                scInMesh = sc
            };

            ITileSplitCriteria split = new TextureSplitCriteriaBackproject(opts);
            return split.ShouldSplit(op, box);
        }
    }
}
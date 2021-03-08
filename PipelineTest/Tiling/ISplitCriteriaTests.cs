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
            Assert.IsTrue(split.ShouldSplit(box, op));

            BoundingBox quarterBox = new BoundingBox(Vector3.Zero, Vector3.One);
            Assert.IsFalse(split.ShouldSplit(quarterBox, op));
        }

        [TestMethod()]
        public void TextureShouldSplitTest()
        {
            int destTextureResolution = 256; //65536 texels / m^2 (half that for approx)
            int srcImageResolution = 1000; //1M pixels / m^2
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcImageResolution,false));
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcImageResolution,true));

            srcImageResolution = 360; //129600 pixels / m^2
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcImageResolution, false));
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcImageResolution, true));
        }

        [TestMethod()]
        public void TextureShouldntSplitTest()
        {
            int destTextureResolution = 256; //65536 texels / m^2 (half that for approx)
            int srcImageResolution = 350; //122500 pixels / m^2
            Assert.IsFalse(StandardTexSplit(destTextureResolution, srcImageResolution, false));
            Assert.IsTrue(StandardTexSplit(destTextureResolution, srcImageResolution, true));

            srcImageResolution = 250; //62500 pixels / m^2
            Assert.IsFalse(StandardTexSplit(destTextureResolution, srcImageResolution, false));
            Assert.IsFalse(StandardTexSplit(destTextureResolution, srcImageResolution, true));
        }

        private static bool StandardTexSplit(int destTextureResolution, int srcImageResolution, bool approx)
        {
            // +----Y
            // |
            // |
            // Z
            
            //uv origin: lower left
            Vertex ul =
                //             position                normal                 color                    uv
                new Vertex(new Vector3(0, -0.5, -0.5), new Vector3(-1, 0, 0), new Vector4(1,0,0,1), new Vector2(0,1));
            Vertex ll =
                new Vertex(new Vector3(0, -0.5, 0.5), new Vector3(-1, 0, 0), new Vector4(0,0,1,1), new Vector2(0,0));
            Vertex ur =
                new Vertex(new Vector3(0, 0.5, -0.5), new Vector3(-1, 0, 0), new Vector4(0,1,0,1), new Vector2(1, 1));
            Vertex lr =
                new Vertex(new Vector3(0, 0.5, 0.5), new Vector3(-1, 0, 0), new Vector4(1,0,1,1), new Vector2(1,0));

            Triangle tri0 = new Triangle(ul, ll, ur);
            Triangle tri1 = new Triangle(ll, lr, ur);

            Mesh mesh = new Mesh(new List<Triangle>() { tri0, tri1 }, true, true, true);

            BoundingBox box = new BoundingBox(-1 * Vector3.One, Vector3.One);

            MeshOperator op = new MeshOperator(mesh);

            SceneCaster sc = new SceneCaster();
            sc.AddMesh(mesh, null, Matrix.Identity);
            sc.Build();

            //construct camera which vews whole mesh exactly
            double focalLength = srcImageResolution;
            Vector3 camC = new Vector3(-1, 0, 0);
            Vector3 camA = new Vector3(1, 0, 0);
            Vector3 camH = new Vector3(0, 1, 0) * focalLength + camA * srcImageResolution / 2.0;
            Vector3 camV = new Vector3(0, 0, 1) * focalLength + camA * srcImageResolution / 2.0;
            CAHV cahv = new CAHV(camC, camA, camH, camV);
            ConvexHull camHull = ConvexHull.FromParams(cahv, srcImageResolution, srcImageResolution, 0.1, 4);

            CameraInstance[] cameraInstances = new CameraInstance[]
            {
                new CameraInstance()
                {
                    cameraToMesh = Matrix.Identity,
                    meshToCamera = Matrix.Identity,
                    cameraModel = cahv,
                    hullInMesh = camHull,
                    widthPixels = srcImageResolution,
                    heightPixels = srcImageResolution
                }
            };

            SplitByTextureOpts opts = new SplitByTextureOpts()
            {
                pctPixelsToTest = 0.5,
                pctSampledPixelsSatisfied = 0.75,
                splitPixelTexelRatio = 2.0,
                maxTileResolution = destTextureResolution,
                cameraInstances = cameraInstances,
                scInMesh = sc
            };

            ITileSplitCriteria split = new TextureSplitCriteriaBackproject(opts);
            if (approx)
            {
                split = new TextureSplitCriteriaApproximate(opts);
            }
            return split.ShouldSplit(box, op);
        }
    }
}

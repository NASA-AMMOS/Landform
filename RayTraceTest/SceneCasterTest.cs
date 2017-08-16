using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;
using System.IO;
using OPS.Imaging;
using OPS.RayTrace;
using Microsoft.Xna.Framework;
using OPS.Test;

namespace RayTraceTest
{
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("x86", "x86")]
    [DeploymentItem("x64", "x64")]
    public class SceneCasterTest
    {

        static Mesh LoadMesh()
        {
            return Mesh.Load(Path.Combine("TestData", "mesh", "raptor.obj"));
        }

        static Image LoadImage()
        {
            return Image.Load(Path.Combine("TestData", "mesh", "raptor.jpg"));
        }

        static Image RenderOrtho(SceneCaster sc, Matrix transform, int width, int height, double worldHeight)
        {            
            GenericImage<Ray> rays = new GenericImage<Ray>(1, width, height);
            double metersPerPixel = worldHeight / height;
            Vector3 forward = transform.Forward;
            Vector3 down = transform.Down;
            Vector3 right = transform.Right;
            for (int r= 0; r < height; r++)
            {
                for(int c= 0; c < width; c++)
                {
                    Vector3 origin = transform.Translation;
                    origin += right * metersPerPixel * (c - (width / 2.0));
                    origin += down * metersPerPixel * (r - (height / 2.0));
                    rays[0, r, c] = new Ray(origin, forward);
                }
            }
            GenericImage<HitData> hits = new GenericImage<HitData>(1, width, height);
            Image img = new Image(3, width, height);
            img.ApplyInPlace(x => 1);   // Fill with white
            img.CreateMask(true);
            for (int i = 0; i < hits.Data[0].Length; i++)
            {
                var hit = sc.Raycast(rays.Data[0][i]);
                img.SetMaskValue(i, !sc.Occludes(rays.Data[0][i]));
                hits.Data[0][i] = hit;
            }            
            
            for(int i = 0; i < hits.Data[0].Length; i++)
            {
                var hit = hits.Data[0][i];
                if (hit != null)
                {
                    var pixel = hit.Texture.UVToPixel(hit.UV.Value);
                    img.Data[0][i] = hit.Texture[0, (int)pixel.Y, (int)pixel.X];
                    img.Data[1][i] = hit.Texture[1, (int)pixel.Y, (int)pixel.X];
                    img.Data[2][i] = hit.Texture[2, (int)pixel.Y, (int)pixel.X];
                }
            }
            bool anyNonFillPixels = false;
            for (int i = 0; i < img.Data[0].Length; i++)
            {
                if(img.Data[0][i] != 1 || img.Data[1][i] != 1 | img.Data[2][i] != 1)
                {
                    anyNonFillPixels = true;
                    break;
                }
            }
            Assert.IsTrue(anyNonFillPixels);
            bool anyOccluded = false;
            for (int i = 0; i < img.Data[0].Length; i++)
            {
                if (img.IsValid(i))
                {
                    anyOccluded = true;
                    break;
                }
            }
            Assert.IsTrue(anyOccluded);
            return img;
        }

        static void RenderRaptor(string filename, Matrix raptorMatrix, Matrix cameraMatrix)
        {
            var mesh = LoadMesh();
            var image = LoadImage();
            mesh.Center();
            var sc = new SceneCaster();            
            sc.AddMesh(mesh, image, raptorMatrix);
            sc.Build();
            var hit = sc.Raycast(new Ray(new Vector3(0, 0, -10), new Vector3(0, 0, 1)));            
            var img = RenderOrtho(sc, cameraMatrix, 512, 256, 120);
            Image mask = new Image(img.Bands, img.Width, img.Height);
            for(int i = 0; i < mask.Data[0].Length; i++)
            {
                if(img.IsValid(i))
                {
                    mask.Data[0][i] = 1;
                }
            }
            img.DeleteMask();
            img.Save<byte>(filename);
            mask.Save<byte>(filename + "_occlusion.png");
        }

        static SceneCaster SimpleMeshSceneCaster(Matrix meshMatrix)
        {
            Mesh m = new Mesh(true, true);
            m.Vertices.Add(new Vertex(-1, -1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(-1, 1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(1, -1, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0));
            m.Faces.Add(new Face(0, 1, 3));
            m.Faces.Add(new Face(0, 3, 2));            
            var sc = new SceneCaster();
            sc.AddMesh(m, null, meshMatrix);
            sc.Build();
            return sc;
        }

        [TestMethod]
        public void SceneCasterRenderTest()
        {
            Matrix dinoMat = Matrix.Identity;
            Matrix cameraMatrix = Matrix.CreateLookAt(new Vector3(0, 0, -10), new Vector3(0, 0, 0), Vector3.Up);
            RenderRaptor("dinoIdent.png", dinoMat, cameraMatrix);
            dinoMat = Matrix.CreateTranslation(new Vector3(20, 0, 0));
            RenderRaptor("dinoNegX.png", dinoMat, cameraMatrix);
            dinoMat = Matrix.CreateFromAxisAngle(Vector3.Up, Math.PI);
            RenderRaptor("dinoRotate.png", dinoMat, cameraMatrix);
            dinoMat = Matrix.CreateScale(0.5);
            RenderRaptor("dinoScale.png", dinoMat, cameraMatrix);
        }

        [TestMethod]
        public void SceneCasterTestMatrix()
        {
            //No rotation
            {
                var sc = SimpleMeshSceneCaster(Matrix.Identity);
                var hit = sc.Raycast(new Ray(new Vector3(0, 0, -1), new Vector3(0, 0, 1)));
                AssertE.AreSimilar(1, hit.Distance, 1E-5);
                Assert.IsTrue(Vector2.AlmostEqual(new Vector2(0.5, 0.5), hit.UV.Value));
                Assert.IsTrue(Vector3.AlmostEqual(hit.Position, Vector3.Zero));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1)));
                hit = sc.Raycast(new Ray(new Vector3(0.1, 0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1)));
                hit = sc.Raycast(new Ray(new Vector3(-0.1, -0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1)));
                hit = sc.Raycast(new Ray(new Vector3(0.1, -0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1)));
                hit = sc.Raycast(new Ray(new Vector3(-0.1, 0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1)));
            }

            // Rotation and translation
            {
                var rotMat = Matrix.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(45));
                var transMat = Matrix.CreateTranslation(new Vector3(0, 0, -0.5));
                var mat = Matrix.Multiply(rotMat, transMat);
                var sc = SimpleMeshSceneCaster(mat);
                var hit = sc.Raycast(new Ray(new Vector3(0, 0, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.Position, new Vector3(0, 0, -0.5)));
                Assert.IsTrue(Vector2.AlmostEqual(new Vector2(0.5, 0.5), hit.UV.Value));
                AssertE.AreSimilar(0.5, hit.Distance, 1E-5);
                Vector3 norm = new Vector3(-1, 0, -1);
                norm.Normalize();
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, norm));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, -norm));
                hit = sc.Raycast(new Ray(new Vector3(0.1, 0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, norm));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, -norm));
                hit = sc.Raycast(new Ray(new Vector3(-0.1, -0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, norm));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, -norm));
                hit = sc.Raycast(new Ray(new Vector3(0.1, -0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, norm));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, -norm));
                hit = sc.Raycast(new Ray(new Vector3(-0.1, 0.1, -1), new Vector3(0, 0, 1)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, norm));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, -norm));
                hit = sc.Raycast(new Ray(new Vector3(2, 0, -0.5), new Vector3(-1, 0, 0)));
                Assert.IsTrue(Vector3.AlmostEqual(hit.FaceNormal, norm));
                Assert.IsTrue(Vector3.AlmostEqual(hit.PointNormal.Value, -norm));
            }

        }
    }
}

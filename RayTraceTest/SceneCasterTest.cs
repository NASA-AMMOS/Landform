using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using System.IO;
using JPLOPS.Imaging;
using JPLOPS.RayTrace;
using Microsoft.Xna.Framework;
using JPLOPS.Test;

namespace RayTraceTest
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("x64", "x64")]
    public class SceneCasterTest
    {
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
            HitData[] hitData = hits.GetBandData(0);
            Ray[] rayData = rays.GetBandData(0);
            for (int i = 0; i < hitData.Length; i++)
            {
                var hit = sc.Raycast(rayData[i]);
                img.SetMaskValue(i, !sc.Occludes(rayData[i]));
                hitData[i] = hit;
            }            

            float[][] imgData = new float[3][];
            for (int i = 0; i < 3; i++)
            {
                imgData[i] = img.GetBandData(i);
            }

            for(int i = 0; i < hitData.Length; i++)
            {
                var hit = hitData[i];
                if (hit != null)
                {
                    var pixel = hit.Texture.UVToPixel(hit.UV.Value);
                    imgData[0][i] = hit.Texture[0, (int)pixel.Y, (int)pixel.X];
                    imgData[1][i] = hit.Texture[1, (int)pixel.Y, (int)pixel.X];
                    imgData[2][i] = hit.Texture[2, (int)pixel.Y, (int)pixel.X];
                }
            }
            bool anyNonFillPixels = false;
            for (int i = 0; i < imgData[0].Length; i++)
            {
                if(imgData[0][i] != 1 || imgData[1][i] != 1 | imgData[2][i] != 1)
                {
                    anyNonFillPixels = true;
                    break;
                }
            }
            Assert.IsTrue(anyNonFillPixels);
            bool anyOccluded = false;
            for (int i = 0; i < imgData[0].Length; i++)
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

        static SceneCaster SimpleMeshSceneCaster(Matrix meshMatrix)
        {
            Mesh m = new Mesh(true, true);
            m.Vertices.Add(new Vertex(-1, -1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(-1,  1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex( 1, -1, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex( 1,  1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0));
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

        private void AlmostEqual(Vector2 a, Vector2 b, string msg)
        {
            Assert.IsTrue(Vector2.AlmostEqual(a, b), msg + " a=" + a + ", b=" + b);
        }

        private void AlmostEqual(Vector3 a, Vector3 b, string msg)
        {
            Assert.IsTrue(Vector3.AlmostEqual(a, b), msg + " a=" + a + ", b=" + b);
        }

        [TestMethod]
        public void SceneCasterTestMatrix()
        {
            //No rotation
            {
                var sc = SimpleMeshSceneCaster(Matrix.Identity);
                var hit = sc.Raycast(new Ray(new Vector3(0, 0, -1), new Vector3(0, 0, 1)));
                AssertE.AreSimilar(1, hit.Distance, 1E-5);

                AlmostEqual(new Vector2(0.5, 0.5), hit.UV.Value, "uv");
                AlmostEqual(hit.Position, Vector3.Zero, "position");
                AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1), "face normal");
                AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1), "point normal");

                hit = sc.Raycast(new Ray(new Vector3(0.1, 0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1), "face normal");
                AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1), "point normal");

                hit = sc.Raycast(new Ray(new Vector3(-0.1, -0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1), "face normal");
                AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1), "point normal");

                hit = sc.Raycast(new Ray(new Vector3(0.1, -0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1), "face normal");
                AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1), "point normal");

                hit = sc.Raycast(new Ray(new Vector3(-0.1, 0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, new Vector3(0, 0, -1), "face normal");
                AlmostEqual(hit.PointNormal.Value, new Vector3(0, 0, 1), "point normal");
            }

            // Rotation and translation
            {
                var rotMat = Matrix.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(45));
                var transMat = Matrix.CreateTranslation(new Vector3(0, 0, -0.5));
                var mat = Matrix.Multiply(rotMat, transMat);
                var sc = SimpleMeshSceneCaster(mat);

                var hit = sc.Raycast(new Ray(new Vector3(0, 0, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.Position, new Vector3(0, 0, -0.5), "position");
                AlmostEqual(new Vector2(0.5, 0.5), hit.UV.Value, "uv");

                AssertE.AreSimilar(0.5, hit.Distance, 1E-5);
                Vector3 norm = new Vector3(-1, 0, -1);
                norm.Normalize();

                AlmostEqual(hit.FaceNormal, norm, "face normal");
                AlmostEqual(hit.PointNormal.Value, -norm, "point normal");

                hit = sc.Raycast(new Ray(new Vector3(0.1, 0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, norm, "face normal");
                AlmostEqual(hit.PointNormal.Value, -norm, "point normal");

                hit = sc.Raycast(new Ray(new Vector3(-0.1, -0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, norm, "face normal");
                AlmostEqual(hit.PointNormal.Value, -norm, "point normal");

                hit = sc.Raycast(new Ray(new Vector3(0.1, -0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, norm, "face normal");
                AlmostEqual(hit.PointNormal.Value, -norm, "point normal");

                hit = sc.Raycast(new Ray(new Vector3(-0.1, 0.1, -1), new Vector3(0, 0, 1)));
                AlmostEqual(hit.FaceNormal, norm, "face normal");
                AlmostEqual(hit.PointNormal.Value, -norm, "point normal");

                hit = sc.Raycast(new Ray(new Vector3(2, 0, -0.5), new Vector3(-1, 0, 0)));
                AlmostEqual(hit.FaceNormal, norm, "face normal");
                AlmostEqual(hit.PointNormal.Value, -norm, "point normal");
            }
        }
    }
}

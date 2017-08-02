using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;
using System.IO;
using OPS.Imaging;
using OPS.RayTrace;
using Microsoft.Xna.Framework;

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

        static Image RenderOrtho(SceneCaster sc, Matrix transform, int width, int height, double worldHeight, bool multi)
        {            
            GenericImage<Ray> rays = new GenericImage<Ray>(1, width, height);
            double metersPerPixel = height / worldHeight;
            Vector3 direction = Vector3.TransformNormal(Vector3.Forward, transform);
            Vector3 up = Vector3.TransformNormal(Vector3.Up, transform);
            Vector3 right = Vector3.TransformNormal(Vector3.Right, transform);
            Vector3 origin = transform.Translation - (right * metersPerPixel * width/2) - (up * metersPerPixel * height/2);
            for (int r= 0; r < height; r++)
            {
                for(int c= 0; c < width; c++)
                {
                    Vector3 p = origin + up * r + right * c;
                    rays[0, r, c] = new Ray(p, direction);
                }
            }
            GenericImage<HitData> hits = new GenericImage<HitData>(1, width, height);
            if(multi)
            {
                hits.Data[0] = sc.Raycast(rays.Data[0]);
            }
            else
            {
                for(int i = 0; i < hits.Data[0].Length; i++)
                {
                    hits.Data[0][i] = sc.Raycast(rays.Data[0][i]);
                }
            }
            Image img = new Image(3, width, height);
            img.ApplyInPlace(x => 1);   // Fill with white
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
            return img;
        }

        [TestMethod]
        public void SceneCasterSingeRayTest()
        {
            var mesh = LoadMesh();
            var image = LoadImage();

            var sc = new SceneCaster();
            sc.AddMesh(mesh, image, Matrix.Identity);
            sc.Build();
            Matrix cameraPos = Matrix.CreateTranslation(new Vector3(0, 0, 3));
            var img = RenderOrtho(sc, cameraPos, 512, 512, 5, false);
            img.Save<byte>("dino.png");
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using System.IO;
using JPLOPS.Pipeline;
using Microsoft.Xna.Framework;

namespace PipelineTest
{
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("x86", "x86")]
    [DeploymentItem("x64", "x64")]
    public class TexturedMeshClipperTest
    {
        static Mesh LoadMesh()
        {
            return Mesh.Load(Path.Combine("TestData", "mesh", "raptor.obj"));
        }

        static Image LoadImage()
        {
            return Image.Load(Path.Combine("TestData", "mesh", "raptor.jpg"));
        }

        [TestMethod]
        public void TexturedMeshClipTest()
        {
            Mesh mesh = LoadMesh();
            mesh.Clean();
            Image img = LoadImage();
            MeshImagePair pair = new MeshImagePair(mesh, img);
            BoundingBox box = new BoundingBox(new Vector3(0), new Vector3(70));
            TexturedMeshClipper clipper = new TexturedMeshClipper();
            clipper.AddInput(pair);
            MeshImagePair clippedPair = clipper.Clip(box);
            Assert.IsTrue(clippedPair.Mesh.HasFaces);
            Assert.IsTrue(clippedPair.Image.Width > 0 && clippedPair.Image.Height > 0);
            Assert.IsTrue(clippedPair.Image.Width <= pair.Image.Width && clippedPair.Image.Height <= pair.Image.Height);
            clippedPair.Image.Save<byte>("clippedTexture.png");
        }

        [TestMethod]
        public void MultipleTexturedMeshClipTest()
        {
            TexturedMeshClipper clipper = new TexturedMeshClipper();
            Mesh mesh1 = LoadMesh();
            Mesh mesh2 = new Mesh(mesh1);
            mesh1.Clean();
            mesh2.Clean();
            Image img = LoadImage();
            clipper.AddInput(new MeshImagePair(mesh1, img));
            mesh2.Translate(new Vector3(0, 0, 60));
            clipper.AddInput(new MeshImagePair(mesh2, img));
            BoundingBox box = new BoundingBox(new Vector3(-100, 60, 0), new Vector3(-30, 90, 120));

            MeshImagePair clippedPair = clipper.Clip(box);
            Assert.IsTrue(clippedPair.Mesh.HasFaces);
            Assert.IsTrue(clippedPair.Image.Width > 0 && clippedPair.Image.Height > 0);
            Assert.IsTrue(clippedPair.Image.Width * clippedPair.Image.Height <= img.Width * img.Height * 2);
            clippedPair.Image.Save<byte>("clippedTexture.png");
            clippedPair.Mesh.Save("clippedMesh.ply", "clippedTexture.png");
        }
    }
}

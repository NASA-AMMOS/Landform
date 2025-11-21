using System;
using System.IO;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using JPLOPS.Pipeline;

namespace PipelineTest
{
    public class TexturedMeshClipperTest : IDisposable
    {
        private string cwd;

        public TexturedMeshClipperTest()
        {
            cwd = System.Environment.CurrentDirectory;
            if (cwd.EndsWith("x64"))
            {
                Directory.SetCurrentDirectory(Directory.GetParent(cwd)!.FullName);
            }
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(cwd);
        }

        static Mesh LoadMesh()
        {
            return Mesh.Load("raptor.obj");
        }

        static Image LoadImage()
        {
            return Image.Load("raptor.jpg");
        }

        [Fact]
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
            Assert.True(clippedPair.Mesh.HasFaces);
            Assert.True(clippedPair.Image.Width > 0 && clippedPair.Image.Height > 0);
            Assert.True(clippedPair.Image.Width <= pair.Image.Width && clippedPair.Image.Height <= pair.Image.Height);
            clippedPair.Image.Save<byte>("clippedTexture.png");
        }

        [Fact]
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
            Assert.True(clippedPair.Mesh.HasFaces);
            Assert.True(clippedPair.Image.Width > 0 && clippedPair.Image.Height > 0);
            Assert.True(clippedPair.Image.Width * clippedPair.Image.Height <= img.Width * img.Height * 2);
            clippedPair.Image.Save<byte>("clippedTexture.png");
            clippedPair.Mesh.Save("clippedMesh.ply", "clippedTexture.png");
        }
    }
}

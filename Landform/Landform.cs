using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Cloud;
using OPS.Pipeline;
using log4net;
using OPS.Util;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Landform
{
    class Landform
    {
        static ILog logger = LogManager.GetLogger(typeof(Landform));

        /// <summary>
        /// The start of everything
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Main(string[] args)
        {
            Config.ApplicationConfigFolder = ".landform";
            // Enable logging
            log4net.Config.XmlConfigurator.Configure();
            // Register filetype handlers
            new OpenInventorSerializer().Register();

            int width = 512;
            int height = 512;

            string root = @"C:\Users\schibler\Desktop\QuadricEdgeCollapseTestOutputs\dst";
            string imgFilename = root + ".jpg";

            /*while (true)
            {
                SceneNode asdf = new SceneNode();
                var a = asdf.Name;
                ;
            }*/

            //var dst = Mesh.Load(@"C:\Users\schibler\Desktop\BlenderBakedMesh\m033333333312_atlased.obj");
            //var dst = Mesh.Load(@"C:\Users\schibler\Repos\Quadric-Edge-Collapse\TestData\TestData\mesh\Raptor.obj");
            //var dst = Mesh.Load(@"C:\Users\schibler\Repos\Quadric-Edge-Collapse\TestData\TestData\mesh\T-Rex.obj");
            var dst = Mesh.Load(@"C:\Users\schibler\Desktop\mesh_surface_raw.ply");
            /*var a = new MeshImagePair(Mesh.Load(@"C:\Users\schibler\Desktop\parents\t033333333300.obj"), Image.Load(@"C:\Users\schibler\Desktop\parents\t033333333300.jpg"));
            var b = new MeshImagePair(Mesh.Load(@"C:\Users\schibler\Desktop\parents\t033333333301.obj"), Image.Load(@"C:\Users\schibler\Desktop\parents\t033333333301.jpg"));
            var c = new MeshImagePair(Mesh.Load(@"C:\Users\schibler\Desktop\parents\t033333333302.obj"), Image.Load(@"C:\Users\schibler\Desktop\parents\t033333333302.jpg"));
            var d = new MeshImagePair(Mesh.Load(@"C:\Users\schibler\Desktop\parents\t033333333303.obj"), Image.Load(@"C:\Users\schibler\Desktop\parents\t033333333303.jpg"));
            */
            /*var a = new MeshImagePair(Mesh.Load(@"C:\\Users\schibler\Desktop\BlenderBakedMesh\m0333333333120_atlased.obj"), Image.Load(@"C:\Users\schibler\Desktop\BlenderBakedMesh\t0333333333120h.jpg"));
            var b = new MeshImagePair(Mesh.Load(@"C:\\Users\schibler\Desktop\BlenderBakedMesh\m0333333333121_atlased.obj"), Image.Load(@"C:\Users\schibler\Desktop\BlenderBakedMesh\t0333333333121h.jpg"));
            var c = new MeshImagePair(Mesh.Load(@"C:\\Users\schibler\Desktop\BlenderBakedMesh\m0333333333122_atlased.obj"), Image.Load(@"C:\Users\schibler\Desktop\BlenderBakedMesh\t0333333333122h.jpg"));
            var d = new MeshImagePair(Mesh.Load(@"C:\\Users\schibler\Desktop\BlenderBakedMesh\m0333333333123_atlased.obj"), Image.Load(@"C:\Users\schibler\Desktop\BlenderBakedMesh\t0333333333123h.jpg"));
            */

            /*var dst = Mesh.Merge(a.Mesh, b.Mesh, c.Mesh, d.Mesh);
            dst.Clean();
            dst = MeshLab.ComputeNormals(dst);

            SurfacePointSampler sps = new SurfacePointSampler();
            Mesh pc = sps.GenerateSampledMesh(dst, 100);
            pc.HasUVs = false;
            
            Mesh dst1 = FSSR.PoissonReconstruct(pc);
            */
            //dst1.Save(root + "_nodec.obj");
            
            var dst1 = EdgeCollapse.QuadricEdgeCollapse(dst, 10000, preserveTopology : false, avoidFlips : true, flipThreshold : -1.0, weightByArea : false, avoidSmallTris : false, angleThreshold : 0.25, notTouched: dst.Corners(new Vector3(0,0,1)));
            
            //dst1.HasNormals = false;
            //var dst2 = UVAtlas.Atlas(dst1, width, height);

            

            //var pair1 = new MeshImagePair(Mesh.Load(@"C:\Users\schibler\Repos\Quadric-Edge-Collapse\TestData\TestData\mesh\Raptor.obj"), Image.Load(@"C:\Users\schibler\Repos\Quadric-Edge-Collapse\TestData\TestData\mesh\Raptor.jpg"));
            //var pair1 = new MeshImagePair(Mesh.Load(@"C:\Users\schibler\Repos\Quadric-Edge-Collapse\TestData\TestData\mesh\T-Rex.obj"), Image.Load(@"C:\Users\schibler\Repos\Quadric-Edge-Collapse\TestData\TestData\mesh\T-Rex.jpg"));

            //MeshImagePair[] pairs = { a, b, c, d };
            //var img = TextureBaker.BakeTexture(pairs, dst2, width, height);
            
            
            dst1.Save(root + ".obj");

            //img.Save<byte>(imgFilename);
            //dst2.Save(root + ".obj", Path.GetFileName(imgFilename));
            
            //res.Save(root + ".obj");

            System.Diagnostics.Process.Start(@"C:\Users\schibler\Desktop\QuadricEdgeCollapseTestOutputs\dst.obj");
            //System.Diagnostics.Process.Start(@"C:\Users\schibler\Desktop\QuadricEdgeCollapseTestOutputs\dst_nodec.obj");

            // Parse command line arguments
            //int returnCode = Commands.RunFromCommandline(args);
            return 0;
        }
    }
}

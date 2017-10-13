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

            //Configure gdal
            GdalConfiguration.ConfigureGdal();

            ////run a lil image pipeline 

            //int width = 512;
            //int height = 512;

            //var pair1 = new MeshImagePair(Mesh.Load(@"C:\\Users\gpease\Documents\texterBakeInput\t3000000000320.obj"), Image.Load(@"C:\Users\gpease\Documents\texterBakeInput\t3000000000320.jpg"));
            //var pair2 = new MeshImagePair(Mesh.Load(@"C:\\Users\gpease\Documents\texterBakeInput\t3000000000331.obj"), Image.Load(@"C:\Users\gpease\Documents\texterBakeInput\t3000000000331.jpg"));
            //var pair3 = new MeshImagePair(Mesh.Load(@"C:\\Users\gpease\Documents\texterBakeInput\t3000000000302.obj"), Image.Load(@"C:\Users\gpease\Documents\texterBakeInput\t3000000000302.jpg"));
            //var pair4 = new MeshImagePair(Mesh.Load(@"C:\\Users\gpease\Documents\texterBakeInput\t3000000000313.obj"), Image.Load(@"C:\Users\gpease\Documents\texterBakeInput\t3000000000313.jpg"));

            //Mesh dst = Mesh.Merge(new Mesh[] { pair1.Mesh, pair2.Mesh, pair3.Mesh, pair4.Mesh });

            //var dst1 = EdgeCollapse.QuadricEdgeCollapse(dst, pair1.Mesh.Faces.Count, 1, notTouched: EdgeCollapse.GetCorners(dst));
            //dst1.HasNormals = false;

            //var dst2 = UVAtlas.Atlas(dst1, width, height, maxStretch: 0.025f);

            //MeshImagePair[] pairs = { pair1, pair2, pair3, pair4 };
            //var img = TextureBaker.BakeTexture(pairs, dst2, width, height);

            //string root = @"C:\Users\gpease\Documents\test-out";
            //string imgFilename = root + ".jpg";
            ////dst1.Save(root + ".obj");
            //img.Save<byte>(imgFilename);
            //dst2.Save(root + ".obj", Path.GetFileName(imgFilename));

            ////System.Diagnostics.Process.Start(@"C:\Users\schibler\Desktop\QuadricEdgeCollapseTestOutputs\dst.obj");


            // Parse command line arguments
            int returnCode = Commands.RunFromCommandline(args);
            return returnCode;
        }
    }
}
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
            // Enable logging
            log4net.Config.XmlConfigurator.Configure();
            // Register filetype handlers
            new OpenInventorSerializer().Register();
            // Load mesh
            Mesh mesh = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop\normals.obj");

            // Sample points on mesh
            Vertex[] sampled = SurfacePointSample.Sample(mesh, 300);

            List<Vertex> sampledVertexList = new List<Vertex>(sampled.Length);
            for (int i = 0; i < sampled.Length; i++)
            {
                sampledVertexList.Add(sampled[i]);
            }

            Mesh pointCloud = new Mesh(hasNormals: true);
            pointCloud.Vertices = sampledVertexList;

            // return 0;

            // Save and open mesh
            // Mesh r = MeshLab.Sample(mesh, 20000);
            pointCloud.Save(@"C:\Users\kchamber.JPL\Desktop\points.ply");
            System.Diagnostics.Process.Start(@"C:\Users\kchamber.JPL\Desktop\points.ply");
            return 0;
            // Parse command line arguments
            // int returnCode = Commands.RunFromCommandline(args);
            // return returnCode;
        }
    }
}

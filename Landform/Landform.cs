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
            Mesh mesh = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop\colors.ply");

            // Sample points on mesh
            Mesh pointCloud = SurfacePointSample.GenerateSampledMesh(mesh, 300);

            // return 0;

            // Save and open mesh
            // Mesh r = MeshLab.Sample(mesh, 20000);
            pointCloud.Save(@"C:\Users\kchamber.JPL\Desktop\points.obj");
            System.Diagnostics.Process.Start(@"C:\Users\kchamber.JPL\Desktop\points.obj");
            return 0;
            // Parse command line arguments
            // int returnCode = Commands.RunFromCommandline(args);
            // return returnCode;
        }
    }
}

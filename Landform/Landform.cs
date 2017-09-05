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
            Mesh a = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/mars_original.obj");
            Mesh b = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/mars_decimated.obj");

            //Mesh a = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/bunny.obj");
            //Mesh b = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/bunny_decimated.obj");

            //Mesh a = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/smooth_suzanne.obj");
            //Mesh b = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/rough_suzanne.obj");

            HausdorffDistance hausdorff = new HausdorffDistance();
            double newHausdorff = hausdorff.Calculate(a, b, 1);
            Console.WriteLine("New: " + newHausdorff);

            double oldHausdorff = MeshLab.BidirectionalHausdorffDistance(a, b).Max;
            Console.WriteLine("Old: " + oldHausdorff);

            return 0;
        }
    }
}

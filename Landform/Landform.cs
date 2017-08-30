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
            Mesh a = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/original.obj");
            Mesh b = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop/decimated.obj");

            HausdorffDistance hausdorff = new HausdorffDistance();

            double newHausdorff = hausdorff.Calculate(a, b);
            Console.WriteLine("New: " + newHausdorff);

            //double oldHausdorff = MeshLab.BidirectionalHausdorffDistance(a, b).Max;
            //Console.WriteLine("Old: " + oldHausdorff);

            return 0;
        }
    }
}

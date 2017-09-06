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
            //Mesh mesh = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop\output\12222030.obj");
            Mesh mesh = Mesh.Load(@"C:\Users\kchamber.JPL\Desktop\output\3000000000333.obj");

            mesh.RemoveSkirt();
            //mesh.AddSkirt(new Vector3(0, -1, 0));

            string path = @"C:\Users\kchamber.JPL\Desktop\output\result.obj";
            mesh.Save(path);
            System.Diagnostics.Process.Start(path);

            return 0;
        }
    }
}

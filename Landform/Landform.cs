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

namespace Landform
{
    class Landform
    {
        /// <summary>
        /// 
        /// The start of everything
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Main(string[] args)
        {
            int returnCode = Commands.RunFromCommandline(args);
            return returnCode;
        }
    }
}

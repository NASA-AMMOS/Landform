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

namespace LandformUtil
{
    class LandformUtil
    {
        static ILog logger = LogManager.GetLogger(typeof(LandformUtil));

        /// <summary>
        /// The start of everything
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Main(string[] args)
        {
            Config.ApplicationConfigFolder = ".landform";

            //these enable Logging.ConfigureLogging() to retrieve Config.FullCommand
            //so that can become part of the log filename log/log-Landform-subcommand-timestamp-pid.txt
            Config.BaseCommand = "LandformUtil";
            if (args.Length > 0)
            {
                Config.SubCommand = args[0];
            }

            //TODO centralize log4net initialization to uniformly handle --quiet and --logfile command line opts
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/308
            Logging.ConfigureLogging();

            // Register filetype handlers
            new DAESerializer().Register();
            new OpenInventorSerializer().Register();
            new DracoSerializer().Register();

            //Configure gdal
            GdalConfiguration.ConfigureGdal();

            // Parse command line arguments
            int returnCode = Commands.RunFromCommandline(args);
            return returnCode;
        }
    }
}

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
using OPS.Pipeline.TileServer;
namespace TilingServer
{
    class TilingServer
    {
        static ILog logger = LogManager.GetLogger(typeof(TilingServer));

        static int Main(string[] args)
        {
            Config.ApplicationConfigFolder = ".landform";

            //these enable Logging.ConfigureLogging() to retrieve Config.FullCommand
            //so that can become part of the log filename log/log-TilingServer-subcommand-timestamp-pid.txt
            Config.BaseCommand = "TilingServer";
            if (args.Length > 0)
            {
                Config.SubCommand = args[0];
            }

            // Parse command line arguments
            int returnCode = TileServerCommands.RunFromCommandline(args);
            return returnCode;
        }
    }
}




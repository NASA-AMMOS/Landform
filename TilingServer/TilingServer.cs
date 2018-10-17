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

            // Enable logging
            if (args.Length > 0)
            {
                log4net.GlobalContext.Properties["command"] = "tilingserver-" + args[0];
            }
            else
            {
                log4net.GlobalContext.Properties["command"] = "tilingserver";
            }
            log4net.Config.XmlConfigurator.Configure();

            // Parse command line arguments
            int returnCode = TileServerCommands.RunFromCommandline(args);
            return returnCode;
        }
    }
}




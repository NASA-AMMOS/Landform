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
using OPS.Imaging;
namespace TilingServer
{
    class TilingServer
    {
        static ILog logger = LogManager.GetLogger(typeof(TilingServer));

        static int Main(string[] args)
        {
            Config.ApplicationConfigFolder = ".landform";
            // Enable logging
            log4net.Config.XmlConfigurator.Configure();
            // Register filetype handlers
            new OpenInventorSerializer().Register();
            new DracoSerializer().Register();

            //Configure gdal
            GdalConfiguration.ConfigureGdal();

            // Parse command line arguments
            int returnCode = TileServerCommands.RunFromCommandline(args);
            return returnCode;
        }
    }
}




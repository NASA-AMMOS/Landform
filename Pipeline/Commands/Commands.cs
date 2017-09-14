using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class Commands
    {
        /// <summary>
        /// Parses command line arguments and executes the appropriate command        
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static int RunFromCommandline(string[] args)
        {
            /// Commands are defined by the list of types passed into ParseArguments
            /// Each passed in object must have a [Verb] decorator
            return CommandLine.Parser.Default.ParseArguments<CralwMSLOptions,
                                                             ConvertBaselineMeshOptions,
                                                             MatchImagesOptions,
                                                             MatchAllImagesOptions,
                                                             PDSImageConverterOptions,
                                                             ConvertBaselineMeshesOptions,
                                                             TileBaselineMeshOptions,
                                                             ClientOptions,
                                                             CheckTilesOptions,
                                                             QueueListenerOptions,
                                                             TileBaselineMeshesOptions,
                                                             BenchmarkS3Options>(args)
              .MapResult(
                (CralwMSLOptions opts) => new CrawlMSL(opts).Run(),
                (ClientOptions opts) =>  new Client(opts).Run(),
                (CheckTilesOptions opts) => new CheckTiles(opts).Run(),
                (QueueListenerOptions opts) => new QueueListener(opts).Run(),
                (ConvertBaselineMeshOptions opts) => new ConvertBaselineMesh(opts).Run(),
                (ConvertBaselineMeshesOptions opts) => new ConvertBaselineMeshes(opts).Run(),
                (TileBaselineMeshOptions opts) => new TileBaselineMesh(opts).Run(),
                (TileBaselineMeshesOptions opts) => new TileBaselineMeshes(opts).Run(),
                (BenchmarkS3Options opts) => new BenchmarkS3(opts).Run(),
                (MatchImagesOptions opts) => new MatchImages(opts).Run(),
                (MatchAllImagesOptions opts) => new MatchAllImages(opts).Run(),
                (PDSImageConverterOptions opts) => new PDSImageConverter(opts).Run(),
                errs => 1);
        }
    }
}

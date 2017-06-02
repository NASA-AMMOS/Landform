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
            return CommandLine.Parser.Default.ParseArguments<CralwMSLOptions, ConvertBaselineMeshOptions, BenchmarkS3Options, MatchImagesOptions>(args)
              .MapResult(
                (CralwMSLOptions opts) => new CrawlMSL(opts).Run(),
                (ConvertBaselineMeshOptions opts) => new ConvertBaselineMesh(opts).Run(),
                (BenchmarkS3Options opts) => new BenchmarkS3(opts).Run(),
                (MatchImagesOptions opts) => new MatchImages(opts).Run(),
                errs => 1);
        }
    }
}

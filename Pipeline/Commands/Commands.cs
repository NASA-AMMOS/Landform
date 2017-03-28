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
            return CommandLine.Parser.Default.ParseArguments<CralwMSLOptions>(args)
              .MapResult(
                (CralwMSLOptions opts) => new CrawlMSL(opts).Crawl(),
                errs => 1);
        }
    }
}

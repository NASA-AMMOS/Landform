using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace OPS.Pipeline.TileServer
{
    public class TileServerCommands
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
            return CommandLine.Parser.Default.ParseArguments<CreateProjectOptions,
                                                             UploadInputOptions
                                                             >(args)
              .MapResult(
                (CreateProjectOptions opts) => new CreateProject(opts).Run(),
                (UploadInputOptions opts) => new UploadInput(opts).Run(),
                errs => 1);
        }
    }
}

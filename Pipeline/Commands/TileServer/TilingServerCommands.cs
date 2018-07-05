using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace OPS.Pipeline
{
    public class TilingServerCommands
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
            return CommandLine.Parser.Default.ParseArguments<TilingServerChunkInputOptions,
                                                             TilingServerWorkflowOptions,
                                                             TilingServerDefineStructureOptions,
                                                             TilingServerBuildLeafTilesOptions
                                                             >(args)
              .MapResult(
                (TilingServerChunkInputOptions opts) => new TilingServerChunkInput(opts).Run(),
                (TilingServerWorkflowOptions opts) => new TilingServerWorkflow(opts).Run(),
                (TilingServerDefineStructureOptions opts) => new TilingServerDefineStructure(opts).Run(),
                (TilingServerBuildLeafTilesOptions opts) => new TilingServerBuildLeafTiles(opts).Run(),
                errs => 1);
        }
    }
}

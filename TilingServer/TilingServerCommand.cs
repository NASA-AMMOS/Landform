using System;
using CommandLine;
using OPS.Pipeline;

namespace OPS.TilingServer
{
    public class TilingServerCommandOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public virtual string ProjectName { get; set; }

        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public virtual bool Local { get; set; }
    }

    public class TilingServerCommand
    {
        protected PipelineCore pipeline;
        protected PipelineExecutive executive;

        protected TilingServerCommand(TilingServerCommandOptions options,
                                      ExecutionMode localExecutionMode = ExecutionMode.None,
                                      bool initTables = true)
            : this(options, options.Local, localExecutionMode)
        {
        }

        protected TilingServerCommand(PipelineCoreOptions options, bool local,
                                      ExecutionMode localExecutionMode = ExecutionMode.None,
                                      bool initTables = true)
        {
            if (local)
            {
                pipeline = new LocalPipeline(options, initAlignmentTables: false, initTilingTables: initTables);
                executive = PipelineExecutive.MakeExecutive(pipeline, localExecutionMode);
            }
            else
            {
                pipeline = new CloudPipeline(options, initAlignmentTables: false, initTilingTables: initTables,
                                             queuePrefix: "tiling");
            }
        }
    }
}

using System;
using CommandLine;
using OPS.Pipeline;

namespace OPS.TilingServer
{
    public class TilingServerCommandOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public bool Local { get; set; }
    }

    public class TilingServerCommand
    {
        protected PipelineCore pipeline;
        protected PipelineExecutive executive;

        protected TilingServerCommand(TilingServerCommandOptions options, ExecutionMode localExecutionMode = ExecutionMode.None)
            : this(options, options.Local, localExecutionMode)
        {
        }

        protected TilingServerCommand(PipelineCoreOptions options, bool local,
                                ExecutionMode localExecutionMode = ExecutionMode.None)
        {
            if (local)
            {
                pipeline = new LocalPipeline(options);
                executive = PipelineExecutive.MakeExecutive(pipeline, localExecutionMode);
            }
            else
            {
                pipeline = new CloudPipeline(options, queuePrefix: "tiling");
            }
        }

        protected TilingServerCommand(PipelineCore pipeline, ExecutionMode localExecutionMode = ExecutionMode.None)
        {
            this.pipeline = pipeline;
            if (pipeline is LocalPipeline && localExecutionMode != ExecutionMode.None)
            {
                if (executive != null)
                {
                    pipeline.LogWarn("Overwriting shared pipeline executive.");
                }
                executive = PipelineExecutive.MakeExecutive(pipeline, localExecutionMode);
            }
        }
    }
}

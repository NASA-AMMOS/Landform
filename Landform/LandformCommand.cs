using System;
using CommandLine;
using OPS.Pipeline;

namespace OPS.Landform
{
    public class LandformCommandOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LandformCommand
    {
        protected PipelineCore pipeline;

        protected LandformCommand(LandformCommandOptions options)
        {
            if (options.Cloud)
            {
                pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                pipeline = new LocalPipeline(options);
            }
        }
    }
}

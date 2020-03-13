using System;
using log4net;
using CommandLine;
using OPS.Pipeline;

namespace OPS.TilingServer
{
    [Verb("deletequeues", HelpText = "Delete queues")]
    public class DeleteQueuesOptions : TilingServerCommandOptions
    {       
        [Option(Default = false, HelpText = "Disable confirmation prompt")]
        public bool Force { get; set; }

        [Value(0, Required = false, HelpText = "Option disabled for this command", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Default = false, HelpText = "Option disabled for this command")]
        public override bool Local { get; set; }
    }

    public class DeleteQueues : TilingServerCommand
    {
        private DeleteQueuesOptions options;

        public DeleteQueues(DeleteQueuesOptions options) : base(options, local: false, initTables: false)
        {
            this.options = options;
        }

        public int Run()
        {
            if (!options.Force)
            {
                Console.WriteLine("delete queues in venue " + pipeline.Venue + " (yes/no)?");
                var response = Console.ReadLine();
                if (response.ToLower() != "yes") return 1;
            }
            pipeline.LogInfo("deleting queues in venue {0}", pipeline.Venue);
            (pipeline as CloudPipeline).DeleteQueues();
            return 0;
        }
    }
}

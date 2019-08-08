using CommandLine;
using log4net;
using System;

namespace OPS.Pipeline.TileServer
{
    [Verb("deletequeues", HelpText = "Delete queues")]
    public class DeleteQueuesOptions : PipelineCoreOptions
    {       
        [Option(Default = false, HelpText = "Disable confirmation prompt")]
        public bool Force { get; set; }
    }

    public class DeleteQueues : CloudPipeline
    {
        private DeleteQueuesOptions options;

        public DeleteQueues(DeleteQueuesOptions options) : base(options, queuePrefix: "tiling")
        {
            this.options = options;
        }

        public int Run()
        {
            if (!options.Force)
            {
                Console.WriteLine("delete queues in venue " + Venue + " (yes/no)?");
                var response = Console.ReadLine();
                if (response.ToLower() != "yes") return 1;
            }
            LogInfo("deleting queues");
            DeleteQueues();
            return 0;
        }
    }
}

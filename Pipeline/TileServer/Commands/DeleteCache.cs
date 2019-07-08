using CommandLine;
using log4net;
using System;

namespace OPS.Pipeline.TileServer
{
    [Verb("deletecache", HelpText = "Delete cache")]
    public class DeleteCacheOptions : PipelineCoreOptions
    {       
        [Option(Default = false, HelpText = "Disable confirmation prompt")]
        public bool Force { get; set; }

        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public bool Local { get; set; }
    }

    public class DeleteCache
    {
        private DeleteCacheOptions options;
        private PipelineCore pipeline;

        public DeleteCache(DeleteCacheOptions options)
        {
            this.options = options;
            pipeline = TileServerCommands.MakePipeline(options, options.Local);
        }

        public int Run()
        {
            if (!options.Force)
            {
                Console.WriteLine("delete download caches " + pipeline.DownloadCache + " (yes/no)?");
                var response = Console.ReadLine();
                if (response.ToLower() != "yes") return 1;
            }
            pipeline.LogInfo("deleting download cache: " + pipeline.DownloadCache);
            pipeline.DeleteDownloadCache();
            return 0;
        }
    }
}

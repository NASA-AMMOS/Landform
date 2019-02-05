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
    }

    public class DeleteCache : CloudPipeline
    {
        private DeleteCacheOptions options;

        public DeleteCache(DeleteCacheOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            if (!options.Force)
            {
                Console.WriteLine("delete download caches " + DownloadCache + " (yes/no)?");
                var response = Console.ReadLine();
                if (response.ToLower() != "yes") return 1;
            }
            LogInfo("deleting download cache: " + DownloadCache);
            DeleteDownloadCache();
            return 0;
        }
    }
}

using CommandLine;
using log4net;
using OPS.Plumbing;
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

        public DeleteCache(DeleteCacheOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
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
            Logger.Info("deleting download cache: " + DownloadCache);
            DeleteDownloadCache();
            return 0;
        }
    }
}

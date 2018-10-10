using CommandLine;
using log4net;
using OPS.Plumbing;
using System;

namespace OPS.Pipeline.TileServer
{
    [Verb("deletecache", HelpText = "Delete cache")]
    public class DeleteCacheOptions
    {       
        [Option(HelpText = "Disable confirmation prompt", Default = false)]
        public bool Force { get; set; }
    }

    public class DeleteCache : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(DeleteCache));

        DeleteCacheOptions options;

        public DeleteCache(DeleteCacheOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
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
            logger.Info("deleting download cache: " + DownloadCache);
            DeleteDownloadCache();
            return 0;
        }
    }
}

using System;
using CommandLine;
using log4net;
using OPS.Pipeline;

namespace OPS.TilingServer
{
    [Verb("deletecache", HelpText = "Delete cache")]
    public class DeleteCacheOptions : PipelineCoreOptions
    {       
        [Option(Default = false, HelpText = "Disable confirmation prompt")]
        public bool Force { get; set; }

        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public bool Local { get; set; }
    }

    public class DeleteCache : TilingServerCommand
    {
        private DeleteCacheOptions options;

        public DeleteCache(DeleteCacheOptions options) : base(options, options.Local)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (!options.Force)
                {
                    Console.WriteLine("delete download caches " + pipeline.DownloadCache + " (yes/no)?");
                    var response = Console.ReadLine();
                    if (response.ToLower() != "yes") return 1;
                }
                pipeline.LogInfo("deleting download cache: " + pipeline.DownloadCache);
                pipeline.DeleteDownloadCache();
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }
            return 0;
        }
    }
}

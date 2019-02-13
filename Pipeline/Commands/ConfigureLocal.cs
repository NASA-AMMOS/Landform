using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;
using log4net;
using OPS.Util;

namespace OPS.Pipeline
{
    [Verb("configure-local", HelpText = "Configures Landform local")]
    public class ConfigureLocalOptions : PipelineCoreOptions
    {
        [Option(Default = null, HelpText = "Venue name")]
        public string Venue { get; set; }
        
        [Option(Default = null, HelpText = "Storage directory")]
        public string StorageDir { get; set; }
    }

    public class ConfigureLocal
    {
        private ConfigureLocalOptions options;
        private static ILog logger = LogManager.GetLogger(typeof(ConfigureLocal));

        public ConfigureLocal(ConfigureLocalOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            LocalPipelineConfig config = new LocalPipelineConfig();

            if (string.IsNullOrEmpty(config.Venue))
            {
                config.Venue = "local"; //default unless overridden by command line option or console input
            }

            config.Venue = ConsoleHelper.Prompt("Venue name", options.Venue, config.Venue);
            config.StorageDir = ConsoleHelper.Prompt("Storage directory", options.StorageDir, config.StorageDir);

            config.Validate();

            var cfgPath = config.ConfigFilepath();
            logger.Info("persisting config to " + cfgPath);
            config.Save();

            return 0;
        }
    }
}

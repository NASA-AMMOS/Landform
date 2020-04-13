using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;

/// <summary>
/// Utility to write ~/.landform/landform-local.json
///
/// Can be run interactively or in batch mode specifying settings by command line options.
///
/// Example command line, batch mode:
///
/// Landform.exe configure-local --venue=landform-local --storagedir=c:/Users/$USERNAME/Documents/landform-storage
/// </summary>
namespace OPS.Landform
{
    [Verb("configure-local", HelpText = "Configures Landform local")]
    public class ConfigureLocalOptions : ConfigureBaseOptions
    {
        //NOTE: any non-null default values for options will short circuit the Prompt() functionality
        //because it can't differentiate an option that got its value as a default
        //vs an option that was explicitly specified on the command line
        //instead put defaults in {Local,Cloud}PipelineConfig
        
        [Option(Default = null, HelpText = "Storage directory")]
        public string StorageDir { get; set; }
    }

    public class ConfigureLocal
    {
        private ConfigureLocalOptions options;

        private ILog logger = LogManager.GetLogger(typeof(ConfigureLocal));

        public ConfigureLocal(ConfigureLocalOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            LocalPipelineConfig config = new LocalPipelineConfig();

            config.Venue = ConsoleHelper.Prompt("venue", options.Venue, config.Venue, options.Interactive);
            config.StorageDir = ConsoleHelper.Prompt("storage directory", options.StorageDir, config.StorageDir,
                                                     options.Interactive);
            config.MaxCores = ConsoleHelper.Prompt("max cores, 0 = all available, N = up to N, -M = reserve M",
                                                   options.MaxCores, config.MaxCores, options.Interactive);
            config.RandomSeed = ConsoleHelper.Prompt("negative to use a time dependent random seed",
                                                     options.RandomSeed, config.RandomSeed, options.Interactive);

            config.Validate();

            var cfgPath = config.ConfigFilePath();
            logger.Info("persisting config to " + cfgPath);
            config.Save();

            return 0;
        }
    }
}

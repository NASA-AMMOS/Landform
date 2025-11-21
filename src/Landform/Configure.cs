using System;
using CommandLine;
using log4net;
using JPLOPS.Util;
using JPLOPS.Pipeline;

/// <summary>
/// Utility to write ~/.landform/landform-local.json
///
/// Can be run interactively or in batch mode specifying settings by command line options.
///
/// Example command line, batch mode:
///
/// Landform.exe configure --venue=landform-local --storagedir=c:/Users/$USERNAME/Documents/landform-storage
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("configure", HelpText = "Configures Landform")]
    public class ConfigureOptions : CommandHelper.BaseOptions
    {
        //NOTE: any non-null default values for options will short circuit the Prompt() functionality
        //because it can't differentiate an option that got its value as a default
        //vs an option that was explicitly specified on the command line
        //instead put defaults in LocalPipelineConfig
        
        [Option(Default = false, HelpText = "Prompt interactively instead of using defaults")]
        public bool Interactive { get; set; }

        [Option(Default = null, HelpText = "Venue name")]
        public string Venue { get; set; }

        [Option(Default = null, HelpText = "Override user mask directory (for compatibility)")]
        public string UserMasksDirectory { get; set; }

        [Option(Default = null, HelpText = "Storage directory")]
        public string StorageDir { get; set; }
    }

    public class Configure
    {
        private ConfigureOptions options;

        private ILog logger = LogManager.GetLogger(typeof(Configure));

        public Configure(ConfigureOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                var config = new LocalPipelineConfig();
                
                config.Venue = ConsoleHelper.Prompt("venue", options.Venue, config.Venue, options.Interactive);
                config.StorageDir = ConsoleHelper.Prompt("storage directory", options.StorageDir, config.StorageDir,
                                                         options.Interactive);
                config.MaxCores = options.MaxCores;
                config.RandomSeed = options.RandomSeed;
                
                config.Validate();
                
                var cfgPath = config.ConfigFilePath();
                logger.Info("persisting config to " + cfgPath);
                config.Save();
            }
            catch (Exception ex)
            {
                Logging.LogException(logger, ex);
                return 1;
            }

            return 0;
        }
    }
}

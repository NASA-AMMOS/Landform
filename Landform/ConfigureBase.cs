using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;

namespace OPS.Landform
{
    public class ConfigureBaseOptions : CommandHelper.OptionsBase
    {
        //null defaults force interactive prompt
        
        [Option(Default = null, HelpText = "Venue name")]
        public string Venue { get; set; }
        
        [Option(Default = null, HelpText = "0 to use all available cores, N to use up to N, -M to reserve M")]
        public string MaxCores { get; set; }

        [Option(Default = null, HelpText = "negative to use a time-dependent random seed")]
        public string RandomSeed { get; set; }

        [Option(Default = null, HelpText = "Override default config dir (defaults to user home dir)")]
        public string ConfigDir { get; set; }

        [Option(Default = null, HelpText = "Override default config folder (defaults to .landform)")]
        public string ConfigFolder { get; set; }

        [Option(Default = false, HelpText = "Suppress non-essential output")]
        public bool Quiet { get; set; }

        [Option(Default = false, HelpText = "Log debug info")]
        public bool Debug { get; set; }

        [Option(Default = null, HelpText = "Override default log filename")]
        public string LogFile { get; set; }

        [Option(Default = null, HelpText = "Override default log directory")]
        public string LogDir { get; set; }

        [Option(Default = null, HelpText = "Override default temp dir")]
        public string TempDir { get; set; }
    }

    public class ConfigureBase
    {
        private ConfigureBaseOptions cbopts;
            
        protected ILog logger;

        public ConfigureBase(ConfigureBaseOptions cbopts)
        {
            this.cbopts = cbopts;

            //this was already called in Landform.cs with args and baseCommand
            //calling again now that we have parsed command line options
            CommandHelper.Configure(args: null, baseCommand: null,
                                    configDir: cbopts.ConfigDir, configFolder: cbopts.ConfigFolder, 
                                    quiet: cbopts.Quiet, debug: cbopts.Debug, logFilename: cbopts.LogFile,
                                    logDir: cbopts.LogDir, tempDir: cbopts.TempDir);

            logger = LogManager.GetLogger(GetType());

            if (!cbopts.Quiet)
            {
                CommandHelper.DumpConfig(logger);
            }
        }
    }
}

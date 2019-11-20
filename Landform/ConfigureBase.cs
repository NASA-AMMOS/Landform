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
    }

    public class ConfigureBase
    {
        private ConfigureBaseOptions cbopts;
            
        protected ILog logger;

        public ConfigureBase(ConfigureBaseOptions cbopts)
        {
            this.cbopts = cbopts;
            if (!string.IsNullOrEmpty(cbopts.ConfigDir))
            {
                Config.ConfigDir = cbopts.ConfigDir;
            }
            if (!string.IsNullOrEmpty(cbopts.ConfigFolder)) 
            {
                Config.ConfigFolder = cbopts.ConfigFolder;
            }
            Logging.ConfigureLogging(cbopts.Quiet, cbopts.Debug, cbopts.LogFile);
            logger = LogManager.GetLogger(GetType());
        }
    }
}

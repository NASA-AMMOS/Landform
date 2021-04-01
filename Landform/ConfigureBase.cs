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
    public class ConfigureBaseOptions : CommandHelper.BaseOptions
    {
        //NOTE: any non-null default values for options will short circuit the Prompt() functionality
        //because it can't differentiate an option that got its value as a default
        //vs an option that was explicitly specified on the command line
        //instead put defaults in {Local,Cloud}PipelineConfig
        
        [Option(Default = false, HelpText = "Prompt interactively instead of using defaults")]
        public bool Interactive { get; set; }

        [Option(Default = null, HelpText = "Venue name")]
        public string Venue { get; set; }
    }
}

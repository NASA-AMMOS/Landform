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
        //null defaults force interactive prompt
        
        [Option(Default = null, HelpText = "Venue name")]
        public string Venue { get; set; }
        
        [Option(Default = null, HelpText = "0 to use all available cores, N to use up to N, -M to reserve M")]
        public string MaxCores { get; set; }

        [Option(Default = null, HelpText = "negative to use a time-dependent random seed")]
        public string RandomSeed { get; set; }
    }
}

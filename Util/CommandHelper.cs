using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandLine;
using log4net;

namespace OPS.Util
{
    public class CommandHelper
    {
        [Verb("base-options")]
        public class BaseOptions
        {
            [Option(Default = null, HelpText = "Override command line options from JSON file")]
            public string OptionsFile { get; set; }

            [Option(Default = null, HelpText = "Override default config dir (defaults to user home dir)")]
            public string ConfigDir { get; set; }
            
            [Option(Default = null, HelpText = "Override default config folder (defaults to .landform)")]
            public string ConfigFolder { get; set; }
            
            [Option(Default = null, HelpText = "Override default log filename")]
            public string LogFile { get; set; }
            
            [Option(Default = null, HelpText = "Override default log directory")]
            public string LogDir { get; set; }
            
            [Option(Default = null, HelpText = "Override default temp dir")]
            public string TempDir { get; set; }

            [Option(Default = false, HelpText = "Suppress non-essential output")]
            public bool Quiet { get; set; }
            
            [Option(Default = false, HelpText = "Log verbose info")]
            public bool Verbose { get; set; }
            
            [Option(Default = false, HelpText = "Log debug info")]
            public bool Debug { get; set; }

            [Option(Default = false, HelpText = "Disable parallism, e.g. for debugging")]
            public bool SingleThreaded { get; set; }
        
            [Option(Default = null, HelpText = "0 to use all available cores, N to use up to N, -M to reserve M")]
            public int? MaxCores { get; set; }

            [Option(Default = null, HelpText = "negative to use a time-dependent random seed")]
            public int? RandomSeed { get; set; }
        }

        public static bool HasFlag(string[] args, string flag)
        {
            return args.Any(arg => arg.StartsWith("-") && arg.ToLower().TrimStart('-') == flag);
        }

        /// <summary>
        /// Early parse of standard command line arguments to set up Config and Logging.
        /// </summary>
        public static bool Configure(string[] args, Type appType = null, Type pipelineType = null,
                                     Func<string> appConfigFile = null)
        {
            Config.CommandLineArgs = args;

            if (appType != null)
            {
                Config.BaseCommand = appType.Name;
                Config.AppVersion = appType.Assembly.GetName().Version.ToString();
            }

            if (pipelineType != null)
            {
                Config.PipelineVersion = pipelineType.Assembly.GetName().Version.ToString();
            }

            var opts = new BaseOptions();
            if (args.Length > 0)
            {
                Config.SubCommand = args[0];

                try
                {
                    var optsArgs = (string[])args.Clone();
                    optsArgs[0] = "base-options";
                    opts = (BaseOptions)ParseCommandLineOpts(optsArgs, new Type[] { typeof(BaseOptions) },
                                                             allowUnknown: true);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("error parsing command line options: {0}", ex.Message);
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(opts.ConfigDir))
            {
                Config.ConfigDir = opts.ConfigDir;
            }

            if (!string.IsNullOrEmpty(opts.ConfigFolder))
            {
                Config.ConfigFolder = opts.ConfigFolder;
            }

            if (!string.IsNullOrEmpty(opts.TempDir))
            {
                TemporaryFile.TemporaryDirectory = opts.TempDir;
            }

            Logging.ConfigureLogging(Config.FullCommand, opts.Quiet, opts.Debug, opts.LogFile, opts.LogDir);

            if (!opts.Quiet)
            {
                var logger = !string.IsNullOrEmpty(Config.SubCommand) ? LogManager.GetLogger(Config.SubCommand)
                    : appType != null ? LogManager.GetLogger(appType)
                    : pipelineType != null ? LogManager.GetLogger(pipelineType)
                    : LogManager.GetLogger("Landform");
                string appVersion = Config.AppVersion ?? "(unknown)";
                string pipelineVersion = Config.PipelineVersion ?? "(unknown)";
                logger.InfoFormat("command: {0} {1}", PathHelper.GetExe(), string.Join(" ", args));
                logger.InfoFormat("{0} {1}{2}", Config.BaseCommand ?? "Landform", appVersion,
                                  appVersion != pipelineVersion ? (", " + pipelineVersion) : "");
                logger.InfoFormat("temp dir: {0}", StringHelper.NormalizeSlashes(TemporaryFile.TemporaryDirectory));
                logger.InfoFormat("log file: {0}", StringHelper.NormalizeSlashes(Logging.GetLogFile()));

                //get the app config instance to ask its file path now
                //after Config.ConfigDir and Config.ConfigFolder are initialized
                string cfgFile = appConfigFile != null ? appConfigFile() : null;
                if (cfgFile != null)
                {
                    logger.InfoFormat("config file: {0}", StringHelper.NormalizeSlashes(cfgFile));
                }
            }

            CoreLimitedParallel.SetMaxCores(opts.SingleThreaded ? 1 : (opts.MaxCores ?? 0));

            NumberHelper.RandomSeed = opts.RandomSeed ?? -1;

            return true;
        }

        public static void DumpConfig(ILog logger)
        {
            logger.InfoFormat("Architecture: {0}", (IntPtr.Size == 4 ? "x86" : "x64"));
            logger.InfoFormat("using {0} of {1} CPU cores",
                              CoreLimitedParallel.GetMaxCores(), CoreLimitedParallel.GetAvailableCores());
        }

        public static object ParseCommandLineOpts(string[] args, IEnumerable<Type> optsTypes, bool allowUnknown = false)
        {
            Func<string, string, bool> startsWith = (s, p) => s.StartsWith(p, true, null);
            Func<string, string, bool> isArg = (a, n) => startsWith(a, "--" + n) || startsWith(a, "-" + n);
            int optsIndex = args.ToList().FindIndex(arg => isArg(arg, "optionsfile"));
            if (optsIndex > 0)
            {
                string optsFile = null;
                string optsArg = args[optsIndex];
                int sep = optsArg.IndexOf("=");
                if (sep > 0 && optsArg.Length > sep + 1)
                {
                    if (args.Length > 2)
                    {
                        throw new Exception("cannot combine --optionsfile with other arguments");
                    }
                    optsFile = optsArg.Substring(sep + 1);
                }
                else if (args.Length > optsIndex + 1 && !args[optsIndex + 1].StartsWith("-"))
                {
                    if (args.Length > 3)
                    {
                        throw new Exception("cannot combine --optionsfile with other arguments");
                    }
                    optsFile = args[optsIndex + 1];
                }
                else
                {
                    throw new Exception("failed to parse --optionsfile");
                }

                if (!File.Exists(optsFile))
                {
                    throw new Exception(string.Format("options file {0} not found", optsFile));
                }

                string verbName = args[0].ToLower();
                Type optsType = optsTypes
                    .Where(t => t.GetCustomAttribute<VerbAttribute>().Name.ToLower() == verbName)
                    .FirstOrDefault();
                if (optsType == null)
                {
                    throw new Exception(string.Format("unknown subcommand {0}", verbName));
                }

                Console.WriteLine("reading {0} options from options file {1}", verbName, optsFile);
                var dict = JsonHelper.FromJson<Dictionary<string, object>>(File.ReadAllText(optsFile));
                object opts = optsType.GetConstructor(new Type[] {}).Invoke(new object[] {});

                foreach (var prop in optsType.GetProperties().Where(p => p.CanWrite))
                {
                    var attr = prop.GetCustomAttribute<BaseAttribute>();
                    if (attr != null)
                    {
                        if (attr.Required && !dict.ContainsKey(prop.Name))
                        {
                            throw new Exception(string.Format("required option {0} not in options file {1}",
                                                              prop.Name, optsFile));
                        }
                        prop.SetValue(opts, attr.Default);
                    }
                    if (dict.ContainsKey(prop.Name))
                    {
                        object val = dict[prop.Name];
                        if (prop.PropertyType.IsEnum)
                        {
                            val = Enum.Parse(prop.PropertyType, (string)val);
                        }
                        prop.SetValue(opts, val);
                    }
                }
                return opts;
            }
            else
            {
                if (allowUnknown)
                {
                    //work around https://github.com/commandlineparser/commandline/issues/525
                    args = FilterOutUnknownArgs(args, optsTypes);
                    allowUnknown = false;
                }
                var parser = new Parser((ParserSettings settings) => 
                        {
                            settings.HelpWriter = Console.Error;
                            settings.IgnoreUnknownArguments = allowUnknown;
                        });
                var res = parser.ParseArguments(args, optsTypes.ToArray());
                if (res is Parsed<object>)
                {
                    return ((Parsed<object>)res).Value;
                }
                //filter like CommandLine.ErrorExtensions.OnlyMeaningfulOnes() (but that's not public)
                var errors = ((NotParsed<object>)res).Errors
                    .Where(e => !e.StopsProcessing)
                    .Where(e => !(e.Tag == ErrorType.UnknownOptionError
                                  && ((UnknownOptionError)e).Token.ToLower() == "help"));
                if (errors.Count() > 0)
                {
                    throw new Exception("failed to parse command line options");
                }
                return null; //e.g. --help or --version
            }
        }

        public static string[] FilterOutUnknownArgs(string[] args, IEnumerable<Type> optsTypes)
        {
            if (args == null || args.Length < 2)
            {
                return args;
            }

            string verbName = args[0].ToLower();
            Type optsType = optsTypes
                .Where(t => t.GetCustomAttribute<VerbAttribute>().Name.ToLower() == verbName)
                .FirstOrDefault();
            if (optsType == null)
            {
                return args;
            }

            var knownArgs = new HashSet<string>();
            foreach (var prop in optsType.GetProperties().Where(p => p.CanWrite))
            {
                var attr = prop.GetCustomAttribute<BaseAttribute>();
                if (attr != null)
                {
                    knownArgs.Add(prop.Name.ToLower());
                }
            }

            Func<string, bool> isKnown =
                arg => arg.StartsWith("-") && knownArgs.Contains(arg.TrimStart('-').Split('=')[0].ToLower());

            var filtered = new List<string>();
            filtered.Add(args[0]);
            for (int i = 1; i < args.Length; i++)
            {
                if (isKnown(args[i]) ||
                    (i > 1 && !args[i].StartsWith("-") && isKnown(args[i - 1]) && !args[i - 1].Contains("=")))
                {
                    filtered.Add(args[i]);
                }
            }

            return filtered.ToArray();
        }

        public static int RunFromCommandline(string[] args, IDictionary<Type, Type> verbs)
        {
            object opts = null;
            try
            {
                opts = ParseCommandLineOpts(args, verbs.Keys);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error parsing command line options: {0}", ex.Message);
                return 1;
            }
            if (opts == null)
            {
                return 0;
            }
            Type optsType = opts.GetType();
            Type verbType = verbs[optsType];
            object verb = verbType.GetConstructor(new Type[] { optsType }).Invoke(new object[] { opts });
            return (int)verbType.GetMethod("Run").Invoke(verb, new object[] {});
        }
    }
}

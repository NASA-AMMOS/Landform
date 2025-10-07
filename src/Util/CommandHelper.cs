using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandLine;
using log4net;

namespace JPLOPS.Util
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
    public class EnvVar : System.Attribute
    {
        public readonly string Name;

        public EnvVar(string name)
        {
            this.Name = name;
        }
    }

    public class CommandHelper
    {
        public const string ENV_PREFIX = "LANDFORM";

        [Verb("base-options")]
        public class BaseOptions
        {
            [Option(Default = null, HelpText = "Override command line options from JSON file")]
            public string OptionsFile { get; set; }

            [Option(Default = null, HelpText = "Override default config dir (defaults to user home dir)")]
            public string ConfigDir { get; set; }
            
            [Option(Default = null, HelpText = "Override default config folder (defaults to .landform)")]
            public string ConfigFolder { get; set; }
            
            [Option(Default = null, HelpText = "Override default log filename, or <orig>:<new> to override part(s) of it, must not contain directory")]
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
        
            [Option(Default = 0, HelpText = "0 to use all available cores, N to use up to N, -M to reserve M.  This will be overridden by config file value for commands that load pipeline config.")]
            public int MaxCores { get; set; }

            [Option(Default = -1, HelpText = "negative to use a time-dependent random seed.  This will be overridden by config file value for commands that load pipeline config.")]
            public int RandomSeed { get; set; }
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

            CoreLimitedParallel.SetMaxCores(opts.SingleThreaded ? 1 : opts.MaxCores);
            NumberHelper.RandomSeed = opts.RandomSeed;

            //get the app config instance to ask its file path now
            //after Config.ConfigDir and Config.ConfigFolder are initialized
            string cfgFile = appConfigFile != null ? appConfigFile() : null; //latent spew, possible side effects

            if (!opts.Quiet)
            {
                var logger = !string.IsNullOrEmpty(Config.SubCommand) ? LogManager.GetLogger(Config.SubCommand)
                    : appType != null ? LogManager.GetLogger(appType)
                    : pipelineType != null ? LogManager.GetLogger(pipelineType)
                    : LogManager.GetLogger("Landform");
                string appVersion = Config.AppVersion ?? "(unknown)";
                logger.InfoFormat("process ID {0}, command: {1} {2}",
                                  ConsoleHelper.GetPID(), PathHelper.GetExe(), string.Join(" ", args));
                logger.InfoFormat("{0} {1}", Config.BaseCommand ?? "Landform", appVersion);
                Config.SetLogger(new ThunkLogger(info: msg => logger.Info(msg))); //flush latent spew
                logger.InfoFormat("temp dir: {0}", StringHelper.NormalizeSlashes(TemporaryFile.TemporaryDirectory));
                logger.InfoFormat("log file: {0}", StringHelper.NormalizeSlashes(Logging.GetLogFile()));
                logger.InfoFormat("max cores: {0}, random seed: {1}",
                                  CoreLimitedParallel.GetMaxCores(), NumberHelper.RandomSeed);

                if (cfgFile != null)
                {
                    logger.InfoFormat("config file: {0}", StringHelper.NormalizeSlashes(cfgFile));
                }
            }

            return true;
        }

        public static void DumpConfig(ILog logger)
        {
            logger.InfoFormat("Architecture: {0}", (IntPtr.Size == 4 ? "x86" : "x64"));
            logger.InfoFormat("using {0} of {1} CPU cores",
                              CoreLimitedParallel.GetMaxCores(), CoreLimitedParallel.GetAvailableCores());
        }

        public static string GetOptName(PropertyInfo prop, BaseAttribute ba)
        {
            string opt = prop.Name;
            var oa = ba as OptionAttribute;
            if (oa != null && !string.IsNullOrEmpty(oa.LongName))
            {
                opt = oa.LongName;
            }
            return opt.ToLower();
        }

        public static Dictionary<string, object> GetArgsFromEnvironment(Type optsType)
        {
            var envArgs = new Dictionary<string, object>();
            var va = optsType.GetCustomAttribute<VerbAttribute>();
            if (va == null)
            {
                return envArgs; //shouldn't happen because only get here if there was a VerbAttribute, but ok
            }
            var ea = optsType.GetCustomAttribute<EnvVar>();
            string vn = ea != null ? ea.Name : StringHelper.SnakeCase(va.Name);
            foreach (var prop in optsType.GetProperties().Where(p => p.CanWrite))
            {
                var ba = prop.GetCustomAttribute<BaseAttribute>();
                if (ba != null)
                {
                    string opt = GetOptName(prop, ba);
                    var ea2 = prop.GetCustomAttribute<EnvVar>();
                    string pn = ea2 != null ? ea2.Name : StringHelper.SnakeCase(prop.Name);
                    string en = (ENV_PREFIX + "_" + vn + "_" + pn).ToUpper();
                    string str = Environment.GetEnvironmentVariable(en);
                    if (string.IsNullOrEmpty(str))
                    {
                        if (str != null)
                        {
                            Config.Log($"ignoring empty environment variable {en}");
                        }
                        //if e.g. LANDFORM_CONTEXTUAL_MISSION was not set try LANDFORM_MISSION
                        en = (ENV_PREFIX + "_" + pn).ToUpper();
                        str = Environment.GetEnvironmentVariable(en);
                    }
                    if (!string.IsNullOrEmpty(str))
                    {
                        try
                        {
                            Config.ParseEnvVal(str, prop.PropertyType, obj =>
                            {
                                if (va.Name.Contains("PASSWORD"))
                                {
                                    str = "******";
                                }
                                //this doesn't work because each call to GetCustomAttribute() returns a new instance
                                //ba.Default = obj;
                                Config.Log($"using {en}={str} to default {va.Name} --{opt} (may be overridden)");
                                envArgs.Add(opt, obj);
                            }, parseNonemptyAsTrue: true);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception(
                                string.Format("error setting {0} --{1} from env var {2}={3}: {4}",
                                              prop.PropertyType.Name, opt, en, str, ex.Message));
                        }
                    }
                    else if (str != null)
                    {
                        Config.Log($"ignoring empty environment variable {en}");
                    }
                }
            }
            return envArgs;
        }

        public static object ParseCommandLineOpts(string[] args, IEnumerable<Type> optsTypes, bool allowUnknown = false)
        {
            Type optsType = null;
            string verbName = null;
            Dictionary<string, object> envArgs = null;
            if (args != null && args.Length > 0 && args[0] != null && !args[0].StartsWith("--"))
            {
                verbName = args[0].ToLower();
                optsType = optsTypes
                    .Where(t => t.GetCustomAttribute<VerbAttribute>().Name.ToLower() == verbName)
                    .FirstOrDefault();
                if (optsType == null)
                {
                    throw new Exception(string.Format("unknown subcommand {0}", verbName));
                }
                if (verbName != "base-options")
                {
                    Config.Log("running subcommand: " + verbName);
                }
                envArgs = GetArgsFromEnvironment(optsType);
            }
                
            Func<string, string, bool> startsWith = (s, p) => s.StartsWith(p, true, null);
            Func<string, string, bool> isArg = (a, n) => startsWith(a, "--" + n) || startsWith(a, "-" + n);
            int optsIndex = args.ToList().FindIndex(arg => isArg(arg, "optionsfile"));
            if (optsIndex > 0 && optsType != null)
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

                Console.WriteLine("reading {0} options from options file {1}", verbName, optsFile);
                var dict = JsonHelper.FromJson<Dictionary<string, object>>(File.ReadAllText(optsFile));
                object opts = optsType.GetConstructor(new Type[] {}).Invoke(new object[] {});

                foreach (var prop in optsType.GetProperties().Where(p => p.CanWrite))
                {
                    var ba = prop.GetCustomAttribute<BaseAttribute>();
                    if (ba != null)
                    {
                        string opt = GetOptName(prop, ba);
                        if (ba.Required && !dict.ContainsKey(opt))
                        {
                            throw new Exception(string.Format("required option {0} not in file {1}", opt, optsFile));
                        }
                        prop.SetValue(opts, ba.Default);
                        if (dict.ContainsKey(opt))
                        {
                            object val = dict[opt];
                            if (prop.PropertyType.IsEnum)
                            {
                                val = Enum.Parse(prop.PropertyType, (string)val);
                            }
                            prop.SetValue(opts, val);
                        }
                        else if (envArgs.ContainsKey(opt))
                        {
                            prop.SetValue(opts, envArgs[opt]);
                        }

                    }
                }
                return opts;
            }
            else
            {
                if (allowUnknown)
                {
                    //work around https://github.com/commandlineparser/commandline/issues/525
                    args = FilterOutUnknownArgs(args, optsType);
                    allowUnknown = false;
                }
                var parser = new Parser((ParserSettings settings) => 
                {
                    settings.HelpWriter = Console.Error;
                    settings.IgnoreUnknownArguments = allowUnknown;
                });
                if (envArgs != null)
                {
                    var fullArgs = new List<string>();
                    var explicitOpts = new HashSet<string>();
                    for (int i = 0; i < args.Length; i++)
                    {
                        fullArgs.Add(args[i]);
                        if (args[i].StartsWith("--"))
                        {
                            explicitOpts.Add(args[i]);
                        }
                    }
                    foreach (var entry in envArgs)
                    {
                        string opt = "--" + entry.Key;
                        if (!explicitOpts.Contains(opt))
                        {
                            if (entry.Value is bool)
                            {
                                if ((bool)(entry.Value))
                                {
                                    fullArgs.Add(opt);
                                }
                            }
                            else
                            {
                                fullArgs.Add(opt);
                                fullArgs.Add(entry.Value.ToString());
                            }
                        }
                    }
                    args = fullArgs.ToArray();
                }
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

        public static string[] FilterOutUnknownArgs(string[] args, Type optsType)
        {
            if (args == null || args.Length < 2 || optsType == null)
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

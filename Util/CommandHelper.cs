using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandLine;

namespace OPS.Util
{
    public class CommandHelper
    {
        public class OptionsBase
        {
            [Option(Default = null, HelpText = "Override command line options from JSON file")]
            public string OptionsFile { get; set; }
        }

        public static void Init(string[] args, string baseCommand, string configFolder = Config.DEF_CONFIG_FOLDER)
        {
            //these enable Logging.ConfigureLogging() to retrieve Config.FullCommand
            //so that can become part of the log filename log/log-Landform-subcommand-timestamp-pid.txt
            Config.BaseCommand = baseCommand;
            if (args.Length > 0)
            {
                Config.SubCommand = args[0];
            }

            Config.ConfigFolder = configFolder;

            Logging.ConfigureLogging();
        }

        public static object ParseCommandLineOpts(string[] args, IEnumerable<Type> optsTypes)
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
                    BaseAttribute attr = prop.GetCustomAttribute<BaseAttribute>();
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
                var res = CommandLine.Parser.Default.ParseArguments(args, optsTypes.ToArray());
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

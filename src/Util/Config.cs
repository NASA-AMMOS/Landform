using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

namespace JPLOPS.Util
{
    /// <summary>
    /// Use this attribute on properties in subclasses of Config to indicate
    /// that they can be read from environmental variables
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ConfigEnvironmentVariable : System.Attribute
    {
        public readonly string EnvironmentalVariableName;

        public ConfigEnvironmentVariable(string environmentalVariableName)
        {
            this.EnvironmentalVariableName = environmentalVariableName;
        }
    }

    public interface ConfigDefaultsProvider
    {
        /// <summary>
        /// Get JSON defaults for given config file.
        /// Returned JSON overrides default literals in code and may be partial.
        /// These defaults are in turn overridden by any actual json config file, which may also be partial.
        /// And those values are in turn overridden by any corresponding environment variables.
        /// Returns null if no defaults available for the given config filename.
        /// Ignores extension of configFilename, if any.
        /// </summary>
        string GetConfigDefaults(string configFilename);
    }

    /// <summary>
    /// Class for specifying application configuration 
    /// Extend this class with public properties.  Properties can be read from files json in a folder
    /// under the users home directory or optionaly EnvironmentalVariables specified using the 
    /// ConfigEnvironmentVariable attribute
    /// </summary>
    public abstract class Config
    { 
        public const string DEF_CONFIG_FOLDER = ".landform";

        public static string BaseCommand; //may be null
        public static string SubCommand;
        public static string FullCommand
        {
            get
            {
                return !string.IsNullOrEmpty(BaseCommand) ?
                    BaseCommand + (!string.IsNullOrEmpty(SubCommand) ? ("-" + SubCommand) : "") : null;
            }
        }

        public static string AppVersion; //may be null

        public static string[] CommandLineArgs;

        public static ConfigDefaultsProvider DefaultsProvider;

        public static ILogger Logger { get; private set; }

        private static List<string> pendingLogs = new List<string>();

        public static void SetLogger(ILogger logger)
        {
            Config.Logger = logger;
            if (logger != null && pendingLogs.Count > 0)
            {
                foreach (var msg in pendingLogs)
                {
                    logger.LogInfo(msg);
                }
                pendingLogs.Clear();
            }
        }

        public static void Log(string msg)
        {
            if (Logger != null)
            {
                Logger.LogInfo(msg);
            }
            else
            {
                pendingLogs.Add(msg);
            }
        }

        public Config()
        {
            LoadDefaults();
            Load(onlyIfAssociatedWithFile: true);
            LoadEnvironmentalVariables();
        }

        /// <summary>
        /// Defaults to user's home directory.
        /// </summary>
        public static string ConfigDir;

        public static string GetConfigDir()
        {
            return !string.IsNullOrEmpty(ConfigDir) ? ConfigDir : PathHelper.GetHomeDir();
        }

        /// <summary>
        /// Application config folder
        /// Config files for this application should be stored in a folder of this name under ConfigDir
        /// This should be just a single folder name not an entire directory path
        /// If this is not set application will not try to read configuration files from disk
        /// </summary>
        public static string ConfigFolder = DEF_CONFIG_FOLDER;

        /// <summary>
        /// Name of configuration file.  This should return just the name of the file without .json or a path
        /// Null or empty means no associated file.
        /// </summary>
        public virtual string ConfigFileName()
        {
            return null;
        }

        /// <summary>
        /// Get full path to config file, if any, else null.
        /// </summary>
        public string ConfigFilePath()
        {
            string fn = ConfigFileName();
            return !string.IsNullOrEmpty(fn) ? Path.Combine(GetConfigDir(), ConfigFolder, fn + ".json") : null;
        }

        public void Save(bool onlyIfAssociatedWithFile = false)
        {
            string file = ConfigFilePath();

            if (string.IsNullOrEmpty(file))
            {
                if (onlyIfAssociatedWithFile)
                {
                    return;
                }
                throw new Exception(GetType().Name + " not associated with a file");
            }

            PathHelper.EnsureExists(Path.GetDirectoryName(file));

            File.WriteAllText(file, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public void Load(bool onlyIfAssociatedWithFile = false)
        {
            string file = ConfigFilePath();

            if (string.IsNullOrEmpty(file))
            {
                if (onlyIfAssociatedWithFile)
                {
                    return;
                }
                throw new Exception(GetType().Name + " not associated with a file");
            }

            if (File.Exists(file))
            {
                string json = null;
                try
                {
                    json = File.ReadAllText(file);
                }
                catch (Exception ex)
                {
                    throw new Exception($"error reading {GetType().Name} file {file}: {ex.Message}");
                }
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        JsonConvert.PopulateObject(json, this);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"error parsing JSON for {GetType().Name} from {file}: {ex.Message}\n" +
                                            json.Replace("{", "{{").Replace("}", "}}"));
                    }
                }
            }
        }

        public void LoadDefaults()
        {
            string json = DefaultsProvider != null ? DefaultsProvider.GetConfigDefaults(ConfigFileName()) : null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    JsonConvert.PopulateObject(json, this);
                }
                catch (Exception ex)
                {
                    throw new Exception($"error loading JSON defaults for {GetType().Name}: {ex.Message}\n" +
                                        json.Replace("{", "{{").Replace("}", "}}"));
                }
            }
        }

        public void LoadEnvironmentalVariables()
        {
            var type = GetType();
            var members = type.GetProperties().Where(p => p.CanWrite).Cast<MemberInfo>().Concat(type.GetFields());
            foreach (var member in members)
            {
                var attrib = member.GetCustomAttribute<ConfigEnvironmentVariable>();
                if (attrib != null && !string.IsNullOrEmpty(attrib.EnvironmentalVariableName))
                {
                    string name = attrib.EnvironmentalVariableName;
                    string str = Environment.GetEnvironmentVariable(name);
                    if (!string.IsNullOrEmpty(str))
                    {
                        SetProperty(member, str, name);
                    }
                    else if (str != null)
                    {
                        Log($"ignoring empty environment variable {name}");
                    }
                }
            }
        }

        private void SetProperty(MemberInfo member, string value, string name)
        {
            if (!(member is FieldInfo || member is PropertyInfo))
            {
                throw new Exception("unexpected type: " + member.GetType().Name);
            }

            var type = member is FieldInfo ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;

            void setValue(Object val)
            {
                Log($"using {name}={val.ToString()} for {GetType().Name}");
                if (member is FieldInfo)
                {
                    ((FieldInfo)member).SetValue(this, val);
                }
                else
                {
                    ((PropertyInfo)member).SetValue(this, val);
                }
            }

            try
            {
                ParseEnvVal(value, type, setValue);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("error setting config {0} value {1} from env var {2}={3}: {4}",
                                                  type.Name, member.Name, name, value, ex.Message));
            }
        }

        public static void ParseEnvVal(string str, Type type, Action<Object> func, bool parseNonemptyAsTrue = false)
        {
            Func<string, bool> parseBool = s =>
                !string.IsNullOrEmpty(s) && (parseNonemptyAsTrue || s.ToLower() == "true");
            new TypeDispatcher()
                .Case<string>(_ => func(str))
                .Case<int>(_ => func(int.Parse(str)))
                .Case<byte>(_ => func(byte.Parse(str)))
                .Case<short>(_ => func(short.Parse(str)))
                .Case<long>(_ => func(long.Parse(str)))
                .Case<uint>(_ => func(uint.Parse(str)))
                .Case<ushort>(_ => func(ushort.Parse(str)))
                .Case<ulong>(_ => func(ulong.Parse(str)))
                .Case<float>(_ => func(float.Parse(str)))
                .Case<double>(_ => func(double.Parse(str)))
                .Case<bool>(_ => func(parseBool(str)))
                .Case<Enum>(_ => func(Enum.Parse(type, str)))
                .Handle(type);
        }
    }
}

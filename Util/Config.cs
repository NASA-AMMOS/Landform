
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace OPS.Util
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
        public static string PipelineVersion; //may be null

        public static string[] CommandLineArgs;

        public static ConfigDefaultsProvider DefaultsProvider;

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
                JsonConvert.PopulateObject(File.ReadAllText(file), this);
            }
        }

        public void LoadDefaults()
        {
            string json = DefaultsProvider != null ? DefaultsProvider.GetConfigDefaults(ConfigFileName()) : null;
            if (!string.IsNullOrEmpty(json))
            {
                JsonConvert.PopulateObject(json, this);
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
                    string str = Environment.GetEnvironmentVariable(attrib.EnvironmentalVariableName);
                    if (str != null)
                    {
                        SetProperty(member, str);
                    }
                }
            }
        }

        private void SetProperty(MemberInfo member, string value)
        {
            if (!(member is FieldInfo || member is PropertyInfo))
            {
                throw new Exception("unexpected type: " + member.GetType().Name);
            }

            void setValue(Object val)
            {
                if (member is FieldInfo)
                {
                    ((FieldInfo)member).SetValue(this, val);
                }
                else
                {
                    ((PropertyInfo)member).SetValue(this, val);
                }
            }

            var type = member is FieldInfo ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;

            Func<string, bool> parseBool = str => !string.IsNullOrEmpty(str) && str.ToLower() == "true";

            new TypeDispatcher()
                .Case<string>(v => setValue(value))
                .Case<int>(v => setValue(int.Parse(value)))
                .Case<byte>(v => setValue(byte.Parse(value)))
                .Case<short>(v => setValue(short.Parse(value)))
                .Case<long>(v => setValue(long.Parse(value)))
                .Case<uint>(v => setValue(uint.Parse(value)))
                .Case<ushort>(v => setValue(ushort.Parse(value)))
                .Case<ulong>(v => setValue(ulong.Parse(value)))
                .Case<float>(v => setValue(float.Parse(value)))
                .Case<double>(v => setValue(double.Parse(value)))
                .Case<bool>(v => setValue(parseBool(value)))
                .Handle(type, value);
        }
    }
}

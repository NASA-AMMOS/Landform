using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

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

    /// <summary>
    /// Class for specifying application configuration 
    /// Extend this class with public properties.  Properties can be read from files json in a folder
    /// under the users home directory or optionaly EnvironmentalVariables specified using the 
    /// ConfigEnvironmentVariable attribute
    /// </summary>
    public abstract class Config
    { 
        public const string DEF_CONFIG_FOLDER = ".landform";

        public static string BaseCommand;
        public static string SubCommand;
        public static string FullCommand {
            get
            {
                return !string.IsNullOrEmpty(BaseCommand) ?
                    BaseCommand + (!string.IsNullOrEmpty(SubCommand) ? ("-" + SubCommand) : "") : null;
            }
        }

        public static string[] CommandLineArgs;

        public Config()
        {
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

            if (!File.Exists(file))
            {
                throw new Exception(string.Format("{0}: file {1} not found", GetType().Name, file));
            }

            JsonConvert.PopulateObject(File.ReadAllText(file), this);
        }

        public void LoadEnvironmentalVariables()
        {
            foreach (var prop in GetType().GetProperties().Where(p => p.CanWrite))
            {
                var attrib = prop.GetCustomAttribute<ConfigEnvironmentVariable>();
                if (attrib != null && !string.IsNullOrEmpty(attrib.EnvironmentalVariableName))
                {
                    string str = Environment.GetEnvironmentVariable(attrib.EnvironmentalVariableName);
                    if (str != null)
                    {
                        SetProperty(prop, str);
                    }
                }
            }
        }

        private void SetProperty(PropertyInfo prop, string value)
        {
            Func<string, bool> parseBool = str => !string.IsNullOrEmpty(str) && str.ToLower() == "true";
            new TypeDispatcher()
                .Case<string>(v => prop.SetValue(this, value))
                .Case<int>(v => prop.SetValue(this, int.Parse(value)))
                .Case<byte>(v => prop.SetValue(this, byte.Parse(value)))
                .Case<short>(v => prop.SetValue(this, short.Parse(value)))
                .Case<long>(v => prop.SetValue(this, long.Parse(value)))
                .Case<uint>(v => prop.SetValue(this, uint.Parse(value)))
                .Case<ushort>(v => prop.SetValue(this, ushort.Parse(value)))
                .Case<ulong>(v => prop.SetValue(this, ulong.Parse(value)))
                .Case<float>(v => prop.SetValue(this, float.Parse(value)))
                .Case<double>(v => prop.SetValue(this, double.Parse(value)))
                .Case<bool>(v => prop.SetValue(this, parseBool(value)))
                .Handle(prop.PropertyType);
        }
    }
}

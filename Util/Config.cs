using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

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
        /// <summary>
        /// Name of configuration file.  This should return just the name of the file without .json or a path
        /// </summary>
        /// <returns></returns>

        protected virtual string ConfigFilename()
        {
            return null;
        }

        public string ConfigFilepath()
        {
            return FullPathToConfig(ConfigFilename());
        }

        /// <summary>
        /// Set the name of the application config folder
        /// Config files for this application should be stored in a folder of this name under the users home directory
        /// This should be just a single folder name not an entire directory path
        /// If this is not set application will not try to read configuration files from disk
        /// </summary>
        public static string ApplicationConfigFolder { get; set; }

        public static string BaseCommand { get; set; }
        public static string SubCommand { get; set; }
        public static string FullCommand {
            get
            {
                string sc = !string.IsNullOrEmpty(SubCommand) ? "-" + SubCommand : "";
                return BaseCommand + sc;
            }
        }

        static string FullPathToConfig(string filename)
        {
            if (ApplicationConfigFolder == null || filename == null)
            {
                return null;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ApplicationConfigFolder, filename + ".json");
        }

        public void Save()
        {
            string filename = ConfigFilepath();
            PathHelper.EnsureExists(Path.GetDirectoryName(filename));
            File.WriteAllText(filename, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public Config()
        {
            // Read from config file
            string filename = ConfigFilepath();
            if (filename != null && File.Exists(filename))
            {
                JsonConvert.PopulateObject(File.ReadAllText(filename), this);
            }
            // Parse any config variables from environment variables
            foreach (MemberInfo member in this.GetType().GetProperties())
            {
                ConfigEnvironmentVariable ca = member.GetCustomAttribute<ConfigEnvironmentVariable>();
                if (ca != null)
                {
                    string str = Environment.GetEnvironmentVariable(ca.EnvironmentalVariableName);
                    if (str != null)
                    {
                        PropertyInfo prop = (PropertyInfo)member;
                        if (prop.PropertyType == typeof(string))
                        {
                            prop.SetValue(this, str);
                        }
                        else if (prop.PropertyType == typeof(int))
                        {
                            prop.SetValue(this, int.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(byte))
                        {
                            prop.SetValue(this, byte.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(short))
                        {
                            prop.SetValue(this, short.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(long))
                        {
                            prop.SetValue(this, long.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(uint))
                        {
                            prop.SetValue(this, uint.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(ushort))
                        {
                            prop.SetValue(this, ushort.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(ulong))
                        {
                            prop.SetValue(this, ulong.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(float))
                        {
                            prop.SetValue(this, float.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(double))
                        {
                            prop.SetValue(this, double.Parse(str));
                        }
                        else if (prop.PropertyType == typeof(bool))
                        {
                            prop.SetValue(this, !string.IsNullOrEmpty(str));
                        }
                        else
                        {
                            throw new Exception("Cannot specify config arguments of type " + prop.PropertyType + "with environment variables");
                        }
                    }
                }
            }
        }
    }

}

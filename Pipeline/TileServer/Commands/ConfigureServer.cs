using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{

    [Verb("configure", HelpText = "Configures a venue")]
    public class ConfigureServerOptions
    {
        [Option(HelpText = "Venue Name", Default = null)]
        public string VenueName { get; set; }
        
        [Option(HelpText = "S3 Url", Default = null)]
        public string S3Url { get; set; }

        [Option(HelpText = "AWS Region", Default = null)]
        public string Region { get; set; }

        [Option(HelpText = "AWS Profile", Default = null)]
        public string Profile { get; set; }
    }

    public class ConfigureServer
    {
        ConfigureServerOptions options;
        public ConfigureServer(ConfigureServerOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            var config = new TileServerConfig();
            config.VenueName = ReadProperty("Venue Name", options.VenueName, config.VenueName);
            config.S3Url = ReadProperty("S3 Url", options.S3Url, config.S3Url);
            config.Region = ReadProperty("AWS Region", options.Region, config.Region);
            config.Profile = ReadProperty("AWS Profile", options.Profile, config.Profile);
            config.Save();
            return 0;
        }


        string ReadProperty(string prompt, string optionsValue, string configValue)
        {
            string defaultValue = configValue == null ? "" : "[" + configValue + "]";
            if (optionsValue != null)
            {
                Console.WriteLine(prompt + defaultValue + ": " + optionsValue);
                return optionsValue;
            }
            bool valid = false;
            while (!valid)
            {
                Console.Write(prompt + defaultValue + ": ");
                var line = Console.ReadLine().Trim();
                if (string.IsNullOrEmpty(line))
                {
                    if (configValue != null)
                    {
                        return configValue;
                    }
                }
                else
                {
                    return line;
                }
            }
            return null;
        }

    }
}

using CommandLine;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using OPS.Cloud;

namespace OPS.Pipeline.TileServer
{
    [Verb("configure", HelpText = "Configures a Landform server venue")]
    public class ConfigureServerOptions : PipelineCoreOptions
    {
        [Option(Default = null, HelpText = "Venue Name")]
        public string Venue { get; set; }
        
        [Option(Default = null, HelpText = "S3 Url")]
        public string S3Url { get; set; }

        [Option(Default = null, HelpText = "AWS Region")]
        public string AWSRegion { get; set; }

        [Option(Default = null, HelpText = "AWS Profile")]
        public string AWSProfile { get; set; }

        [Option(Default = "mslice", HelpText = "MSLICE Profile")]
        public string MSLICEAWSProfile { get; set; }

        [Option(Default = "s3://red-product/", HelpText = "MSLICE S3 Url")]
        public string MSLICES3Url { get; set; }

        [Option(Default = false, HelpText = "Do not persist config")]
        public bool NoPersist { get; set; }

        [Option(Default = false, HelpText = "Do not write user data script")]
        public bool NoUserData { get; set; }
    }

    public class ConfigureServer
    {
        private ConfigureServerOptions options;
        private static ILog logger = LogManager.GetLogger(typeof(ConfigureServer));

        public ConfigureServer(ConfigureServerOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            CloudPipelineConfig config = new CloudPipelineConfig();

            config.Venue = ReadProperty("Venue Name", options.Venue, config.Venue);
            config.S3Url = ReadProperty("S3 Url", options.S3Url, config.S3Url);
            config.AWSRegion = ReadProperty("AWS Region", options.AWSRegion, config.AWSRegion);
            config.AWSProfile = ReadProperty("AWS Profile", options.AWSProfile, config.AWSProfile);
            config.MSLICEAWSProfile = ReadProperty("MSLICE AWS Profile", options.MSLICEAWSProfile, config.MSLICEAWSProfile);
            config.MSLICES3Url = ReadProperty("MSLICE S3 Url", options.MSLICES3Url, config.MSLICES3Url);

            var cfgPath = config.ConfigFilepath();
            if (!options.NoPersist)
            {
                logger.Info("persisting config to " + cfgPath);
                config.Save();
            }
            else
            {
                logger.Info("not persisting config to " + cfgPath);
            }
            string userDataPath = Path.GetFullPath("ec2userdata.txt");
            if (!options.NoUserData)
            {
                logger.Info("saving EC2 user data script to " + userDataPath);
                File.WriteAllText(userDataPath, BuildEC2UserDataScript(config));
            }
            else
            {
                logger.Info("not saving EC2 user data script to " + userDataPath);
            }
            return 0;
        }

        string BuildEC2UserDataScript(CloudPipelineConfig config)
        {

            string template = @"<powershell>
New-Item -Path ""c:\temp"" -ItemType ""directory"" -Force
(new-object net.webclient).DownloadFile('https://aka.ms/vs/15/release/VC_redist.x64.exe','c:\temp\vc_redist_2017.x64.exe')
c:\temp\vc_redist_2017.x64.exe /quiet
(new-object net.webclient).DownloadFile('https://download.microsoft.com/download/9/3/F/93FCF1E7-E6A4-478B-96E7-D4B285925B00/vc_redist.x64.exe','c:\temp\vc_redist_2015.x64.exe')
c:\temp\vc_redist_2015.x64.exe /quiet
#Set-ExecutionPolicy RemoteSigned -Force
Import-Module AWSPowerShell
(new-object net.webclient).DownloadFile('https://aws-codedeploy-us-west-1.s3.amazonaws.com/latest/codedeploy-agent.msi','c:\temp\codedeploy-agent.msi')
c:\temp\codedeploy-agent.msi /quiet /l c:\temp\host-agent-install-log.txt
powershell.exe -Command Read-S3Object -BucketName {2} -Key {0}{3}/app/tileserver.zip -File c:\temp\tileserver.zip
# ExtractToDirectory() will not overwrite existing files
# and this EC2 instance filesystem is persistent across reboots
# so to handle the case that tileserver.zip has been updated, first blow away any existing c:\tileserver
# this will lose any old logs under c:\tileserver\log too, but that's arguably FNB
Remove-Item c:\tileserver -Force -Recurse -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory(""C:\temp\tileserver.zip"", ""c:\tileserver"")
c:\tileserver\TilingServer.exe configure --venue={0} --s3url={1} --awsregion={4} --awsprofile=null --nouserdata
Start-Process -WorkingDirectory c:\tileserver c:\tileserver\TilingServer.exe startworker
</powershell>
<persist>true</persist>";
            S3Url url = new S3Url(config.S3Url);
            //                             0                 1             2               3           4
            return string.Format(template, config.Venue, config.S3Url, url.BucketName, url.Prefix, config.AWSRegion);

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

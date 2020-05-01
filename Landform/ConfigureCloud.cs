using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;
using log4net;
using OPS.Cloud;
using OPS.Util;
using OPS.Pipeline;

/// <summary>
/// Utility to write ~/.landform/landform-cloud.json
///
/// Can be run interactively or in batch mode specifying settings by command line options.
///
/// Can also optionally write an EC2 user data script.
///
/// As of 3/27/20 this is not used much anymore, except Web/tools/configureBackend.js as part of the AMMOS TilingServer.
///
/// Instead see ConfigureLocal.cs
///
/// Example command line, batch mode, as used by Web/tools/configureBackend.js:
///
/// Landform.exe configure-cloud --venue=landform-dev-test --s3url=s3://landlords-dev/landform-web
///   --awsregion=us-west-1 --awsprofile=default
///   --msliceawsprofile=mslice --msliceawsregion=us-west-1 --mslices3url=s3://red-product
///   --workerexecutable=TilingServer.exe
/// </summary>
namespace OPS.Landform
{
    [Verb("configure-cloud", HelpText = "Configures Landform cloud")]
    public class ConfigureCloudOptions : ConfigureBaseOptions
    {
        //NOTE: any non-null default values for options will short circuit the Prompt() functionality
        //because it can't differentiate an option that got its value as a default
        //vs an option that was explicitly specified on the command line
        //instead put defaults in CloudPipelineConfig

        [Option(Default = null, HelpText = "S3 url")]
        public string S3Url { get; set; }

        [Option(Default = null, HelpText = "AWS region")]
        public string AWSRegion { get; set; }

        [Option(Default = null, HelpText = "AWS profile")]
        public string AWSProfile { get; set; }

        [Option(Default = null, HelpText = "MSLICE profile")]
        public string MSLICEAWSProfile { get; set; }

        [Option(Default = null, HelpText = "MSLICE region")]
        public string MSLICEAWSRegion { get; set; }

        [Option(Default = null, HelpText = "MSLICE S3 url")]
        public string MSLICES3Url { get; set; }

        [Option(Default = null, HelpText = "legacy compatibility (read only) true/false")]
        public string LegacyCompat { get; set; }

        [Option(Default = false, HelpText = "Do not persist config")]
        public bool NoPersist { get; set; }

        [Option(Default = false, HelpText = "Do not write user data script")]
        public bool NoUserData { get; set; }

        [Option(Default = "Landform.exe", HelpText = "Worker executable name to embed in user data script")]
        public string WorkerExecutable { get; set; }
    }

    public class ConfigureCloud
    {
        private ConfigureCloudOptions options;

        private ILog logger = LogManager.GetLogger(typeof(ConfigureCloud));

        public ConfigureCloud(ConfigureCloudOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                CloudPipelineConfig config = new CloudPipelineConfig();
                
                config.Venue = ConsoleHelper.Prompt("venue", options.Venue, config.Venue, options.Interactive);
                config.S3Url = ConsoleHelper.Prompt("S3 url", options.S3Url, config.S3Url, options.Interactive);
                config.AWSRegion = ConsoleHelper.Prompt("AWS region", options.AWSRegion, config.AWSRegion,
                                                        options.Interactive);
                config.AWSProfile = ConsoleHelper.Prompt("AWS profile", options.AWSProfile, config.AWSProfile,
                                                         options.Interactive);
                config.MSLICEAWSProfile = ConsoleHelper.Prompt("MSLICE AWS profile", options.MSLICEAWSProfile,
                                                               config.MSLICEAWSProfile, options.Interactive);
                config.MSLICEAWSRegion = ConsoleHelper.Prompt("MSLICE AWS region",options.MSLICEAWSRegion,
                                                              config.MSLICEAWSRegion, options.Interactive);
                config.MSLICES3Url = ConsoleHelper.Prompt("MSLICE S3 url", options.MSLICES3Url, config.MSLICES3Url,
                                                          options.Interactive);
                config.MaxCores = ConsoleHelper.Prompt("max cores, 0 = all available, N = up to N, -M = reserve M",
                                                       options.MaxCores, config.MaxCores, options.Interactive);
                config.RandomSeed = ConsoleHelper.Prompt("negative to use a time dependent random seed",
                                                         options.RandomSeed, config.RandomSeed, options.Interactive);
                config.LegacyCompat = ConsoleHelper.Prompt("legacy compatability (read only) true/false",
                                                           options.LegacyCompat, config.LegacyCompat,
                                                           options.Interactive);
                
                config.Validate();
                
                var cfgPath = config.ConfigFilePath();
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
            }
            catch (Exception ex)
            {
                Logging.LogException(logger, ex);
                return 1;
            }
            return 0;
        }

        private string BuildEC2UserDataScript(CloudPipelineConfig config)
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
powershell.exe -Command Read-S3Object -BucketName {2} -Key {3}/app/landform-worker.zip -File c:\temp\landform-worker.zip
# ExtractToDirectory() will not overwrite existing files
# and this EC2 instance filesystem is persistent across reboots
# so to handle the case that landform-worker.zip has been updated, first blow away any existing c:\landform
# this will lose any old logs under c:\landform\log too, but that's arguably FNB
Remove-Item c:\landform -Force -Recurse -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory(""C:\temp\landform-worker.zip"", ""c:\landform"")
c:\landform\{11} configure-cloud --venue={0} --s3url={1} --awsregion={4} --awsprofile=null --msliceawsprofile={5} --msliceawsregion={6} --mslices3url={7} --maxcores={8} --randomseed={9} --legacycompat={10} --nouserdata
Start-Process -WorkingDirectory c:\landform c:\landform\{11} worker
</powershell>
<persist>true</persist>";
            S3Url url = new S3Url(config.S3Url);
            return string.Format(template,
                                 config.Venue, //0
                                 config.S3Url, //1
                                 url.BucketName, //2
                                 url.Path, //3
                                 config.AWSRegion, //4
                                 config.MSLICEAWSProfile, //5
                                 config.MSLICEAWSRegion, //6
                                 config.MSLICES3Url, //7
                                 config.MaxCores, //8
                                 config.RandomSeed, //9
                                 config.LegacyCompat, //10
                                 options.WorkerExecutable); //11
        }
    }
}

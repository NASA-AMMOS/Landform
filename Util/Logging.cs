using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Core;

namespace OPS.Util
{
    public interface ILogger
    {
        void LogInfo(string msg, params Object[] args);
        void LogVerbose(string msg, params Object[] args);
        void LogDebug(string msg, params Object[] args);
        void LogWarn(string msg, params Object[] args);
        void LogError(string msg, params Object[] args);
    }

    public class Logging
    {
        //%level must be last token before : to faciltate parsing errors in web code
        const string DEBUG_PATTERN_LAYOUT = "%date %logger{1} %location %level: %message%newline";

        private static volatile bool didConfig = false;
        public static void ConfigureLogging(bool quiet = false, bool debug = false, string overrideLogFilename = null)
        {
            //this is used as part of the the default log filename
            log4net.GlobalContext.Properties["command"] = Config.FullCommand;

            //normally Logging.ConfigureLogging() would only be called once during app init
            //but there are some cases where it's hard to structure the code
            //to avoid more than one possible call
            //that's OK, but we only want to set things up from App.config once
            //if we call XmlConfigurator.Configure() more than once than one effect
            //is that we can get get extra log files on disk
            //because each call can create a log file with a different timestamp in the filename
            if (!didConfig)
            {
                log4net.Config.XmlConfigurator.Configure();
                didConfig = true;
            }

            var h = (log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository();

            h.Root.Level = debug ? Level.Debug : Level.Info;
            h.RaiseConfigurationChanged(EventArgs.Empty);

            //it is fairly tricky to change log filename at runtime
            //https://stackoverflow.com/a/6963420
            foreach (IAppender a in h.Root.Appenders)
            {
                if (a is FileAppender)
                {
                    FileAppender fa = (FileAppender)a;
                    bool fileChanged = !string.IsNullOrEmpty(overrideLogFilename);
                    FileInfo oldFile = null;
                    if (fileChanged)
                    {
                        oldFile = new FileInfo(fa.File);
                        fa.File = Path.Combine(oldFile.DirectoryName, overrideLogFilename);
                    }
                    if (debug)
                    {
                        fa.Layout = new PatternLayout(DEBUG_PATTERN_LAYOUT);
                    }
                    if (fileChanged || debug)
                    {
                        fa.ActivateOptions();
                        if (oldFile != null && oldFile.Exists)
                        {
                            //if (oldFile.Length == 0)
                            //https://github.jpl.nasa.gov/OnSight/Landform/issues/350
                            if (oldFile.Length < 5)
                            {
                                if (oldFile.Length > 0 && !quiet)
                                {
                                    Console.WriteLine(string.Format("deleting log file {0} " +
                                                                    "with only {1} bytes before changing filename",
                                                                    oldFile, oldFile.Length));
                                }

                                //the log filename has changed, but no logs have been written yet to the old file
                                //it seems that log4net creates the file (zero-length) before anything gets written
                                //in this case, just delete the old filename because most of the point of this whole
                                //thing is to try to avoid the filesystem getting littered up with a lot of different
                                //log files - and zero length log files are of pretty much no use anyway
                                try
                                {
                                    oldFile.Delete();
                                }
                                catch (Exception e)
                                {
                                    if (!quiet)
                                    {
                                        Console.WriteLine(string.Format("error deleting empty log file ({0}): {1}",
                                                                        e.GetType().FullName, e.Message));
                                    }
                                }
                            }
                            else
                            {
                                //the log filename has changed, but logs have already been written to the
                                //old filename - so leave it there
                                if (!quiet)
                                {
                                    Console.WriteLine(string.Format("changing log file to {0}, " +
                                                                    "old log file {1} not empty", fa.File, oldFile));
                                }
                            }
                        }
                    }
                    if (!quiet)
                    {
                        Console.WriteLine(string.Format("logging to {0}", fa.File));
                    }
                }
                else if (a is ConsoleAppender)
                {
                    ConsoleAppender ca = (ConsoleAppender)a;
                    if (debug)
                    {
                        ca.Layout = new PatternLayout(DEBUG_PATTERN_LAYOUT);
                    }
                    if (quiet)
                    {
                        ca.Threshold = log4net.Core.Level.Off;
                    }
                    if (debug || quiet)
                    {
                        ca.ActivateOptions();
                    }
                }
            }
        }
    }
}

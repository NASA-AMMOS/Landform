using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using log4net;
using log4net.Appender;
using log4net.Layout;

namespace OPS.Util
{
    public class Logging
    {
        //%level must be last token before : to faciltate parsing errors in web code
        const string DEBUG_PATTERN_LAYOUT = "%date %logger{1} %location %level: %message%newline";

        public static void ConfigureLogging(bool quiet = false, bool debug = false, string overrideLogFilename = null)
        {
            //this is used as part of the the default log filename
            log4net.GlobalContext.Properties["command"] = Config.FullCommand;

            log4net.Config.XmlConfigurator.Configure();

            //it is fairly tricky to change log filename at runtime
            //https://stackoverflow.com/a/6963420
            var h = (log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository();
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
                            if (oldFile.Length == 0)
                            {
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

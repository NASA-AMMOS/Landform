using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using log4net;
using log4net.Appender;

namespace OPS.Util
{
    public class Logging
    {
        public static void ConfigureLogging(bool quiet = false, string overrideLogFilename = null)
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
                    if (!string.IsNullOrEmpty(overrideLogFilename))
                    {
                        var old = new FileInfo(fa.File);
                        fa.File = Path.Combine(old.DirectoryName, overrideLogFilename);
                        fa.ActivateOptions();
                        if (old.Exists)
                        {
                            if (old.Length == 0)
                            {
                                try
                                {
                                    old.Delete();
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
                                                                    "old log file {1} not empty", fa.File, old));
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
                    if (quiet)
                    {
                        ca.Threshold = log4net.Core.Level.Off;
                        ca.ActivateOptions();
                    }
                }
            }
        }
    }
}

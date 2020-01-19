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

        /// <summary>
        /// for a non aggregate exception, default is to just spew its message
        /// because that is commonly going to be enough and may be user visible (e.g. invalid command line args)
        /// for an aggregate we spew the message and stack trace of the first inner exception
        /// because that is most likely an unexpected error that needs to be debugged
        /// </summary>
        void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false,
                          bool aggregateStackTrace = true);
    }

    public class ThunkLogger : ILogger
    {
        public Action<string> Info, Verbose, Debug, Warn, Error;
        public Action<Exception, string, int, bool, bool> Exception;

        public void LogInfo(string msg, params Object[] args)
        {
            Log(Info ?? Verbose ?? Debug, msg, args);
        }

        public void LogVerbose(string msg, params Object[] args)
        {
            Log(Verbose ?? Debug, msg, args);
        }

        public void LogDebug(string msg, params Object[] args)
        {
            Log(Debug, msg, args);
        }

        public void LogWarn(string msg, params Object[] args)
        {
            if (Warn != null)
            {
                Log(Warn, msg, args);
            }
            else
            {
                LogInfo("WARN: " + msg, args);
            }
        }

        public void LogError(string msg, params Object[] args)
        {
            if (Error != null)
            {
                Log(Error, msg, args);
            }
            else
            {
                LogInfo("ERROR: " + msg, args);
            }
        }

        public void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false,
                                 bool aggregateStackTrace = true)
        {
            if (Exception != null)
            {
                Exception(ex, msg, maxAggregateSpew, stackTrace, aggregateStackTrace);
            }
            else
            {
                LogError("{0}: {1}", msg, ex.Message);
            }
        }

        private void Log(Action<string> thunk, string msg, params Object[] args)
        {
            if (thunk != null)
            {
                thunk(string.Format(msg, args));
            }
        }
    }

    public class Logging
    {
        //%level must be last token before : to faciltate parsing errors in web code
        const string DEBUG_PATTERN_LAYOUT = "%date %logger{1} %location %level: %message%newline";

        public static string GetLogFile()
        {
            var h = (log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository();
            foreach (IAppender a in h.Root.Appenders)
            {
                if (a is FileAppender)
                {
                    return ((FileAppender)a).File;
                }
            }
            return null;
        }

        private static volatile bool didConfig = false;
        public static void ConfigureLogging(string commandName = null, bool quiet = false, bool debug = false,
                                            string logFilename = null, string logDir = null)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                var exe = PathHelper.GetExe();
                if (!string.IsNullOrEmpty(exe))
                {
                    commandName = StringHelper.GetLastUrlPathSegment(exe, stripExtension: true); //backlashes are ok
                }
                else
                {
                    commandName = "Landform";
                }
            }
            log4net.GlobalContext.Properties["command"] = commandName; //used in the default log filename

            //normally Logging.ConfigureLogging() would only be called once during app init
            //but there are some cases where it's hard to structure the code
            //to avoid more than one possible call
            //that's OK, but we only want to set things up from App.config once
            //if we call XmlConfigurator.Configure() more than once
            //then one effect is that we can get get extra log files on disk
            //because each call can create a log file with a different timestamp in the filename
            //note that we want to configure from xml first to get the default log filename
            //below we might change that entirely or we might only change the directory
            if (!didConfig)
            {
                log4net.Config.XmlConfigurator.Configure();
                didConfig = true;
            }

            string logFile = null;
            if (!string.IsNullOrEmpty(logDir))
            {
                if (string.IsNullOrEmpty(logFilename))
                {
                    logFilename = Path.GetFileName(GetLogFile());
                }
                logFile = Path.Combine(logDir, logFilename);
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
                    bool fileChanged = !string.IsNullOrEmpty(logFilename) || !string.IsNullOrEmpty(logFile);
                    FileInfo oldFile = null;
                    if (fileChanged)
                    {
                        oldFile = new FileInfo(fa.File);
                        if (!string.IsNullOrEmpty(logFile))
                        {
                            fa.File = logFile;
                        }
                        else
                        {
                            fa.File = Path.Combine(oldFile.DirectoryName, logFilename);
                        }
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

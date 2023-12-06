using System;
using System.Collections.Generic;
using System.IO;
using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Core;

namespace JPLOPS.Util
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
        void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false);
    }

    public class ThunkLogger : ILogger
    {
        public Action<string> Info, Verbose, Debug, Warn, Error;
        public Action<Exception, string, int, bool> Exception;

        public ThunkLogger(Action<string> info = null, Action<string> verbose = null, Action<string> debug = null,
                           Action<string> warn = null, Action<string> error = null,
                           Action<Exception, string, int, bool> exception = null)
        {
            this.Info = info;
            this.Verbose = verbose;
            this.Debug = debug;
            this.Warn = warn;
            this.Error = error;
            this.Exception = exception;
        }

        public ThunkLogger(ILog logger)
        {
            Info = msg => logger.Info(msg);
            Debug = msg => logger.Debug(msg);
            Warn = msg => logger.Warn(msg);
            Error = msg => logger.Error(msg);
        }

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

        public void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false)
        {
            if (Exception != null)
            {
                Exception(ex, msg, maxAggregateSpew, stackTrace);
            }
            else
            {
                LogError((!string.IsNullOrEmpty(msg) ? msg + ": " : "") +
                         $"({ex.GetType().Name}) {ex.Message}\n{Logging.GetStackTrace(ex)}");
                var innerExceptions = Logging.GetInnerExceptions(ex);
                if (innerExceptions != null)
                {
                    foreach (var ex2 in innerExceptions)
                    {
                        LogException(ex2, null, maxAggregateSpew, stackTrace);
                    }
                }
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

        public static IEnumerable<Exception> GetInnerExceptions(Exception ex)
        {
            if (ex is AggregateException)
            {
                return (ex as AggregateException).InnerExceptions;
            }
            else if (ex.InnerException != null)
            {
                return new Exception[] { ex.InnerException };
            }
            else
            {
                return new Exception[] { };
            }
        }

        public static void LogException(ILog logger, Exception ex)
        {
            logger.ErrorFormat("({0}) {1}\n{2}", ex.GetType().Name, ex.Message, GetStackTrace(ex));
            var innerExceptions = Logging.GetInnerExceptions(ex);
            if (innerExceptions != null)
            {
                foreach (var ex2 in innerExceptions)
                {
                    logger.ErrorFormat("{0}\n{1}", ex2.Message, GetStackTrace(ex2));
                }
            }
        }

        public static string GetStackTrace(Exception ex)
        {
            try
            {
                return ex.StackTrace;
            }
            catch (Exception ex2)
            {
                return "(error getting stack trace): " + ex2.Message;
            }
        }
        
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
            if (!string.IsNullOrEmpty(logFilename) && StringHelper.NormalizeSlashes(logFilename).IndexOf('/') >= 0)
            {
                throw new Exception(string.Format("log filename must not contain directory, got \"{0}\"", logFilename));
            }

            //we started sometimes getting 
            //System.TypeInitializationException: The type initializer for 'Amazon.AWSConfigs' threw an exception.
            //System.Runtime.InteropServices.COMException: Catastrophic failure
            //at System.Security.Policy.PEFileEvidenceFactory.GetLocationEvidence(...)
            //...
            //at System.Configuration.ClientConfigPaths.GetEvidenceInfo(...)
            //...
            //at System.Configuration.ConfigurationManager.GetSection(String sectionName)
            //at Amazon.AWSConfigs.GetSection[T](String sectionName)
            //at Amazon.AWSConfigs..cctor()
            //
            //workaround from https://stackoverflow.com/a/15759103
            try
            {
                System.Configuration.ConfigurationManager.GetSection("dummy");
            }
            catch (Exception)
            {
                //ignore
            }
            //also see the end of this function for another workaround

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
            //below we might change that entirely or we might only change the directory or prefix
            if (!didConfig)
            {
                log4net.Config.XmlConfigurator.Configure();
                didConfig = true;
            }

            if (logFilename != null && logFilename.Contains(":"))
            {
                string[] parts = StringHelper.ParseList(logFilename, ':');
                if (parts.Length != 2)
                {
                    throw new Exception("custom log filename contains : but not in form <orig>:<new>");
                }
                string orig = Path.GetFileName(GetLogFile());
                logFilename = orig.Replace(parts[0], parts[1]);
            }

            string logFile = null;
            if (!string.IsNullOrEmpty(logDir))
            {
                //must be absolute or fa.ActivateOptions() will assume directory containing exe, not cwd 
                logDir = Path.GetFullPath(logDir);
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
                    var oldFile = fa.File != null ? new FileInfo(fa.File) : null;
                    if (string.IsNullOrEmpty(logFile) && !string.IsNullOrEmpty(logFilename))
                    {
                        if (oldFile != null)
                        {
                            logFile = Path.Combine(oldFile.DirectoryName, logFilename);
                        }
                        else
                        {
                            logFile = logFilename;
                        }
                    }
                    bool fileChanged = false;
                    if (!string.IsNullOrEmpty(logFile))
                    {
                        logFile = StringHelper.NormalizeSlashes(logFile);
                        fileChanged = fa.File == null || StringHelper.NormalizeSlashes(fa.File) != logFile;
                    }
                    if (fileChanged)
                    {
                        fa.File = logFile;
                    }
                    if (debug)
                    {
                        fa.Layout = new PatternLayout(DEBUG_PATTERN_LAYOUT);
                    }
                    if (fileChanged || debug)
                    {
                        fa.ActivateOptions();

                        if (fileChanged && oldFile.Exists)
                        {
                            //if (oldFile.Length == 0)
                            if (oldFile.Length < 5)
                            {
                                if (oldFile.Length > 0 && !quiet)
                                {
                                    Console.WriteLine(string.Format("WARN: deleting log file {0} " +
                                                                    "with only {1} bytes before changing filename",
                                                                    oldFile, oldFile.Length));
                                }

                                //the log filename has changed, but no logs have been written yet to the old file
                                //it seems that log4net creates the file (zero-length) before anything gets written
                                //in this case, just delete the old filename because most of the point of this whole
                                //thing is to try to avoid the filesystem getting littered up with a lot of different
                                //log files - and zero length log files are of pretty much no use anyway
                                PathHelper.DeleteWithRetry(oldFile.FullName);
                            }
                            else if (!quiet)
                            {
                                //the log filename has changed, but logs have already been written to the
                                //old filename - so leave it there
                                Console.WriteLine(string.Format("WARN: changing log file to {0}, " +
                                                                "old log file {1} not empty", fa.File, oldFile));
                            }
                        }
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

            //see comments at top of function
            //https://stackoverflow.com/a/49777426
            try
            {
                System.Runtime.Remoting.Messaging.CallContext
                    .FreeNamedDataSlot("log4net.Util.LogicalThreadContextProperties");
            }
            catch (Exception)
            {
                //ignore
            }
        }
    }
}

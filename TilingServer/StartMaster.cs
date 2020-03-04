using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;

namespace OPS.TilingServer
{
    [Verb("master", HelpText = "Runs a tiling workflow")]
    public class StartMasterOptions : PipelineCoreOptions
    {
        [Option(HelpText = "Start a worker in the same process (useful for debugging)", Default = false)]
        public bool StartWorker { get; set; }

        [Option(Default = false, HelpText = "Support alignment workflows")]
        public bool SupportAlignment { get; set; }
    }

    //https://github.jpl.nasa.gov/OnSight/Landform/issues/399
    //TODO this needs to get refactored as the master task for all Landform pipeline workflows
    //for now it only handles tiling workflows
    public class StartMaster : CloudPipeline
    {
        public const double STATUS_SPEW_SEC = 15;
        public const double LONG_TASK_WARN_SEC = 5 * 60;

        private StartMasterOptions options;
        private string queuePrefix;

        private Task workerTask = null;
        private Dictionary<string, PipelineStateMachine> stateMachines =
            new Dictionary<string, PipelineStateMachine>();

        public StartMaster(StartMasterOptions options) : this(options, options.SupportAlignment ? "" : "tiling") {}

        public StartMaster(StartMasterOptions options, string queuePrefix)
            : base(options, initAlignmentTables: options.SupportAlignment, queuePrefix: queuePrefix)
        {
            this.options = options;
            this.queuePrefix = queuePrefix;
        }

        public int Run()
        {
            DumpConfig();

            if (options.StartWorker)
            {
                workerTask = new Task(() => {
                        try
                        {
                            var opts = new StartWorkerOptions();
                            opts.OptionsFile = options.OptionsFile;
                            opts.ConfigDir = options.ConfigDir;
                            opts.ConfigFolder = options.ConfigFolder;
                            opts.LogFile = options.LogFile;
                            opts.LogDir = options.LogDir;
                            opts.TempDir = options.TempDir;
                            opts.Quiet = options.Quiet;
                            opts.Verbose = options.Verbose;
                            opts.Debug = options.Debug;
                            opts.ClearCache = options.ClearCache;
                            opts.StackTraces = options.StackTraces;
                            opts.SingleThreaded = options.SingleThreaded;
                            opts.UserMasksDirectory = options.UserMasksDirectory;
                            opts.UserMasksInverted = options.UserMasksInverted;
                            opts.SupportAlignment = options.SupportAlignment;
                            var worker = new StartWorker(opts, queuePrefix);
                            worker.EnableCleanupTempDir = false;
                            worker.Run();
                        }
                        catch (Exception e)
                        {
                            LogException(e, "error in worker task", stackTrace: true);
                        }
                    });
                workerTask.Start();
            }

            while (true)
            {
                try
                {
                    RunMaster();
                }
                catch (Exception e)
                {
                    LogException(e, "error in master task", stackTrace: true);
                    Thread.Sleep(2000); // limit spew just in case a misconfiguration is causing this error
                }
            }
#pragma warning disable 0162
            return 0;
#pragma warning restore 0162
        }

        const int FAILED_MESSAGE_TIMEOUT_SEC = 3;
        const int DEQUEUE_THROTTLE_MS = 50;
        private void RunMaster()
        {
            double lastSpew = UTCTime.Now();
            while (true)
            {
                //only take one message at a time when we are ready to process it
                var m = MasterQueue.DequeueOne<PipelineMessage>();
                Stopwatch sw = new Stopwatch();
                sw.Start();
                if (m != null)
                {
                    try
                    {
                        if (!stateMachines.ContainsKey(m.ProjectName))
                        {
                            ProjectType? pt = PipelineStateMachine.GetProjectType(this, m);
                            if (pt.HasValue)
                            {
                                var sm = PipelineStateMachine.CreateInstance(this, pt.Value, m.ProjectName);
                                stateMachines.Add(m.ProjectName, sm);
                            }
                            else
                            {
                                //this can happen if we get a duplicate DeleteProject message 
                                LogWarn("could not determine project type, discarding message: {0}", m.Info());
                            }
                        }

                        if (stateMachines.ContainsKey(m.ProjectName))
                        {
                            stateMachines[m.ProjectName].ProcessMessage(m);
                        }

                        MasterQueue.DeleteMessage(m);
                    }
                    catch (Exception e)
                    {
                        LogException(e, m.Info() + ": processing error", stackTrace: true);
                        try
                        {
                            //try to make the message available in our queue again soon
                            MasterQueue.UpdateTimeout(m, FAILED_MESSAGE_TIMEOUT_SEC);
                        }
                        catch (Exception e2)
                        {
                            LogError("{0}: error resetting visibility timeout ({1}): {2}",
                                     m.Info(), e2.GetType().FullName, e2.Message);
                        }
                    }
                    double totalSec = 0.001 * sw.ElapsedMilliseconds;
                    if (totalSec > MasterQueue.TimeoutSec)
                    {
                        LogError("{0}: took {1}s, but max processing time is {2}s",
                                 m.Info(), totalSec, MasterQueue.TimeoutSec);
                    }
                }
                int sleepMS = (int)(DEQUEUE_THROTTLE_MS - sw.ElapsedMilliseconds);
                if (sleepMS > 0)
                {
                    Thread.Sleep(sleepMS);
                }

                double now = UTCTime.Now();
                if (now - lastSpew > STATUS_SPEW_SEC * 1e3)
                {
                    lastSpew = now;
                    foreach (var sm in stateMachines.Values)
                    {
                        sm.SpewStatus(LONG_TASK_WARN_SEC);
                    }
                }
            }
        }
    }
}

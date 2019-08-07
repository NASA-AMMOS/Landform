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
    [Verb("startmaster", HelpText = "Runs a tiling workflow")]
    public class StartMasterOptions : PipelineCoreOptions
    {
        [Option(HelpText = "Start a worker in the same process (useful for debugging)", Default = false)]
        public bool StartWorker { get; set; }
    }

    //https://github.jpl.nasa.gov/OnSight/Landform/issues/399
    //TODO this needs to get refactored as the master task for all Landform pipeline workflows
    //for now it only handles tiling workflows
    public class StartMaster : CloudPipeline
    {
        private StartMasterOptions options;

        private Task workerTask = null;
        private Dictionary<string, PipelineStateMachine> stateMachines =
            new Dictionary<string, PipelineStateMachine>();

        public StartMaster(StartMasterOptions options) : base(options, queuePrefix: "tiling")
        {
            this.options = options;
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
                            opts.Quiet = options.Quiet;
                            opts.Verbose = options.Verbose;
                            opts.Debug = options.Debug;
                            opts.LogFile = options.LogFile;
                            opts.SingleThreaded = options.SingleThreaded;
                            var worker = new StartWorker(opts, "alignment");
                            worker.EnableCleanupTempDir = false;
                            worker.Run();
                        }
                        catch (Exception e)
                        {
                            LogError("error in worker task ({0}): {1}", e.GetType().FullName, e.Message);
                            LogError(e.StackTrace);
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
                    LogError("error in master task ({0}): {1}", e.GetType().FullName, e.Message);
                    LogError(e.StackTrace);
                    // Introduce a sleep here to limit debug spew just in case a misconfiguration is causing this error
                    Thread.Sleep(2000);  
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
            while (true)
            {
                //only take one message at a time when we are ready to process it
                var m = MasterQueue.DequeueOne();
                Stopwatch sw = new Stopwatch();
                sw.Start();
                if (m != null)
                {
                    try
                    {
                        if (!stateMachines.ContainsKey(m.ProjectName))
                        {
                            PipelineStateMachine.ProjectType? pt = PipelineStateMachine.GetProjectType(this, m);
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
                        LogError("{0}: processing error ({1}): {2}", m.Info(), e.GetType().FullName, e.Message);
                        LogError(e.StackTrace);

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
            }
        }
    }
}

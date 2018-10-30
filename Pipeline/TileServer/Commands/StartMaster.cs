using CommandLine;
using log4net;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

namespace OPS.Pipeline.TileServer
{
    [Verb("startmaster", HelpText = "Runs a tiling workflow")]
    public class StartMasterOptions : PipelineCoreOptions
    {
    }

    public class StartMaster : PipelineCore
    {
        private StartMasterOptions options;

        private TileServerCloud cloud;

        private Dictionary<string, PipelineStateMachine> projectNameToStateMachine =
            new Dictionary<string, PipelineStateMachine>();

        public StartMaster(StartMasterOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            TileServerConfig.Instance.Dump(Logger);

            cloud = new TileServerCloud(this);

            while (true)
            {
                try
                {
                    RunMaster();
                }
                catch (Exception e)
                {
                    Logger.ErrorFormat("error in master task ({0}): {1}", e.GetType().FullName, e.Message);
                    Logger.Error(e.StackTrace);
                    // Introduce a sleep here to limit debug spew just in case a misconfiguration is causing this error
                    Thread.Sleep(2000);  
                }
            }
#pragma warning disable 0162
            return 0;
#pragma warning restore 0162
        }

        const int FAILED_MESSAGE_TIMEOUT_SEC = 3;
        const int DEQUEUE_THROTTLE_MS = 500;
        private void RunMaster()
        {
            var masterQueue = cloud.MasterQueue;
            while (true)
            {
                //only take one message at a time when we are ready to process it
                var m = masterQueue.DequeueOne();
                if (m != null)
                {
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    try
                    {
                        bool projectDeleted = false;
                        if (!projectNameToStateMachine.ContainsKey(m.ProjectName))
                        {
                            string projectType = null;
                            if (m is CreateProjectMessage)
                            {
                                projectType = ((CreateProjectMessage)m).ProjectType;
                            }
                            else
                            {
                                TilingProject project = TilingProject.Find(this.DynamoContext, m.ProjectName);
                                if (project != null)
                                {
                                    projectType = project.ProjectType;
                                }
                                else if (m is DeleteProjectMessage)
                                {
                                    projectDeleted = true;
                                }
                            }
                            if (!projectDeleted)
                            {
                                PipelineStateMachine.ProjectType pt;
                                if (string.IsNullOrEmpty(projectType) ||
                                    !Enum.TryParse(projectType, /* ignoreCase */ true, out pt) ||
                                    !PipelineStateMachine.StateMachines.ContainsKey(pt))
                                {
                                    throw new Exception("could not create state machine for project " + m.ProjectName +
                                                        " of type \"" + projectType + "\"");
                                }
                                
                                var smt = PipelineStateMachine.StateMachines[pt];
                                var sm = (PipelineStateMachine)Activator.CreateInstance(smt, this, cloud.WorkerQueue,
                                                                                        m.ProjectName);
                                projectNameToStateMachine.Add(m.ProjectName, sm);
                            }
                        }

                        if (!projectDeleted)
                        {
                            projectNameToStateMachine[m.ProjectName].ProcessMessage(m);
                        }

                        masterQueue.DeleteMessage(m);
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorFormat("{0}: processing error ({1}): {2}",
                                           m.Info(), e.GetType().FullName, e.Message);
                        Logger.Error(e.StackTrace);

                        try
                        {
                            //try to make the message available in our queue again soon
                            masterQueue.UpdateTimeout(m, FAILED_MESSAGE_TIMEOUT_SEC);
                        }
                        catch (Exception e2)
                        {
                            Logger.ErrorFormat("{0}: error resetting visibility timeout ({1}): {2}",
                                               m.Info(), e2.GetType().FullName, e2.Message);
                        }
                    }
                    double totalSec = 0.001 * sw.ElapsedMilliseconds;
                    if (totalSec > masterQueue.TimeoutSec)
                    {
                        Logger.ErrorFormat("{0}: took {1}s, but max processing time is {2}s",
                                           m.Info(), totalSec, masterQueue.TimeoutSec);
                    }
                }
                Thread.Sleep(DEQUEUE_THROTTLE_MS);
            }
        }
    }
}

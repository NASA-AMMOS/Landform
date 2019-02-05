using CommandLine;
using log4net;
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

    //https://github.jpl.nasa.gov/ProtoSpace/ps-pipeline/issues/159
    //TODO this needs to get refactored as the master task for all Landform pipeline workflows
    //for now it only handles tiling workflows
    public class StartMaster : CloudPipeline
    {
        private StartMasterOptions options;

        private Dictionary<string, PipelineStateMachine> projectNameToStateMachine =
            new Dictionary<string, PipelineStateMachine>();

        public StartMaster(StartMasterOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            DumpConfig();

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
                                TilingProject project = TilingProject.Find(this, m.ProjectName);
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
                                var sm = (PipelineStateMachine)Activator.CreateInstance(smt, this, m.ProjectName);
                                projectNameToStateMachine.Add(m.ProjectName, sm);
                            }
                        }

                        if (!projectDeleted)
                        {
                            projectNameToStateMachine[m.ProjectName].ProcessMessage(m);
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

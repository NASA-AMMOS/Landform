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

        private Dictionary<string,Type> registeredStateMachines = new Dictionary<string, Type>();
        private Dictionary<string, PipelineStateMachine> projectNameToStateMachine =
            new Dictionary<string, PipelineStateMachine>();

        public StartMaster(StartMasterOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
            RegisterStateMachine(GenericTilingStateMachine.ProjectType(), typeof(GenericTilingStateMachine));
            RegisterStateMachine(MSLStateMachine.ProjectType(), typeof(MSLStateMachine));
        }

        private void RegisterStateMachine(string projectType, Type stateMachine)
        {
            if (registeredStateMachines.ContainsKey(projectType))
            {
                throw new ArgumentException("state machine for project type " + projectType + " already registered");
            }
            registeredStateMachines.Add(projectType, stateMachine);
        }

        public int Run()
        {
            TileServerConfig.Instance.Dump(Logger);
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

        private void RunMaster()
        {
            var cloud = new TileServerCloud(this);
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
                        if (!projectNameToStateMachine.ContainsKey(m.ProjectName))
                        {
                            string projectType = null;
                            if (m.GetType() == typeof(CreateProjectMessage))
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
                            }
                            if (string.IsNullOrEmpty(projectType) || !registeredStateMachines.ContainsKey(projectType))
                            {
                                throw new Exception("could not create state machine for project " + m.ProjectName +
                                                    " of type \"" + projectType + "\"");
                            }

                            var smt = registeredStateMachines[projectType];
                            var sm = (PipelineStateMachine)Activator.CreateInstance(smt, this, cloud.WorkerQueue,
                                                                                    m.ProjectName);
                            projectNameToStateMachine.Add(m.ProjectName, sm);
                        }

                        projectNameToStateMachine[m.ProjectName].ProcessMessage(m);

                        masterQueue.DeleteMessage(m);
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorFormat("{0}: processing error ({1}): {2}",
                                           m.Info(), e.GetType().FullName, e.Message);
                        Logger.Error(e.StackTrace);
                    }
                    double totalSec = 0.001 * sw.ElapsedMilliseconds;
                    if (totalSec > masterQueue.TimeoutSec)
                    {
                        Logger.ErrorFormat("{0}: took {1}s, but max processing time is {2}s",
                                           m.Info(), totalSec, masterQueue.TimeoutSec);
                    }
                }
                Thread.Sleep(100); //throttle Dequeue()
            }
        }
    }
}

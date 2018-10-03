using CommandLine;
using log4net;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace OPS.Pipeline.TileServer
{
    [Verb("startmaster", HelpText = "Runs a tiling workflow")]
    public class StartMasterOptions
    {

    }

    public class StartMaster : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(StartMaster));

        StartMasterOptions options;

        private static Dictionary<string,Type> registeredStateMachines = new Dictionary<string, Type>();
        private static ConcurrentDictionary<string, PipelineStateMachine> projectNameToStateMachine = new ConcurrentDictionary<string, PipelineStateMachine>();

        public StartMaster(StartMasterOptions options) : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;

            RegisterStateMachine(GenericTilingStateMachine.ProjectType(), typeof(GenericTilingStateMachine));
            RegisterStateMachine(MSLStateMachine.ProjectType(), typeof(MSLStateMachine));
        }

        public int Run()
        {
            TileServerConfig.Instance.Dump(logger);
            while (true)
            {
                try
                {
                    RunMaster();
                }
                catch (Exception e)
                {
                    logger.Error("error in master task: " + e.Message);
                    logger.Error(e.StackTrace);
                    // Introduce a sleep here to limit debug spew just in case a misconfiguration is causing this error
                    Thread.Sleep(2000);  
                }
            }
            return 0;
        }

        void RunMaster()
        {
            var cloud = new TileServerCloud(this);

            while (true)
            {
                var messages = cloud.MasterQueue.Dequeue(TilingQueue.MAX_MESSAGES_PER_DEQUEUE);
                foreach (var m in messages) 
                {
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
                            projectNameToStateMachine.TryAdd(m.ProjectName, sm);
                        }

                        bool processed = projectNameToStateMachine[m.ProjectName].ProcessMessage(m);
                        if (!processed)
                        {
                            logger.Error("unknown message type: " + m.GetType());
                        }

                        cloud.MasterQueue.Delete(m);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e.Message);
                        logger.Error(e.StackTrace);
                    }
                }
            }
        }

        private void RegisterStateMachine(string projectType, Type stateMachine)
        {
            if (registeredStateMachines.ContainsKey(projectType))
            {
                throw new ArgumentException("state machine for project type " + projectType + " already registered");
            }
            registeredStateMachines.Add(projectType, stateMachine);
        }
    }
}

using CommandLine;
using log4net;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace OPS.Pipeline.TileServer
{
    [Verb("startmaster", HelpText = "Runs a tiling workflow")]
    public class StartMasterOptions
    {
        [Option(HelpText = "Run a single master on the main thread", Default = true)]
        public bool SingleThreaded { get; set; }
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

            if (options.SingleThreaded)
            {
                logger.Info("TilingServer master single-threaded");
                RunMaster();
            }
            else
            {
                logger.Info("TilingServer master multi-threaded (EXPERIMENTAL)");

                Task[] tasks = new Task[Environment.ProcessorCount];
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = Task.Run(() => {
                            try
                            {
                                RunMaster();
                            }
                            catch (Exception e)
                            {
                                logger.Error("error in master task " + i + ": " + e.Message);
                                logger.Error(e.StackTrace);
                            }
                        });
                }
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i].Wait();
                }
            }
            return 0;
        }

        void RunMaster()
        {
            var cloud = new TileServerCloud(this);
            var workerQueue = cloud.WorkerQueue;
            var completionQueue = cloud.CompletionQueue;

            while (true)
            {
                var messages = completionQueue.Deque(TilingQueue.MAX_MESSAGES_PER_DEQUEUE);
                foreach (var m in messages) 
                {
                    if (!projectNameToStateMachine.ContainsKey(m.ProjectName))
                    {
                        projectNameToStateMachine.TryAdd(m.ProjectName, CreateStateMachine(workerQueue, m.ProjectName));
                    }
                    
                    try
                    {
                        projectNameToStateMachine[m.ProjectName].ProcessCompletedMessage(m);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e.Message);
                        logger.Error(e.StackTrace);
                    }
                    completionQueue.Delete(m);
                }
            }
        }

        private void RegisterStateMachine(string projectType, Type stateMachine)
        {
            if (registeredStateMachines.ContainsKey(projectType))
                throw new ArgumentException("projectType already mapped");
         
            registeredStateMachines.Add(projectType, stateMachine);
            
        }

        private PipelineStateMachine CreateStateMachine(TilingQueue workerQueue, string projectName)
        {
            TilingProject project = TilingProject.Find(this.DynamoContext, projectName);
            return (PipelineStateMachine)Activator.CreateInstance(registeredStateMachines[project.ProjectType], this, workerQueue, projectName);
         }
    }
}

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
        [Option(HelpText = "Run a single master on the main thread for debugging", Default = false)]
        public bool SingleThreaded { get; set; }
    }

    public class StartMaster : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(StartMaster));

        StartMasterOptions options;

        private static Dictionary<string,Type> registeredStateMachines = new Dictionary<string, Type>();
        private static Dictionary<string, PipelineStateMachine> projectNameToStateMachine = new Dictionary<string, PipelineStateMachine>();

        public StartMaster(StartMasterOptions options) : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;

            RegisterStateMachine("GenericTiling", typeof(GenericTilingStateMachine));
        }

        public int Run()
        {
            if (options.SingleThreaded)
            {
                RunMaster();
            }
            else
            {
                Task[] tasks = new Task[Environment.ProcessorCount];
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = Task.Run(() => RunMaster());
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
                    lock (projectNameToStateMachine)
                    {
                        if (!projectNameToStateMachine.ContainsKey(m.ProjectName))
                        {
                            projectNameToStateMachine.Add(m.ProjectName, CreateStateMachine(workerQueue, m.ProjectName));
                        }
                    }

                    string s = JsonHelper.ToJson(m);
                    try
                    {
                        projectNameToStateMachine[m.ProjectName].ProcessMessage(m);
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

        private string GetProjectType(string projectName)
        {
            TilingProject project = TilingProject.Find(this.DynamoContext, projectName);
            return project.ProjectType;
        }

        private void RegisterStateMachine(string projectType, Type stateMachine)
        {
            if (registeredStateMachines.ContainsKey(projectType))
                throw new ArgumentException("projectType already mapped to different state machine");
         
            registeredStateMachines.Add(projectType, stateMachine);
            
        }

        private PipelineStateMachine CreateStateMachine(TilingQueue workerQueue, string projectName)
        {
            return (PipelineStateMachine)Activator.CreateInstance(registeredStateMachines[GetProjectType(projectName)], this, workerQueue, projectName);
         }
    }
}

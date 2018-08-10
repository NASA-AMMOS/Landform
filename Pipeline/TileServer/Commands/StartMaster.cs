using CommandLine;
using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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

        public StartMaster(StartMasterOptions options) : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }


        class ProjectCache
        {
            HashSet<string> ids;
            Dictionary<string, List<string>> dependedOnBy;
            Dictionary<string, List<string>> dependsOn;
            HashSet<string> completed;
            HashSet<string> enqued;
            public string RootId { get; private set; }

            PipelineCore pipeline;
            TilingProject project;
            Object lockObj = new object();


            static Dictionary<string, ProjectCache> projects = new Dictionary<string, ProjectCache>();
            static object globalLock = new object();

            public static ProjectCache GetCacheForProject(PipelineCore pipeline, string projectName)
            {
                lock (globalLock)
                {
                    if (!projects.ContainsKey(projectName))
                    {
                        var p = TilingProject.Find(pipeline.DynamoContext, projectName);
                        projects.Add(p.Name, new ProjectCache(pipeline, p));
                    }
                    return projects[projectName];
                }
            }

            public static void ClearCacheForProject(string projectName)
            {
                lock (globalLock)
                {
                    if (projects.ContainsKey(projectName))
                    {
                        projects.Remove(projectName);
                    }
                }
            }

            public ProjectCache(PipelineCore pipeline, TilingProject project)
            {
                this.pipeline = pipeline;
                this.project = project;
                Init();
            }

            void Init()
            {
                lock (lockObj)
                {
                    ids = new HashSet<string>();
                    dependedOnBy = new Dictionary<string, List<string>>();
                    dependsOn = new Dictionary<string, List<string>>();
                    completed = new HashSet<string>();
                    enqued = new HashSet<string>();
                    var list = TilingNode.Find(this.pipeline.DynamoContext, this.project).ToList();
                    foreach (var n in list)
                    {
                        ids.Add(n.Id);

                        if(n.DependedOnBy == null)
                        {
                            n.DependedOnBy = new List<string>();
                        }
                        if(n.DependsOn == null)
                        {
                            n.DependsOn = new List<string>();
                        }
                        dependedOnBy.Add(n.Id, n.DependedOnBy);
                        dependsOn.Add(n.Id, n.DependsOn);
                        if (n.MeshUrl != null)
                        {
                            completed.Add(n.Id);
                        }
                        if (n.ParentId == null)
                        {
                            RootId = n.Id;
                        }
                    }
                }
            }

            public void MarkEnqued(string id)
            {
                lock (lockObj)
                {
                    enqued.Add(id);
                }
            }

            public void MarkDone(string id)
            {
                lock (lockObj)
                {
                    completed.Add(id);
                }
            }

            public bool AlreadyProcessed(string id)
            {
                lock (lockObj)
                {
                    return enqued.Contains(id) || completed.Contains(id);
                }
            }

            public bool ShouldRun(string id)
            {
                lock (lockObj)
                {
                    // Don't run if we node is done or enqueued
                    if (AlreadyProcessed(id))
                    {
                        return false;
                    }
                    // Only run if all nodes id depends on are completed
                    return dependsOn[id].All(i => completed.Contains(i));
                }
            }

            public List<string> GetDependentTilesToRun(string id)
            {
                lock (lockObj)
                {
                    // Find all nodes that depend on id and return only those that are ready to run
                    return dependedOnBy[id].Where(i => ShouldRun(i)).ToList();
                }
            }

            public List<string> GetTilesReadyToRun()
            {
                lock (lockObj)
                {
                    var ready = ids.Where(i => ShouldRun(i)).ToList();
                    return ready;
                }
            }
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
                    string s = JsonHelper.ToJson(m);          
                    try
                    {
                        // process
                        if (m.GetType() == typeof(DefineTilesMessage))
                        {
                            // This is the first message that happens when we trigger a new run
                            // Force a clearing of the cache just to avoid stale data form a previous run
                            ProjectCache.ClearCacheForProject(m.ProjectName);
                            ProjectCache.GetCacheForProject(this, m.ProjectName);

                            logger.Info("DefineTiles project:" + m.ProjectName);
                            TilingProject project = TilingProject.Find(this.DynamoContext, m.ProjectName);
                            ChunkInputs(workerQueue, project);
                        }
                        else if (m.GetType() == typeof(ChunkInputMessage))
                        {
                            logger.Info("ChunkInput project:" + m.ProjectName + " input:" + ((ChunkInputMessage)m).InputName);
                            TilingProject project = TilingProject.Find(this.DynamoContext, m.ProjectName);
                            var inputs = TilingInput.Find(this.DynamoContext, project);
                            bool allChunked = inputs.All(i => i.Chunked);
                            if (allChunked)
                            {
                                BuildLeaves(workerQueue, project);
                            }
                        }
                        else if (m.GetType() == typeof(TileCompletedMessage))
                        {
                            var id = ((TileCompletedMessage)m).TileId;
                            logger.Info("TileCompleted project:" + m.ProjectName + " tile:" + id);
                            var pc = ProjectCache.GetCacheForProject(this, m.ProjectName);
                            pc.MarkDone(id);
                            if (id == pc.RootId)
                            {
                                var tilesetJob = new BuildTilesetJsonMessage(m.ProjectName);
                                workerQueue.Enqueue(tilesetJob);
                            }
                            else
                            {
                                foreach (var pid in pc.GetDependentTilesToRun(id))
                                {
                                    logger.Info("EnquingParent " + m.ProjectName + " tile:" + pid);
                                    var parentJob = new BuildParentsMessage(m.ProjectName, pid);
                                    workerQueue.Enqueue(parentJob);
                                    pc.MarkEnqued(pid);
                                }
                            }
                        }
                        else if (m.GetType() == typeof(BuildTilesetJsonMessage))
                        {
                            logger.Info("TilesetComplete " + m.ProjectName);
                        }
                        else
                        {
                            logger.Info("Unknown message type: " + m.GetType());
                        }
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

        void ChunkInputs(TilingQueue queue, TilingProject project)
        {
            var inputs = TilingInput.Find(this.DynamoContext, project);
            foreach (var input in inputs)
            {
                queue.Enqueue(new ChunkInputMessage(project.Name, input.Name));
            }
        }

        void BuildLeaves(TilingQueue queue, TilingProject project)
        {
            logger.Info("Build Leaves");
            SceneNode root = TilingNode.BuildTreeFromDatabase(this.DynamoContext, project);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);
            var pc = ProjectCache.GetCacheForProject(this, project.Name);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildLeavesMessage(project.Name, group.Select(n => n.Name).ToList());
                queue.Enqueue(leafJob);
                foreach (var leaf in group)
                {
                    pc.MarkEnqued(leaf.Name);
                }
            }
        }


        Queue<SceneNode> GroupSceneNodesIntoJobs(SceneNode node, List<List<SceneNode>> outputGroups, int nodesPerGroup = 32)
        {
            var result = new Queue<SceneNode>();
            if (node.IsLeaf)
            {
                result.Enqueue(node);
                return result;
            }
            foreach (var c in node.Children)
            {
                var tmp = GroupSceneNodesIntoJobs(c, outputGroups, nodesPerGroup);
                foreach (var e in tmp)
                {
                    result.Enqueue(e);
                }
            }
            while (result.Count > nodesPerGroup)
            {
                List<SceneNode> outputGroup = new List<SceneNode>();
                for (int i = 0; i < nodesPerGroup; i++)
                {
                    outputGroup.Add(result.Dequeue());
                }
                outputGroups.Add(outputGroup);
            }
            if (node.Parent == null && result.Count != 0)
            {
                outputGroups.Add(result.ToList());
                result.Clear();
            }
            return result;
        }

    }
}

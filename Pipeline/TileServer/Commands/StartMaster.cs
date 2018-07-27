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
        [Value(0, Required = true, HelpText = "Dynamo DB prefix")]
        public string DynamoDBPrefix { get; set; }

        [Option(HelpText = "AWS profile to use", Default = "default")]
        public string Profile { get; set; }
    }

    public class StartMaster : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(StartMaster));

        StartMasterOptions options;
        public StartMaster(StartMasterOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }       

        public int Run()
        {
            var cloud = new TileServerCloud(options.DynamoDBPrefix, this);
            var workerQueue = cloud.WorkerQueue;
            var completionQueue = cloud.CompletionQueue;

            Dictionary<string, DateTime> recentlyProcessed = new Dictionary<string, DateTime>();
            
            while (true)
            {
                var messages = completionQueue.Deque(10);
                foreach(var m in messages)
                {
                    string s = JsonHelper.ToJson(m);
                    if (recentlyProcessed.Count > 5000)
                    {
                        recentlyProcessed.Clear();
                    }
                    if (!recentlyProcessed.ContainsKey(s))
                    {
                        recentlyProcessed.Add(s, DateTime.UtcNow);
                    }
                    else
                    {
                        if(DateTime.UtcNow - recentlyProcessed[s] < new TimeSpan(0, 5, 0))
                        {
                            logger.Info("Skipping identical message");
                            completionQueue.Delete(m);
                            continue;
                        }
                        else
                        {
                            recentlyProcessed[s] = DateTime.UtcNow;
                        }
                    } 
                    try
                    {
                        // process
                        TilingProject project = TilingProject.Find(this.DynamoContext, m.ProjectName);
                        if (m.GetType() == typeof(DefineTilesMessage))
                        {
                            logger.Info("DefineTiles project:" + m.ProjectName);
                            ChunkInputs(workerQueue, project);
                        }
                        else if (m.GetType() == typeof(ChunkInputMessage))
                        {
                            logger.Info("ChunkInput project:" + m.ProjectName + " input:" + ((ChunkInputMessage)m).InputName);

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
                            var node = TilingNode.Find(this.DynamoContext, project, id);
                            if (node.ParentId == null)
                            {
                                var tilesetJob = new BuildTilesetJsonMessage(project.Name);
                                workerQueue.Enqueue(tilesetJob);
                            }
                            else
                            {
                                // For each node that depends on this one
                                foreach (var pid in node.DependedOnBy)
                                {
                                    var pnode = TilingNode.Find(this.DynamoContext, project, pid);
                                    // Check to see if all of it's dependencies have been completed
                                    bool allDependenciesDone = true;
                                    foreach (var d in pnode.DependsOn)
                                    {
                                        var dnode = TilingNode.Find(this.DynamoContext, project, d);
                                        if (dnode.MeshUrl == null)
                                        {
                                            allDependenciesDone = false;
                                            break;
                                        }
                                    }
                                    if (allDependenciesDone)
                                    {
                                        logger.Info("EnquingParent " + project.Name + " tile:" + pid);
                                        var parentJob = new BuildParentsMessage(project.Name, pid);
                                        workerQueue.Enqueue(parentJob);
                                    }

                                }
                            }
                        }
                        else if (m.GetType() == typeof(BuildTilesetJsonMessage))
                        {
                            logger.Info("TilesetComplete " + project.Name);

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
            return 0;
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
            foreach (var group in leafGroups)
            {
                var leafJob = new BuildLeavesMessage(project.Name, group.Select(n => n.Name).ToList());
                queue.Enqueue(leafJob);
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

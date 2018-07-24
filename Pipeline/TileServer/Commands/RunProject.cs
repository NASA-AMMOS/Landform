using CommandLine;
using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OPS.Pipeline;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.TileServer
{
    [Verb("runproject", HelpText = "Runs a tiling workflow")]

    public class RunProjectOptions
    {
        [Value(0, Required = true, HelpText = "Dynamo DB prefix")]
        public string DynamoDBPrefix { get; set; }

        [Value(1, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Option(HelpText = "AWS profile to use", Default = "default")]
        public string Profile { get; set; }
    }

    public class RunProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(RunProject));

        RunProjectOptions options;
        public RunProject(RunProjectOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }

        class TileDependencyMapping
        {
            Dictionary<string, HashSet<string>> dependsOn = new Dictionary<string, HashSet<string>>();
            Dictionary<string, HashSet<string>> dependedOnBy = new Dictionary<string, HashSet<string>>();

            public HashSet<string> RequestedTiles = new HashSet<string>();

            public HashSet<string> DependsOn(string id)
            {
                if (!dependsOn.ContainsKey(id))
                {
                    return new HashSet<string>();
                }
                return dependsOn[id];
            }

            public HashSet<string> DependedOnBy(string id)
            {
                if (!dependedOnBy.ContainsKey(id))
                {
                    return new HashSet<string>();
                }
                return dependedOnBy[id];
            }

            public void AddDependency(string node, string dependency)
            {
                if(!dependsOn.ContainsKey(node))
                {
                    dependsOn.Add(node, new HashSet<string>());
                }
                dependsOn[node].Add(dependency);
                if (!dependedOnBy.ContainsKey(dependency))
                {
                    dependedOnBy.Add(dependency, new HashSet<string>());
                }
                dependedOnBy[dependency].Add(node);
            }
        }

        public int Run()
        {
            var workerQueue = TileServerCloud.WorkerQueue(options.DynamoDBPrefix, options.Profile);
            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            var completionQueue = TileServerCloud.CompletionQueue(options.DynamoDBPrefix, options.Profile, project);

            DefineTiles(workerQueue);
            var dependencies = new TileDependencyMapping();
            while (true)
            {
                var m = completionQueue.Deque();
                if (m != null)
                {
                    try
                    {
                        // process
                        if (m.GetType() == typeof(DefineTilesMessage))
                        {
                            SceneNode root = TilingNode.BuildTreeFromDatabase(this.DynamoContext, project);
                            foreach (var node in root.DepthFirstTraverse())
                            {
                                if (!node.IsLeaf)
                                {
                                    var dependsOn = node.FindNodesRequiredForParent(root);
                                    foreach (var d in dependsOn)
                                    {
                                        dependencies.AddDependency(node.Name, d.Name);
                                    }
                                }
                            }
                            ChunkInputs(workerQueue);

                        }
                        else if (m.GetType() == typeof(ChunkInputMessage))
                        {
                            var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
                            var inputs = TilingInput.Find(this.DynamoContext, p);
                            bool allChunked = inputs.All(i => i.Chunked);
                            if (allChunked)
                            {
                                BuildLeaves(workerQueue);
                            }
                        }
                        else if (m.GetType() == typeof(TileCompletedMessage))
                        {
                            var id = ((TileCompletedMessage)m).TileId;
                            var node = TilingNode.Find(this.DynamoContext, project, id);
                            if(node.ParentId == null)
                            {
                                break;
                            }
                            //dependencies.Resolved.Add(id);
                            var parentIds = dependencies.DependedOnBy(id);
                            foreach (var pid in parentIds)
                            {
                                var pnode = TilingNode.Find(this.DynamoContext, project, pid);
                                if(/*pnode.MeshUrl != null || */dependencies.RequestedTiles.Contains(pid))
                                {
                                    // Don't enque a parent more than once
                                    continue;
                                }
                                bool allDependenciesDone = true;
                                foreach (var d in dependencies.DependsOn(pid))
                                {
                                    var dnode = TilingNode.Find(this.DynamoContext, project, d);
                                    if (dnode.MeshUrl == null)
                                    {
                                        allDependenciesDone = false;
                                        break;
                                    }
                                }
                                if(allDependenciesDone)
                                {
                                    logger.Info("Enquing parent job");
                                    dependencies.RequestedTiles.Add(pid);
                                    var parentJob = new BuildParentsMessage(project.Name, pid, dependencies.DependsOn(pid).ToList());
                                    workerQueue.Enqueue(parentJob);
                                }
                            }
                        }
                        else
                        {
                            logger.Info("Unknown message type");
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

        void DefineTiles(TilingQueue queue)
        {
            logger.Info("Define tiles");
            queue.Enqueue(new DefineTilesMessage(options.ProjectName));
            //WaitForTilesToBeDefined();
        }

        void ChunkInputs(TilingQueue queue)
        {
            logger.Info("Chunk inputs");
            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            var inputs = TilingInput.Find(this.DynamoContext, project);
            foreach (var input in inputs)
            {
                queue.Enqueue(new ChunkInputMessage(options.ProjectName, input.Name));
            }
            //WaitForInputsToChunk();
        }

        void BuildLeaves(TilingQueue queue)
        {
            logger.Info("Build Leaves");
            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            SceneNode root = TilingNode.BuildTreeFromDatabase(this.DynamoContext, project);

            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildLeavesMessage(project.Name, group.Select(n => n.Name).ToList());
                queue.Enqueue(leafJob);
            }
            //WaitForLeaves();
        }

        //void BuildParents(TilingQueue queue, int nodesPerJob = 32)
        //{
        //    logger.Info("Build Parents");
        //    var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
        //    SceneNode root = TilingNode.BuildTreeFromDatabase(this.DynamoContext, project);            
        //    foreach (var depthGroup in root.GetReverseDepthGroups())
        //    {
        //        int i = 0;
        //        var nodes = depthGroup.ToList();
        //        while(i < nodes.Count())
        //        {
        //            Dictionary<string, List<string>> ids = new Dictionary<string, List<string>>();
        //            int end = i + nodesPerJob;
        //            for (; i < end && i < depthGroup.Count(); i++)
        //            {
        //                var requiredNodes = nodes[i].FindNodesRequiredForParent(root).Select(n => n.Name);
        //                ids.Add(nodes[i].Name, requiredNodes.ToList());
        //            }
        //            var parentJob  = new BuildParentsMessage(project.Name, ids);
        //            queue.Enqueue(parentJob);
        //        }
        //    }
        //}

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

        //void WaitForTilesToBeDefined()
        //{
        //    while(true)
        //    {
        //        var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
        //        if(!p.TilesDefined)
        //        {
        //            Thread.Sleep(1000);
        //        }
        //        else
        //        {
        //            break;
        //        }
        //    }
        //}

        //void WaitForInputsToChunk()
        //{
        //    while (true)
        //    {
        //        var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
        //        var inputs = TilingInput.Find(this.DynamoContext, p);
        //        bool allChunked = inputs.All(i => i.Chunked);
        //        if (!allChunked)
        //        {
        //            Thread.Sleep(2000);
        //        }
        //        else
        //        {
        //            break;
        //        }                
        //    }
        //}

        //void WaitForLeaves()
        //{
        //    while (true)
        //    {
        //        var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
        //        var nodes = TilingNode.Find(this.DynamoContext, p);
        //        bool allMeshed = nodes.All(n => !n.IsLeaf() || n.MeshUrl != null);
        //        if (!allMeshed)
        //        {
        //            Thread.Sleep(10000);
        //        }
        //        else
        //        {
        //            break;
        //        }
        //    }
        //}
    }


}

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

        public int Run()
        {
            var queue = new TilingQueue(options.DynamoDBPrefix, options.Profile);
            DefineTiles(queue);
            ChunkInputs(queue);
            BuildLeaves(queue);


            


            return 0;
        }

        void DefineTiles(TilingQueue queue)
        {
            logger.Info("Define tiles");
            queue.Enqueue(new DefineTilesMessage(options.ProjectName));
            WaitForTilesToBeDefined();
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
            WaitForInputsToChunk();
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

        void WaitForTilesToBeDefined()
        {
            while(true)
            {
                var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
                if(!p.TilesDefined)
                {
                    Thread.Sleep(1000);
                }
                else
                {
                    break;
                }
            }
        }

        void WaitForInputsToChunk()
        {
            while (true)
            {
                var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
                var inputs = TilingInput.Find(this.DynamoContext, p);
                bool allChunked = inputs.All(i => i.Chunked);
                if (!allChunked)
                {
                    Thread.Sleep(2000);
                }
                else
                {
                    break;
                }                
            }
        }
    }


}

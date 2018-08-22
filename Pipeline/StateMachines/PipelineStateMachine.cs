using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Pipeline.TileServer;
using System.Collections.Generic;
using System.Linq;

namespace OPS.Pipeline.TileServer
{
    abstract class PipelineStateMachine
    {
        protected static ILog logger = LogManager.GetLogger(typeof(PipelineStateMachine));

        protected PipelineCore pipeline;
        protected TilingQueue workerQueue;
        protected ProjectCache projectCache;

        public PipelineStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
        {
            this.pipeline = pipeline;
            this.workerQueue = workerQueue;
            this.projectCache = new ProjectCache(pipeline, projectName);
        }

        public static PipelineStateMachine CreateStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectType, string projectName)
        {
            switch (projectType)
            {
                case "MSL":
                    return new MSLStateMachine(pipeline, workerQueue, projectName);
                case "GenericMesh":
                    return new GenericMeshStateMachine(pipeline, workerQueue, projectName);
                default:
                    throw new System.NotImplementedException("unknown state machine type: " + projectType);
            }
        }

        abstract public void ProcessMessage(TilingQueueMessage m);

        // *** common helpers for all existing pipelines *** //

        protected void ChunkInputs(TilingProject project)
        {
            var inputs = TilingInput.Find(pipeline.DynamoContext, project);
            foreach (var input in inputs)
            {
                workerQueue.Enqueue(new ChunkInputMessage(project.Name, input.Name));
            }
        }

        protected void BuildLeaves(TilingProject project)
        {
            logger.Info("Build Leaves");
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildLeavesMessage(project.Name, group.Select(n => n.Name).ToList());
                workerQueue.Enqueue(leafJob);
                foreach (var leaf in group)
                {
                    this.projectCache.MarkEnqued(leaf.Name);
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

    // *** common messages for all existing pipelines *** //

    public class TileCompletedMessage : TilingQueueMessage
    {
        public string TileId;

        public TileCompletedMessage(string projectName, string id) : base(projectName)
        {
            this.TileId = id;
        }
    }

    public class BuildLeavesMessage : TilingQueueMessage
    {
        public List<string> TileIds { get; set; }

        public BuildLeavesMessage() { }

        public BuildLeavesMessage(string projectName, List<string> tileIds) : base(projectName)
        {
            this.TileIds = tileIds;
        }
    }
}
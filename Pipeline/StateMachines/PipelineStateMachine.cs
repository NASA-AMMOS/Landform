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
        private static ILog logger = LogManager.GetLogger(typeof(PipelineStateMachine));

        protected PipelineCore pipeline;
        protected TilingQueue workerQueue;
        protected ProjectCache projectCache;

        public PipelineStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
        {
            this.pipeline = pipeline;
            this.workerQueue = workerQueue;
            this.projectCache = new ProjectCache(pipeline, projectName);
        }
       
        abstract public void ProcessMessage(TilingQueueMessage m);
        
        // shared functionality for all current pipelines

        protected void ChunkInputs(TilingProject project)
        {
            var inputs = TilingInput.Find(pipeline.DynamoContext, project);
            foreach (var input in inputs)
            {
                workerQueue.Enqueue(new ChunkInputMessage(project.Name, input.Name));
            }
        }

        protected Queue<SceneNode> GroupSceneNodesIntoJobs(SceneNode node, List<List<SceneNode>> outputGroups, int nodesPerGroup = 32)
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

    // shared messages for all current pipelines

    public class RunProjectMessage : TilingQueueMessage
    {
        public RunProjectMessage() { }

        public RunProjectMessage(string projectName) : base(projectName)
        {
        }
    }

    public class TileCompletedMessage : TilingQueueMessage
    {
        public string TileId;

        public TileCompletedMessage(string projectName, string id) : base(projectName)
        {
            this.TileId = id;
        }
    }


}
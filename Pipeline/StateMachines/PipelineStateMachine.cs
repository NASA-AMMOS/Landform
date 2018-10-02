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
        protected string projectName;

        public PipelineStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
        {
            this.pipeline = pipeline;
            this.workerQueue = workerQueue;
            this.projectName = projectName;
            this.projectCache = new ProjectCache(pipeline, projectName);
        }
       
        abstract public void ProcessCompletedMessage(TilingQueueMessage m);
        
        // shared functionality for all current pipelines

        protected void ChunkInputs()
        {
            var inputs = TilingInput.Find(pipeline.DynamoContext, projectName);
            foreach (var input in inputs)
            {
                logger.Info("chunking input " + input.Name + " in " + projectName);
                workerQueue.Enqueue(new ChunkInputMessage(projectName, input.Name));
            }
        }

        protected bool InputChunked(string inputName)
        {
            logger.Info("input " + inputName + " chunked in " + projectName);
            var inputs = TilingInput.Find(pipeline.DynamoContext, projectName);
            bool allChunked = inputs.All(i => i.Chunked);
            if (allChunked)
            {
                logger.Info("all inputs chunked in " + projectName);
            }
            return allChunked;
        }

        protected void TileCompleted(string tileId)
        {
            logger.Info("tile " + tileId + " completed in " + projectName);
            
            projectCache.MarkDone(tileId);
            if (tileId == projectCache.RootId)
            {
                logger.Info("building tileset JSON in " + projectName);
                workerQueue.Enqueue(new BuildTilesetJsonMessage(projectName));
            }
            else
            {
                foreach (var pid in projectCache.GetDependentTilesToRun(tileId))
                {
                    logger.Info("enquing parent " + pid + " in " + projectName);
                    workerQueue.Enqueue(new BuildParentMessage(projectName, pid));
                    projectCache.MarkEnqued(pid);
                }
            }
        }

        protected void TilesetCompleted()
        {
            var project = TilingProject.Find(pipeline.DynamoContext, projectName);
            project.FinishedRunning = true;
            project.Save(pipeline.DynamoContext);
            logger.Info(projectName + " finished running");
        }

        protected Queue<SceneNode> GroupSceneNodesIntoJobs(SceneNode node, List<List<SceneNode>> outputGroups,
                                                           int nodesPerGroup = 32)
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

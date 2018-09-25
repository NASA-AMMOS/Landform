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
       
        abstract public void ProcessCompletedMessage(TilingQueueMessage m);
        
        // shared functionality for all current pipelines

        protected void ChunkInputs(TilingProject project)
        {
            var inputs = TilingInput.Find(pipeline.DynamoContext, project);
            foreach (var input in inputs)
            {
                logger.Info("chunking input " + input.Name + " in " + project.Name);
                workerQueue.Enqueue(new ChunkInputMessage(project.Name, input.Name));
            }
        }

        protected bool InputChunked(TilingProject project, string inputName)
        {
            logger.Info("input " + inputName + " chunked in " + project.Name);
            var inputs = TilingInput.Find(pipeline.DynamoContext, project);
            bool allChunked = inputs.All(i => i.Chunked);
            if (allChunked)
            {
                logger.Info("all inputs chunked in " + project.Name);
            }
            return allChunked;
        }

        protected void TileCompleted(TilingProject project, string tileId)
        {
            logger.Info("tile " + tileId + " completed in " + project.Name);
            
            projectCache.MarkDone(tileId);
            if (tileId == projectCache.RootId)
            {
                logger.Info("building tileset JSON in " + project.Name);
                workerQueue.Enqueue(new BuildTilesetJsonMessage(project.Name));
            }
            else
            {
                foreach (var pid in projectCache.GetDependentTilesToRun(tileId))
                {
                    logger.Info("enquing parent " + pid + " in " + project.Name);
                    workerQueue.Enqueue(new BuildParentsMessage(project.Name, pid));
                    projectCache.MarkEnqued(pid);
                }
            }
        }

        protected void TilesetCompleted(TilingProject project)
        {
            project.FinishedRunning = true;
            project.Save(pipeline.DynamoContext);
            logger.Info(project.Name + " finished running");
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

    public class TileCompletedMessage : TilingQueueMessage
    {
        public string TileId;

        public TileCompletedMessage(string projectName, string id) : base(projectName)
        {
            this.TileId = id;
        }
    }


}

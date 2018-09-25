
using System.Linq;
using OPS.Plumbing;
using OPS.Geometry;
using System.Collections.Generic;

namespace OPS.Pipeline.TileServer
{
    class GenericTilingStateMachine : PipelineStateMachine
    {
        public GenericTilingStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName) : base(pipeline, workerQueue, projectName)
        {
        }

        static public string ProjectType()
        {
            return "GenericTiling";
        }

        override public void ProcessCompletedMessage(TilingQueueMessage m)
        {
            var project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);

            if (m.GetType() == typeof(DefineTilesMessage))
            {
                // This is the first message that happens when we trigger a new run
                // Force a clearing of the cache just to avoid stale data form a previous run
                projectCache.Refresh();

                ChunkInputs(project);
            }
            else if (m.GetType() == typeof(ChunkInputMessage))
            {
                bool allChunked = InputChunked(project, ((ChunkInputMessage)m).InputName);
                if (allChunked)
                {
                    BuildBakedLeaves(project);
                }
            }
            else if (m.GetType() == typeof(TileCompletedMessage))
            {
                TileCompleted(project, ((TileCompletedMessage)m).TileId);
            }
            else if (m.GetType() == typeof(BuildTilesetJsonMessage))
            {
                TilesetCompleted(project);
            }
            else
            {
                logger.Info("Unknown message type: " + m.GetType());
            }
        }

        protected void BuildBakedLeaves(TilingProject project)
        {
            logger.Info("building baked leaves in " + project.Name);
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildBakedLeavesMessage(project.Name, group.Select(n => n.Name).ToList());
                workerQueue.Enqueue(leafJob);
                foreach (var leaf in group)
                {
                    projectCache.MarkEnqued(leaf.Name);
                }
            }
        }
    }
}

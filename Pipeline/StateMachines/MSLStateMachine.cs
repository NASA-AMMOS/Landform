using OPS.Pipeline.MeshWorker;
using OPS.Geometry;
using OPS.Plumbing;
using System.Linq;
using System.Collections.Generic;
using log4net;

namespace OPS.Pipeline.TileServer
{
    class MSLStateMachine : PipelineStateMachine
    {
        private static ILog logger = LogManager.GetLogger(typeof(MSLStateMachine));

        public MSLStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
            : base(pipeline, workerQueue, projectName)
        {
        }

        static public string ProjectType()
        {
            return "MSL";
        }

        public override void ProcessCompletedMessage(TilingQueueMessage m)
        {
            var project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);

            if (m.GetType() == typeof(BuildTilingInput))
            {
                // This is the first message that happens when we trigger a new run
                // Force a clearing of the cache just to avoid stale data form a previous run
                projectCache.Refresh();

                //TODO: call code to build big mesh and create a tiling input

                logger.Info("defining tiles in project " + project.Name);
                workerQueue.Enqueue(new DefineTilesMessage(project.Name));
            }
            else if (m.GetType() == typeof(DefineTilesMessage))
            {
                ChunkInputs(project);
            }
            else if (m.GetType() == typeof(ChunkInputMessage))
            {
                bool allChunked = InputChunked(project, ((ChunkInputMessage)m).InputName);
                if (allChunked)
                {
                    BuildBackprojectLeaves(project);
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

        protected void BuildBackprojectLeaves(TilingProject project)
        {
            logger.Info("building backproject leaves in " + project.Name);
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildBackprojectLeavesMessage(project.Name, group.Select(n => n.Name).ToList());
                workerQueue.Enqueue(leafJob);
                foreach (var leaf in group)
                {
                    projectCache.MarkEnqued(leaf.Name);
                }
            }
        }

        /// <summary>
        /// message sent to create a large mesh from input data 
        /// and upload it as the tiling input
        /// </summary>
        public class BuildTilingInput : TilingQueueMessage
        {
            public BuildTilingInput() { }

            public BuildTilingInput(string projectName) : base(projectName)
            {
            }
        }
    }
}

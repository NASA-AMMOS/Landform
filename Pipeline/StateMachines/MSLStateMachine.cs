using System;
using System.Linq;
using System.Collections.Generic;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Pipeline.MeshWorker;
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
            if (m.ProjectName != projectName)
            {
                throw new ArgumentException(string.Format("received message for project \"{0}\", expected \"{1}\"",
                                                          m.ProjectName, projectName));
            }

            if (m.GetType() == typeof(BuildTilingInput))
            {
                // This is the first message that happens when we trigger a new run
                // Force a clearing of the cache just to avoid stale data form a previous run
                projectCache.Refresh();

                //TODO: call code to build big mesh and create a tiling input

                logger.Info("defining tiles in project " + projectName);
                workerQueue.Enqueue(new DefineTilesMessage(projectName));
            }
            else if (m.GetType() == typeof(DefineTilesMessage))
            {
                ChunkInputs();
            }
            else if (m.GetType() == typeof(ChunkInputMessage))
            {
                bool allChunked = InputChunked(((ChunkInputMessage)m).InputName);
                if (allChunked)
                {
                    BuildBackprojectLeaves();
                }
            }
            else if (m.GetType() == typeof(TileCompletedMessage))
            {
                TileCompleted(((TileCompletedMessage)m).TileId);
            }
            else if (m.GetType() == typeof(BuildTilesetJsonMessage))
            {
                TilesetCompleted();
            }
            else
            {
                logger.Info("Unknown message type: " + m.GetType());
            }
        }

        protected void BuildBackprojectLeaves()
        {
            logger.Info("building backproject leaves in " + projectName);
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, projectName);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildBackprojectLeavesMessage(projectName, group.Select(n => n.Name).ToList());
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

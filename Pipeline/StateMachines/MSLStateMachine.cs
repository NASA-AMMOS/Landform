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

        public override bool ProcessMessage(TilingQueueMessage m)
        {
            if (base.ProcessMessage(m))
            {
                return true;
            }

            bool processed = false;

            if (m.GetType() == typeof(RunProjectMessage))
            {
                RunProject(new BuildTilingInputMessage(projectName));
                processed = true;
            }
            else if(m.GetType() == typeof(BuildTilingInputMessage))
            {
                logger.Info("tiling input built in project " + projectName);
                workerQueue.Enqueue(new DefineTilesMessage(projectName));
                processed = true;
            }

            return processed;
        }

        protected override void BuildLeaves(TilingProject project)
        {
            logger.Info("building backproject leaves in " + projectName);
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildBackprojectLeavesMessage(projectName, group.Select(n => n.Name).ToList());
                workerQueue.Enqueue(leafJob);
                foreach (var leaf in group)
                {
                    projectCache.MarkEnqueued(leaf.Name);
                }
            }
        }       
    }
}

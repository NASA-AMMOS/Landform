using System;
using System.Linq;
using System.Collections.Generic;
using OPS.Plumbing;
using OPS.Geometry;
using log4net;

namespace OPS.Pipeline.TileServer
{
    class GenericTilingStateMachine : PipelineStateMachine
    {
        protected static ILog logger = LogManager.GetLogger(typeof(GenericTilingStateMachine));

        public GenericTilingStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
            : base(pipeline, workerQueue, projectName)
        {
        }

        static public string ProjectType()
        {
            return "GenericTiling";
        }

        override public bool ProcessMessage(TilingQueueMessage m)
        {
            if (base.ProcessMessage(m))
            {
                return true;
            }

            bool processed = false;

            if (m.GetType() == typeof(RunProjectMessage))
            {
                RunProject(new DefineTilesMessage(projectName));
                processed = true;
            }

            return processed;
        }

        protected override bool SkipChunking(TilingProject project)
        {
            return project.TilingScheme == TilingScheme.UserDefined.ToString();
        }

        protected override void BuildLeaves(TilingProject project)
        {
            logger.Info("building baked leaves in " + projectName);
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildBakedLeavesMessage(projectName, group.Select(n => n.Name).ToList());
                workerQueue.Enqueue(leafJob);
                foreach (var leaf in group)
                {
                    projectCache.MarkEnqueued(leaf.Name);
                }
            }
        }
    }
}

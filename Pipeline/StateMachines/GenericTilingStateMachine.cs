
using System.Linq;
using OPS.Plumbing;
using OPS.Geometry;
using System.Collections.Generic;
using log4net;

namespace OPS.Pipeline.TileServer
{
    class GenericTilingStateMachine : PipelineStateMachine
    {
        protected static ILog logger = LogManager.GetLogger(typeof(GenericTilingStateMachine));

        public GenericTilingStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName) : base(pipeline, workerQueue, projectName)
        {
        }

        static public string ProjectType()
        {
            return "GenericTiling";
        }

        override public void ProcessMessage(TilingQueueMessage m)
        {
            if(m.GetType() == typeof(RunProjectMessage))
            {
                logger.Info("Run project:" + m.ProjectName);
                
                this.workerQueue.Enqueue(new DefineTilesMessage(m.ProjectName));
            }
            else if (m.GetType() == typeof(DefineTilesMessage))
            {
                logger.Info("DefineTiles project:" + m.ProjectName);
              
                TilingProject project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);
                this.projectCache.Refresh();

                TilingProject project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);
                if(project.TilingScheme == TilingScheme.UserDefined.ToString())
                {
                    // Skip Chunking
                    BuildBakedLeaves(project);
                }
                else
                {
                    ChunkInputs(project);
                }
            }
            else if (m.GetType() == typeof(ChunkInputMessage))
            {
                logger.Info("ChunkInput project:" + m.ProjectName + " input:" + ((ChunkInputMessage)m).InputName);
                TilingProject project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);
                var inputs = TilingInput.Find(pipeline.DynamoContext, project);
                bool allChunked = inputs.All(i => i.Chunked);
                if (allChunked)
                {
                    BuildBakedLeaves(project);
                }
            }
            else if (m.GetType() == typeof(TileCompletedMessage))
            {
                var id = ((TileCompletedMessage)m).TileId;
                logger.Info("TileCompleted project:" + m.ProjectName + " tile:" + id);

                this.projectCache.MarkDone(id);
                if (id == this.projectCache.RootId)
                {
                    var tilesetJob = new BuildTilesetJsonMessage(m.ProjectName);
                    workerQueue.Enqueue(tilesetJob);
                }
                else
                {
                    foreach (var pid in this.projectCache.GetDependentTilesToRun(id))
                    {
                        logger.Info("EnquingParent " + m.ProjectName + " tile:" + pid);
                        var parentJob = new BuildParentsMessage(m.ProjectName, pid);
                        workerQueue.Enqueue(parentJob);
                        this.projectCache.MarkEnqued(pid);
                    }
                }
            }
            else if (m.GetType() == typeof(BuildTilesetJsonMessage))
            {
                logger.Info("TilesetComplete " + m.ProjectName);
            }
            else
            {
                logger.Info("Unknown message type: " + m.GetType());
            }
        }

        protected void BuildBakedLeaves(TilingProject project)
        {
            logger.Info("Build Leaves");
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(root, leafGroups);

            foreach (var group in leafGroups)
            {
                var leafJob = new BuildBakedLeavesMessage(project.Name, group.Select(n => n.Name).ToList());
                workerQueue.Enqueue(leafJob);
                foreach (var leaf in group)
                {
                    this.projectCache.MarkEnqued(leaf.Name);
                }
            }
        }

        
    }
}
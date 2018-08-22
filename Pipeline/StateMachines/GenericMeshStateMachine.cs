
using System.Linq;
using OPS.Plumbing;

namespace OPS.Pipeline.TileServer
{
    class GenericMeshStateMachine : PipelineStateMachine
    {
        public GenericMeshStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName) : base(pipeline, workerQueue, projectName)
        {
        }

        override public void ProcessMessage(TilingQueueMessage m)
        { 
            if (m.GetType() == typeof(DefineTilesMessage))
            {
                logger.Info("DefineTiles project:" + m.ProjectName);

                TilingProject project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName); //BUGBUG: why not reading from cached tiling project?
                ChunkInputs(project);
            }
            else if (m.GetType() == typeof(ChunkInputMessage))
            {
                logger.Info("ChunkInput project:" + m.ProjectName + " input:" + ((ChunkInputMessage)m).InputName);
                TilingProject project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);  //BUGBUG: why not reading from cached tiling project?
                var inputs = TilingInput.Find(pipeline.DynamoContext, project);
                bool allChunked = inputs.All(i => i.Chunked);
                if (allChunked)
                {
                    BuildLeaves(project);
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
    }
}
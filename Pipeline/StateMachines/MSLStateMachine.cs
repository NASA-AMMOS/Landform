using OPS.Pipeline.MeshingWorker;
using OPS.Plumbing;
using System.Linq;

namespace OPS.Pipeline.TileServer
{
    class MSLStateMachine : PipelineStateMachine
    {
        public MSLStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName) : base(pipeline, workerQueue, projectName)
        {
        }

        public override void ProcessMessage(TilingQueueMessage m)
        {

            if (m.GetType() == typeof(BuildBigMeshMessage))
            {
                logger.Info("Build mesh");

                // This is the first message that happens when we trigger a new run
                // Force a clearing of the cache just to avoid stale data form a previous run
                this.projectCache.Refresh();

                //TODO: insert thomas code to build big mesh

                workerQueue.Enqueue(new DefineTilesMessage(m.ProjectName));
            }
            else if (m.GetType() == typeof(DefineTilesMessage))
            {
                logger.Info("DefineTiles project:" + m.ProjectName);
                TilingProject project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);
                ChunkInputs(project);
            }
            else if (m.GetType() == typeof(ChunkInputMessage))
            {
                logger.Info("ChunkInput project:" + m.ProjectName + " input:" + ((ChunkInputMessage)m).InputName);
                TilingProject project = TilingProject.Find(pipeline.DynamoContext, m.ProjectName);
                var inputs = TilingInput.Find(pipeline.DynamoContext, project);
                bool allChunked = inputs.All(i => i.Chunked);
                if (allChunked)
                {
                    BuildBackProjectedLeaves(workerQueue, project);
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
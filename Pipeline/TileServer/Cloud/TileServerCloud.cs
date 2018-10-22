using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using log4net;
using OPS.Cloud;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    public class TileServerCloud
    {
        const int WORKER_QUEUE_TIMEOUT_SEC = 60;
        const int MASTER_QUEUE_TIMEOUT_SEC = 30 * 60;

        Type[] tableTypes = new Type[]
            {
                typeof(TilingProject),
                typeof(TilingInput),
                typeof(TilingNode),
                typeof(TilingInputChunk),
                typeof(Project),
                typeof(FrameTransform),
                typeof(Frame),
                typeof(Observation),
                typeof(Overlap),
                typeof(TransformPrior)
            };

        private PipelineCore pipeline;

        public TileServerCloud(PipelineCore pipelineCore)
        {
            this.pipeline = pipelineCore;
        }

        private TilingQueue _workerQueue;
        public TilingQueue WorkerQueue
        {
            get
            {
                if (_workerQueue == null)
                {
                    _workerQueue = new TilingQueue(TileServerConfig.Instance.VenueName + "_worker", pipeline.Profile,
                                                   WORKER_QUEUE_TIMEOUT_SEC);
                }
                return _workerQueue;
            }
        }

        private TilingQueue _masterQueue;
        public TilingQueue MasterQueue
        {
            get
            {
                if (_masterQueue == null)
                {
                    _masterQueue = new TilingQueue(TileServerConfig.Instance.VenueName + "_master", pipeline.Profile,
                                                   MASTER_QUEUE_TIMEOUT_SEC);
                }
                return _masterQueue;
            }
        }

        public void DeleteQueues()
        {
            WorkerQueue.Delete();
            MasterQueue.Delete();
        }

        public void EnsureTablesExist()
        {
            // make sure tables exist
            foreach (var t in tableTypes)
            {
                var tn = TileServerConfig.Instance.VenueName + CreateCloudTemplates.TableName(t);

                try
                {
                    pipeline.DynamoDB.DescribeTable(new DescribeTableRequest(tn));
                    pipeline.Logger.InfoFormat("Table {0}: exists", tn);
                }
                catch (ResourceNotFoundException)
                {
                    pipeline.Logger.InfoFormat("Table {0}: creating", tn);
                    pipeline.DynamoDB.CreateTable(CreateCloudTemplates.CreateTable(t, TileServerConfig.Instance.VenueName));
                }
            }

            WaitForTables();
        }

        private void WaitForTables()
        {
            foreach (var t in tableTypes)
            {
                var tn = TileServerConfig.Instance.VenueName + CreateCloudTemplates.TableName(t);
                string tableStatus = "";
                bool firstTime = true;
                while (tableStatus != "ACTIVE")
                {
                    pipeline.Logger.Info("Waiting for table: " + tn);
                    try
                    {
                        var tableResponse = this.pipeline.DynamoDB.DescribeTable(new DescribeTableRequest(tn));
                        tableStatus = tableResponse.Table.TableStatus;
                    }
                    catch (ResourceNotFoundException)
                    {
                        //Wait for table
                        
                    }
                    if (!firstTime)
                    {
                        System.Threading.Thread.Sleep(3000);
                    }
                    firstTime = false;
                }
            }
        }
    }
}

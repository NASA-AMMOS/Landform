using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using log4net;
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
        

        Type[] tableTypes = new Type[]
            {
                typeof(TilingProject),
                typeof(TilingInput),
                typeof(TilingNode),
                typeof(TilingInputChunk)
            };

        PipelineCore pipeline;

        static ILog logger = LogManager.GetLogger(typeof(TileServerCloud));

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
                    _workerQueue = new TilingQueue(TileServerConfig.Instance.VenueName + "_worker", pipeline.Profile);
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
                    _masterQueue = new TilingQueue(TileServerConfig.Instance.VenueName + "_master", pipeline.Profile);
                }
                return _masterQueue;
            }
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
                }
                catch (ResourceNotFoundException)
                {
                    logger.InfoFormat("Table {0}: creating", tn);
                    pipeline.DynamoDB.CreateTable(CreateCloudTemplates.CreateTable(t, TileServerConfig.Instance.VenueName));
                    continue;
                }
                logger.InfoFormat("Table {0}: exists", tn);
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
                    logger.Info("Waiting for table: " + tn);
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

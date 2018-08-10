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
    class TileServerCloud
    {
        

        Type[] tableTypes = new Type[] { typeof(TilingProject), typeof(TilingInput), typeof(TilingNode), typeof(TilingInputChunk) };

        PipelineCore pipeline;

        static ILog logger = LogManager.GetLogger(typeof(TileServerCloud));

        public TileServerCloud(PipelineCore pipelineCore)
        {
            this.pipeline = pipelineCore;
        }

        public TilingQueue WorkerQueue
        {
            get { return new TilingQueue(TileServerConfig.Instance.VenueName, pipeline.Profile); }
        }

        public TilingQueue CompletionQueue
        {
            get { return new TilingQueue(TileServerConfig.Instance.VenueName + "_completion", pipeline.Profile); }
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
                    logger.Info("Waiting for table: " + CreateCloudTemplates.TableName(t));
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

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using log4net;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    class TileServerCloud
    {

        public const string InputUrlBase = "s3://landlords-dev/tileserver/data";
        public const string WWWUrlBase = "s3://landlords-dev/tileserver/www";
        public const string ChunkUrlBase = "s3://landlords-dev/tileserver/chunk";
        public const string TileUrlBase = "s3://landlords-dev/tileserver/tiles";



        Type[] tableTypes = new Type[] { typeof(TilingProject), typeof(TilingInput), typeof(TilingNode), typeof(TilingInputChunk) };

        string dynamoDBPrefix;
        PipelineCore pipeline;

        static ILog logger = LogManager.GetLogger(typeof(TileServerCloud));

        public TileServerCloud(string dynamoDBPrefix, PipelineCore pipelineCore)
        {
            this.dynamoDBPrefix = dynamoDBPrefix;
            this.pipeline = pipelineCore;
        }

        public TilingQueue WorkerQueue
        {
            get { return new TilingQueue(this.dynamoDBPrefix, pipeline.Profile); }
        }

        public TilingQueue CompletionQueue
        {
            get { return new TilingQueue(this.dynamoDBPrefix + "_completion", pipeline.Profile); }
        }

        public void EnsureTablesExist()
        {
            // make sure tables exist
            foreach (var t in tableTypes)
            {
                var tn = dynamoDBPrefix + CreateCloudTemplates.TableName(t);

                try
                {
                    pipeline.DynamoDB.DescribeTable(new DescribeTableRequest(tn));
                }
                catch (ResourceNotFoundException)
                {
                    // Table already exists
                    logger.InfoFormat("Table {0}: creating", tn);
                    pipeline.DynamoDB.CreateTable(CreateCloudTemplates.CreateTable(t, dynamoDBPrefix));
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
                var tn = dynamoDBPrefix + CreateCloudTemplates.TableName(t);
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

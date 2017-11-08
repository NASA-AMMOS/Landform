using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;
using System.Threading;
using System.Timers;
using Amazon.Util;
using Amazon.S3;
using Amazon.S3.Model;
using System.IO;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.EC2.Util;

using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline
{

    [Verb("queuelisten", HelpText = "Listen for messages on an SQS queue")]
    public class QueueListenerOptions
    {
    }

    /// <summary>
    /// Stack configuration specifies the other resources in this worker's stack relevant to this worker. 
    /// For dev - set in a file in the user's home directory/.landform/pipelineworker.json
    /// In AWS deployment, this file is created by the autoscale group configuration in UserData, executed whenever a machine starts up. 
    /// </summary>
    class MeshTilingConfig : Config
    {
        public string JobQueue { get; set; }
        
        public string PipelineName { get; set; }
        
        public string FailureSns { get; set; }

        //keep up-to-date with resource stack (defined in pipeline.template)
        protected override string ConfigFilename()
        {
            return "pipelineworker";
        }
    }

    public class QueueListener
    {
        private MeshTilingConfig config;

        private static IAmazonSQS SQSClient;
        private static IAmazonS3 S3Client;
        private static IAmazonSimpleNotificationService SNSClient;
        private static IAmazonCloudWatch CWClient;
        
        private readonly string[] EXTENSIONS = new string[3] { ".obj", ".mtl" , ".jpg"}; 
        private const int OBJ = 0; private const int MTL = 1; private const int IMG = 2; //indices of file types in extension array 

        //monitoring counts 
        private int messagesRecieved = 0;
        private int messagesSucceeded = 0;
        private int messagesFailed = 0;

        //timer for monitoring job 
        private System.Timers.Timer metricsTimer; 

        public QueueListener()
        {
            this.config = new MeshTilingConfig();
        }

        private void sendMetrics(object source, ElapsedEventArgs e)
        {
            //gather and reset metrics for this interval
            int total = Interlocked.Exchange(ref messagesRecieved, 0);
            int successes = Interlocked.Exchange(ref messagesSucceeded, 0);
            int failures = Interlocked.Exchange(ref messagesFailed, 0);

            Console.WriteLine("Recieved " + total + " messages, writing to CloudWatch");

            //publish custom metric
            var CWResponse = CWClient.PutMetricData(new PutMetricDataRequest
            {
                MetricData = new List<MetricDatum> { new MetricDatum
                {
                    MetricName = "MessagesRecieved",
                    Unit = StandardUnit.Count,
                    Value = total,
                    Dimensions = new List<Dimension> {
                        new Dimension {Name = "OwnerName", Value = this.config.PipelineName},  
                        new Dimension {Name = "Instance", Value = EC2InstanceMetadata.InstanceId != null ? EC2InstanceMetadata.InstanceId : "dev_machine" }
                    }
                } },
                Namespace = "Pipeline"
            });
        }

        public int Run()
        {
            //TODO check that the given queue name is valid before we wait around a long time 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); //TODO should pull region from somewhere?
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            SNSClient = new AmazonSimpleNotificationServiceClient(Amazon.RegionEndpoint.USWest1);
            CWClient = new AmazonCloudWatchClient(Amazon.RegionEndpoint.USWest1);

            //start collecting metrics! 
            metricsTimer = new System.Timers.Timer(120000); //publish metrics every 2 minutes
            metricsTimer.Elapsed += new ElapsedEventHandler(sendMetrics);
            metricsTimer.Enabled = true;

            Parallel.For(0, 8, (int i) => //Gather a max of 8 messages at once. TODO should be configurable
            {
                while (true)
                {
                    var req = new ReceiveMessageRequest
                    {
                        AttributeNames = new List<string>() { "All" }, //metadata about recieved message - will enable some benchmarking
                        MessageAttributeNames = new List<string>() { "All" },
                        MaxNumberOfMessages = 1,
                        QueueUrl = config.JobQueue,
                        WaitTimeSeconds = (int)TimeSpan.FromSeconds(15).TotalSeconds //how long I'll wait for a message
                    };
                    ReceiveMessageResponse r = SQSClient.ReceiveMessage(req);
                    if (r.Messages.Count > 0) //we have a message
                    {
                        Interlocked.Increment(ref messagesRecieved);
                        CreateParentTileMsg m = (CreateParentTileMsg)PipelineMessage.FromMessage(r.Messages[0]);
                        Console.WriteLine(".....Message recieved:"
                            +"\r\n        Message ID = " + m.MessageId
                            +"\r\n        URL = " + m.ParentPath);
                        try
                        {
                            processMessage(m); //Process messages synchronously 
                            Interlocked.Increment(ref messagesSucceeded);
                        }
                        catch (Exception e)
                        {
                            Interlocked.Increment(ref messagesFailed);
                            string msg = "Processing failed for message " + m.MessageId + "; additional message info: " + m.ParentPath
                                + "\r\n Error msg is: " + e.Message
                                + "\r\n Stack trace is: " + e.StackTrace;
                            Console.WriteLine(msg);
                            //if an SNS has been specified for failiure messages, publish there 
                            if (config.FailureSns != null)
                            {
                                PublishRequest notification = new PublishRequest
                                {
                                    Message = msg,
                                    TopicArn = config.FailureSns
                                };
                                var s = SNSClient.Publish(notification);
                                Console.WriteLine("published " + Convert.ToString(s));
                            }
                        }
                    }
                }
            });
            
            return 0;
        }


        private int processMessage(CreateParentTileMsg m)
        {
            //ParentPath is currently the path, including bucket, to the s3 resource that the parent WILL be; minus endings 
            string s3url = "s3://" + m.ParentPath;

            //run a lil image pipeline 

            int width = 512;
            int height = 512;

            //Download files 
            MeshImagePair[] meshes = new MeshImagePair[m.NumChildren];
            Mesh[] justmesh = new Mesh[m.NumChildren];
            StorageHelper storage = new StorageHelper();
            int newFaceCount = 0;
            for (int index = 0; index < m.NumChildren; index++)
            {
                //GOD KNOWS WHY but this doesn't break very often, so using this while working on AWS resources
                //Frequency of breakage: a few in a thousand 
                //inline temp files 
                S3Url url = new S3Url(s3url);
                string root = (@"C:\tmp\in\" + Guid.NewGuid()).Replace('/', '\\');
                storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[OBJ], root + EXTENSIONS[OBJ]);
                storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[IMG], root + EXTENSIONS[IMG]);

                meshes[index] = new MeshImagePair(Mesh.Load(root + EXTENSIONS[OBJ]), Image.Load(root + EXTENSIONS[IMG]));
                justmesh[index] = meshes[index].Mesh;
                newFaceCount += meshes[index].Mesh.Faces.Count;

                if (File.Exists(root + EXTENSIONS[OBJ]))
                {
                    File.Delete(root + EXTENSIONS[OBJ]);
                }
                if (File.Exists(root + EXTENSIONS[IMG]))
                {
                    File.Delete(root + EXTENSIONS[IMG]);
                }

                /*
                //using temp file helper
                //Frequency of breakage: one in 10 to one in 20? 
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[OBJ], tmp[OBJ]);
                    storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[IMG], tmp[IMG]);

                    meshes[index] = new MeshImagePair(Mesh.Load(tmp[OBJ]), Image.Load(tmp[IMG]));
                    newFaceCount += meshes[index].Mesh.Faces.Count;
                });
                */

                /*
                //using temp files with streaming 
                //Frequency of breakage: same as with DownloadFile
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    storage.GetStream(s3url + Convert.ToString(index) + EXTENSIONS[OBJ], (Stream s) => {
                        using (var fileStream = File.Create(tmp[OBJ]))
                        {
                            s.CopyTo(fileStream);
                        }
                    });
                    storage.GetStream(s3url + Convert.ToString(index) + EXTENSIONS[IMG], (Stream s) => {
                        using (var fileStream = File.Create(tmp[IMG]))
                        {
                            s.CopyTo(fileStream);
                        }
                    });

                    meshes[index] = new MeshImagePair(Mesh.Load(tmp[OBJ]), Image.Load(tmp[IMG]));
                    newFaceCount += meshes[index].Mesh.Faces.Count;
                });*/

                /*
                //grabbing local files instead, to isolate TU vs mesh and image loading 
                //breaks
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    File.Copy(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.obj", tmp[OBJ]);
                    File.Copy(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.jpg", tmp[IMG]);

                    meshes[index] = new MeshImagePair(Mesh.Load(tmp[OBJ]), Image.Load(tmp[IMG]));
                    newFaceCount += meshes[index].Mesh.Faces.Count;
                });
                */

                /*
                //hardcoding, no temp file use, just get and delete 
                //doesn't break
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    meshes[index] = new MeshImagePair(Mesh.Load(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.obj"), Image.Load(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.jpg"));
                    newFaceCount += meshes[index].Mesh.Faces.Count;
                });
                */

                
                //mesh or image breaking? 
                //Image!
                //image seems to load ok even when loader doesn't give up file...
                /*
                Mesh mesh = null; Image image = null; string impath;
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    File.Copy(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.obj", tmp[OBJ]);
                    mesh = Mesh.Load(tmp[OBJ]);
                });
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    File.Copy(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.jpg", tmp[IMG]);
                    image = Image.Load(tmp[IMG]);
                    impath = tmp[IMG];
                    try
                    {
                        File.Delete(impath);
                    }
                    catch
                    {
                        Console.WriteLine("pasta");
                    }
                });
                meshes[index] = new MeshImagePair(mesh, image);
                newFaceCount += meshes[index].Mesh.Faces.Count;
                */

                //using temp file only for TU - just to check if this is also breaking (it's not, at least for ~hundreds)
                /*
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[OBJ], tmp[OBJ]);
                    storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[IMG], tmp[IMG]);
                });
                meshes[index] = new MeshImagePair(Mesh.Load(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.obj"), Image.Load(@"C:\Users\gpease\Documents\data\Terrain\Terrain-clean\2111111111000.jpg"));
                newFaceCount += meshes[index].Mesh.Faces.Count;
                */
            }
            newFaceCount = Convert.ToInt32(newFaceCount / Convert.ToDouble(m.NumChildren));

            //Merge the meshes 
            Mesh dst = Mesh.Merge(justmesh);

            //Recompute normals 
            //dst.GenerateVertexNormals();
            //dst = MeshLab.ComputeNormals(dst);
            //Ensure normals - FSSR will fail without them
            dst.GenerateVertexNormals();

            //Sample to smooth edges between parents 
            SurfacePointSampler sps = new SurfacePointSampler();
            Mesh pc = sps.GenerateSampledMesh(dst, 10, 200.0 / dst.SurfaceArea());
            pc.HasUVs = false;
            Mesh dst1 = FSSR.PoissonReconstruct(pc);

            //Collapse so we have the same num faces our children did 
            var dst2 = EdgeCollapse.QuadricEdgeCollapse(dst1, newFaceCount, 1);
            dst2.HasNormals = false;

            //Make us pretty again 
            var dst3 = UVAtlas.Atlas(dst2, width, height, maxStretch: 0.025f);
            var img = TextureBaker.BakeTexture(meshes, dst3, width, height);
            //Recompute normals 
            dst3.GenerateVertexNormals();
            //dst3 = MeshLab.ComputeNormals(dst3);

            //after processing, the file should have normals and UVs. 
            if (!(dst3.HasNormals && dst3.HasUVs && dst3.HasFaces))
            {
                throw new Exception("The generated meshe was missing a required feature (normals, UVs, or faces)");
            }

            //Save out to temporary files on disk then upload those to S3
            /*
            TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
            {
                img.Save<byte>(tmp[IMG]);
                dst3.Save(tmp[OBJ], Path.GetFileName(s3url + EXTENSIONS[IMG])); 
                storage.UploadFileSingleThread(tmp[IMG], s3url + EXTENSIONS[IMG]);
                storage.UploadFileSingleThread(tmp[OBJ], s3url + EXTENSIONS[OBJ]);
                Console.WriteLine(".....Upload finished for Message ID = " + m.MessageId);
                storage.UploadFile(Path.ChangeExtension(tmp[OBJ], EXTENSIONS[MTL]), s3url + EXTENSIONS[MTL]); 
            });
            */
            //Save out to temporary files with the names we'll eventually use 
            //TODO this is a temporary fix because there is a filename dependency when writing out MTL files. This will break if a worker gets two of the same message at once (which is possible)
            string filename = Path.GetFileName(s3url);
            string location = (@"C:\tmp\in\" + filename).Replace('/', '\\');
            img.Save<byte>(location + EXTENSIONS[IMG]);
            dst3.Save(location + EXTENSIONS[OBJ], Path.GetFileName(s3url + EXTENSIONS[IMG]));
            storage.UploadFileSingleThread(location + EXTENSIONS[IMG], s3url + EXTENSIONS[IMG]);
            storage.UploadFileSingleThread(location + EXTENSIONS[OBJ], s3url + EXTENSIONS[OBJ]);
            storage.UploadFileSingleThread(location + EXTENSIONS[MTL], s3url + EXTENSIONS[MTL]);
            Console.WriteLine(".....Upload finished for Message ID = " + m.MessageId);

            if (File.Exists(location + EXTENSIONS[OBJ]))
            {
                File.Delete(location + EXTENSIONS[OBJ]);
            }
            if (File.Exists(location + EXTENSIONS[IMG]))
            {
                File.Delete(location + EXTENSIONS[IMG]);
            }
            if (File.Exists(location + EXTENSIONS[MTL]))
            {
                File.Delete(location + EXTENSIONS[MTL]);
            }

            //message is still in queue until we tell it to delete 
            m.DeleteMessage(SQSClient, config.JobQueue);

            return 0; 
        }
    }
}

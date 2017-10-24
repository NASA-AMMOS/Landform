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
    class StackConfig : Config
    {
        public string JobQueue { get; set; }
        
        public string PipelineName { get; set; }
        
        public string FailureSns { get; set; }

        protected override string ConfigFilename()
        {
            return "pipelineworker";
        }
    }

    public class QueueListener
    {
        private StackConfig config;

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
            this.config = new StackConfig();
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
            return 0; //testing deployment validation

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
                        Message m = r.Messages[0];
                        Console.WriteLine(".....Message recieved:"
                            +"\r\n        Message ID = " + m.MessageId
                            +"\r\n        URL = " + m.MessageAttributes["ParentPath"].StringValue);
                        try
                        {
                            processMessage(m); //Process messages synchronously 
                            Interlocked.Increment(ref messagesSucceeded);
                        }
                        catch (Exception e)
                        {
                            Interlocked.Increment(ref messagesFailed);
                            string msg = "Processing failed for message " + m.MessageId + "; additional message info: " + m.MessageAttributes["ParentPath"].StringValue
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


        private int processMessage(Message m)
        {
            //ParentPath is currently the path, including bucket, to the s3 resource that the parent WILL be; minus endings 
            string s3url = "s3://" + m.MessageAttributes["ParentPath"].StringValue;
            int numChildren = Convert.ToInt32(m.MessageAttributes["NumChildren"].StringValue);

            //run a lil image pipeline 

            int width = 512;
            int height = 512;

            //Download files 
            MeshImagePair[] meshes = new MeshImagePair[numChildren];
            Mesh[] justmesh = new Mesh[numChildren];
            StorageHelper storage = new StorageHelper();
            int newFaceCount = 0;
            for (int index = 0; index < numChildren; index++)
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
            newFaceCount = Convert.ToInt32(newFaceCount / Convert.ToDouble(numChildren));

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
            
            TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
            {
                img.Save<byte>(tmp[IMG]);
                dst3.Save(tmp[OBJ], Path.GetFileName(tmp[IMG]));
                storage.UploadFileSingleThread(tmp[IMG], s3url + EXTENSIONS[IMG]);
                storage.UploadFileSingleThread(tmp[OBJ], s3url + EXTENSIONS[OBJ]);
                Console.WriteLine(".....Upload finished for Message ID = " + m.MessageId);
                //storage.UploadFile(Path.ChangeExtension(tmp[OBJ], EXTENSIONS[MTL]), s3url + EXTENSIONS[MTL]); //MTL file is path-dependent so *shrug*
            });


            /*
            //When files are saved like this (in one go) only one HTTP PUT lambda is generated. The SUPER FAST SPEEDY upload above generates multiple. TODO
            S3Url url = new S3Url(s3url);
            string root = (@"C:\tmp\out\" + url.Prefix).Replace('/', '\\');
            string imgFilename = root + ".jpg";
            PathHelper.EnsureExists(root); //TODO this will make extra directories (prefix/) that are unneeded 
            //dst1.Save(root + ".obj");
            img.Save<byte>(imgFilename);
            dst3.Save(root + ".obj", Path.GetFileName(imgFilename));

            UploadResult(url.BucketName, url.Prefix, root);
            */

            //message is still in queue until we tell it to delete 
            var delRequest = new DeleteMessageRequest
            {
                QueueUrl = config.JobQueue,
                ReceiptHandle = m.ReceiptHandle
            };

            var delResponse = SQSClient.DeleteMessage(delRequest);
            if (delResponse.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine(".....Message " + m.MessageId + " deleted.");
            }

            return 0; 
        }
    }
}

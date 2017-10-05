using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;
using System.Threading;
using Amazon.S3;
using Amazon.S3.Model;
using System.IO;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline
{

    [Verb("queuelisten", HelpText = "Listen for messages on an SQS queue")]
    public class QueueListenerOptions
    {
        [Option(Required = false, HelpText = "Name of the aws profile to use to authenticate")]
        public string AwsProfile { get; set; }

        [Option(Required = false, HelpText = "ARN of the SNS topic to alert on failures, if desired")]
        public string FailureSns { get; set; }

        [Option(Required = false, HelpText = "HTTP address of the queue")]
        public string QueueUrl { get; set; }
    }

    public class QueueListener
    {
        public QueueListenerOptions options;

        public IAmazonSQS SQSClient;
        public IAmazonS3 S3Client;
        public IAmazonSimpleNotificationService SNSClient;
        
        private readonly string[] EXTENSIONS = new string[] { ".obj", ".mtl" , ".jpg"};
        private const int OBJ = 0; private const int MTL = 1; private const int IMG = 2; //indices of file types in extension array 
        private readonly int[] INDICES = new int[] { 0, 1, 2, 3 };

        public QueueListener(QueueListenerOptions options)
        {
            this.options = options;
            //TODO below is just because I'm lazy, irl queue name should be given 
            if (options.QueueUrl == null)
            {
                this.options.QueueUrl = "https://sqs.us-west-1.amazonaws.com/589270964471/LandformsTestQueue";
            }
        }

        public int Run()
        {
            //TODO check that the given queue name is valid before we wait around a long time 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); //TODO should pull region from somewhere?
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            SNSClient = new AmazonSimpleNotificationServiceClient(Amazon.RegionEndpoint.USWest1); 

            Parallel.For(0, 8, (int i) => //Gather a max of 8 messages at once. TODO should be configurable
            {
                while (true)
                {
                    var req = new ReceiveMessageRequest
                    {
                        AttributeNames = new List<string>() { "All" }, //metadata about recieved message - will enable some benchmarking
                        MessageAttributeNames = new List<string>() { "All" },
                        MaxNumberOfMessages = 1,
                        QueueUrl = options.QueueUrl,
                        WaitTimeSeconds = (int)TimeSpan.FromSeconds(15).TotalSeconds //how long I'll wait for a message
                    };
                    ReceiveMessageResponse r = SQSClient.ReceiveMessage(req);
                    if (r.Messages.Count > 0) //we have a message
                    {
                        Message m = r.Messages[0];
                        Console.WriteLine(".....Message recieved:"
                            +"\r\n        Message ID = " + m.MessageId
                            +"\r\n        URL = " + m.MessageAttributes["ParentPath"].StringValue);
                        try
                        {
                            Console.WriteLine("--- thread"+Convert.ToString(i) + " in message processing");
                            processMessage(m); //Process messages synchronously 
                            Console.WriteLine("--- thread"+Convert.ToString(i) + " done message processing");
                        }
                        catch (Exception e)
                        {
                            string msg = "Processing failed for message " + m.MessageId + "; additional message info: " + m.MessageAttributes["ParentPath"].StringValue
                                + "\r\n Error msg is: " + e.Message
                                + "\r\n Stack trace is: " + e.StackTrace;
                            Console.WriteLine(msg);
                            //if an SNS has been specified for failiure messages, publish there 
                            if (options.FailureSns != null)
                            {
                                PublishRequest notification = new PublishRequest
                                {
                                    Message = msg,
                                    TopicArn = options.FailureSns
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
            string s3url = "s3://" + m.MessageAttributes["ParentPath"].StringValue; //TODO this is the full path, including bucket name, of the (to-be-computed) parent of the added file. Missing file ending

            //run a lil image pipeline 

            int width = 512;
            int height = 512;

            //Download files 
            MeshImagePair[] meshes = new MeshImagePair[4];
            StorageHelper storage = new StorageHelper();
            int newFaceCount = 0;
            foreach (int index in INDICES)
            {
                //inline temp files 
                S3Url url = new S3Url(s3url);
                string root = (@"C:\tmp\in\" + Guid.NewGuid()).Replace('/', '\\');
                storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[OBJ], root + EXTENSIONS[OBJ]);
                storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[IMG], root + EXTENSIONS[IMG]);

                meshes[index] = new MeshImagePair(Mesh.Load(root + EXTENSIONS[OBJ]), Image.Load(root + EXTENSIONS[IMG]));
                newFaceCount += meshes[index].Mesh.Faces.Count;

                if (File.Exists(root + EXTENSIONS[OBJ]))
                {
                    File.Delete(root + EXTENSIONS[OBJ]);
                }
                if (File.Exists(root + EXTENSIONS[IMG]))
                {
                    File.Delete(root + EXTENSIONS[IMG]);
                }

                //using temp file helper
                /*
                TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                {
                    storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[OBJ], tmp[OBJ]);
                    storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[IMG], tmp[IMG]);

                    meshes[index] = new MeshImagePair(OBJSerializer.Read(tmp[OBJ]), Image.Load(tmp[IMG]));
                    newFaceCount += meshes[index].Mesh.Faces.Count;
                });*/
            }
            newFaceCount = Convert.ToInt32(newFaceCount / 4.0);

            //Merge the meshes 
            Mesh dst = Mesh.Merge(meshes[0].Mesh, meshes[1].Mesh, meshes[2].Mesh, meshes[3].Mesh);

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
            var dst2 = EdgeCollapse.QuadricEdgeCollapse(dst1, newFaceCount, 1, notTouched: EdgeCollapse.GetCorners(dst1));
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
                QueueUrl = options.QueueUrl,
                ReceiptHandle = m.ReceiptHandle
            };

            var delResponse = SQSClient.DeleteMessage(delRequest);
            Console.WriteLine(".....Message " + m.MessageId + " deleted.");

            return 0; //TODO check response 
        }

        /*
        //modeled off http://docs.aws.amazon.com/AmazonS3/latest/dev/RetrievingObjectUsingNetSDK.html
        //Get an object that we know exists in S3. Copy it to a local file. 
        public async Task<string> ReadObjectData(string bucket, string key, string temp)
        {
            GetObjectRequest request = new GetObjectRequest
            {
                BucketName = bucket,
                Key = key
            };

            GetObjectResponse response = null;
            for (int i = 0; i < 4; i++) //Eventual consistency means we may have to wait for the object to appear
            {
                try
                {
                    response = await S3Client.GetObjectAsync(request);
                    break;
                }
                catch (AmazonS3Exception e)
                {
                    if (i < 3 && e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        await Task.Delay(1000); //wait a second. TODO what is the usual time to consistency? 
                        continue; 
                    }
                    throw;
                }
            }

            //Stream responseStream = response.ResponseStream;
            //StreamReader reader = new StreamReader(responseStream);
            response.WriteResponseStreamToFile(temp);
            Console.WriteLine("wrote " + key + " to temporary file.");
            return temp;
        }*/

        //upload the .obj, .mtl, and .jpg files for this prefix where: 
        //bucket/prefix.extention is where the files will end up on S3
        //local.extension is the full path of the files locally locally
        
        public int UploadResult(string bucket, string prefix, string local)
        {
            foreach (string extension in EXTENSIONS)
            {
                PutObjectRequest r = new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = prefix + extension,
                    FilePath = local + extension
                };
                PutObjectResponse response = S3Client.PutObject(r);
            }
            return 0; //TODO check response
        }
    }
}

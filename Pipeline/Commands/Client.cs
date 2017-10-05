using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using System.Threading;
using Amazon.S3;
using Amazon.S3.Model;
using System.IO;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2;


/// <summary>
/// This file is just a holder for things I want to try using AWSSDK :) 
/// command line: client --awsprofile default 
/// </summary>

namespace OPS.Pipeline
{
    [Verb("client", HelpText = "trying things out")]
    public class ClientOptions
    {
        [Option(Required = false, HelpText = "Name of the aws profile to use to authenticate")]
        public string AwsProfile { get; set; }
    }

    public class Client
    {
        private readonly string[] EXTENSIONS = new string[] { ".obj", ".mtl", ".jpg" };
        private const int OBJ = 0; private const int MTL = 1; private const int IMG = 2; //indices of file types in extension array 
        private readonly int[] INDICES = new int[] { 0, 1, 2, 3 };

        public ClientOptions options;

        public Client(ClientOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            string s3url = "s3://landlords-dev/gailin/211111111103";

            Parallel.For(0, 8, (int i) =>
            {
                while (true) //run till i break
                {
                    if (i<7)
                    {
                        //grab-o some files 
                        MeshImagePair[] meshes = new MeshImagePair[4];
                        StorageHelper storage = new StorageHelper();
                        int newFaceCount = 0;
                        foreach (int index in INDICES)
                        {
                            TemporaryFile.GetAndDeleteMultiple(EXTENSIONS, tmp =>
                            {
                                storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[OBJ], tmp[OBJ]);
                                storage.DownloadFile(s3url + Convert.ToString(index) + EXTENSIONS[IMG], tmp[IMG]);

                                meshes[index] = new MeshImagePair(OBJSerializer.Read(tmp[OBJ]), Image.Load(tmp[IMG]));
                                newFaceCount += meshes[index].Mesh.Faces.Count;
                            });
                        }
                        Console.WriteLine("download complete thread " + Convert.ToString(i));
                    }
                    else
                    {
                        foreach (int index in INDICES)
                        {
                            StorageHelper storage = new StorageHelper();
                            string local = @"C:\Users\gpease\Documents\data\Terrain\Terrain-clean-renamed\211111111103";
                            storage.UploadFileSingleThread(local + Convert.ToString(index) + EXTENSIONS[IMG], s3url + EXTENSIONS[IMG]);
                            storage.UploadFileSingleThread(local + Convert.ToString(index) + EXTENSIONS[OBJ], s3url + EXTENSIONS[OBJ]);
                            Console.WriteLine("upload complete" + Convert.ToString(i));
                        }
                        
                    }
                }
            });
            return 0;
            
        }


        /// <summary>
        /// For getting a table on the initialization of the Lambda
        /// from http://docs.aws.amazon.com/amazondynamodb/latest/developerguide/GettingStarted.NET.03.html
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public static Table GetTableObject(string tableName)
        {
            AmazonDynamoDBConfig ddbConfig = new AmazonDynamoDBConfig();
            ddbConfig.RegionEndpoint = Amazon.RegionEndpoint.USWest1;
            AmazonDynamoDBClient client;
            try
            {
                client = new AmazonDynamoDBClient(ddbConfig);
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n Error: failed to create a DynamoDB client; " + ex.Message);
                return (null);
            }

            // Now, create a Table object for the specified table
            Table table = null;
            try
            {
                table = Table.LoadTable(client, tableName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n Error: failed to load the " + tableName + " table; " + ex.Message);
                return (null);
            }
            return (table);
        }

        //below here is just old stuff

        public static string getGreeting(string who)
        {
            return "{\"Hello\": \"" + who + "\"}";
        }

        private async Task<bool> objectExists(string bucket, string key)
        {
            //Try to get this object. According to https://forums.aws.amazon.com/message.jspa?messageID=215792, this is faster than listing with key as prefix
            using (IAmazonS3 client = new AmazonS3Client(Amazon.RegionEndpoint.USEast1))
                try
            {
                GetObjectMetadataResponse data = await client.GetObjectMetadataAsync(bucket, key);
            }
            catch (AmazonS3Exception e) //If this is a NoSuchKey exception, the object does not exist 
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return false; //if e.ErrorCode == "NotFound"
                }
                throw; //otherwise this is some other error 
            }
            return true;
        }

        //Use list instead of get to preserve read-before-write consistency for new puts 
        private async Task<bool> objectExistsLite(string bucket, string key)
        {
            //List objects in bucket with this name as prefix. According to https://forums.aws.amazon.com/message.jspa?messageID=215792, this is slower than get metadata approach
            //Relies on file endings (so no filename is a prefix of another) 
            using (IAmazonS3 client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1))
            {
                ListObjectsRequest r = new ListObjectsRequest
                {
                    BucketName = bucket,
                    Prefix = key,
                    MaxKeys = 1
                };
                ListObjectsResponse data = await client.ListObjectsAsync(r);
                return data.S3Objects.Count == 1;
            }
        }
    }
}

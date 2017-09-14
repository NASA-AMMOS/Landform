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

/// <summary>
/// This file is just a holder for things I want to try using AWSSDK :) 
/// command line: client --awsprofile default 
/// </summary>

namespace OPS.Pipeline
{
    [Verb("client", HelpText = "Listen for requests from AWS step function")]
    public class ClientOptions
    {
        [Option(Required = true, HelpText = "Name of the aws profile to use to authenticate")]
        public string AwsProfile { get; set; }

        [Option(Required = true, HelpText = "File name to mess with")]
        public string FileName { get; set; }
    }

    public class Client
    {
        public ClientOptions options;

        public Client(ClientOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            Task.WaitAll(asyncRun());
            return 0;
        }

        public async Task<int> asyncRun()
        {
            //testing read-after-write consistency: check if a thing exists (one way or the other), create it, read it. 
            Console.WriteLine("...object exists: " + Convert.ToString(await objectExistsLite("landlords-dev", options.FileName)));
            await write("landlords-dev", options.FileName);
            await ReadObjectData("landlords-dev", options.FileName);

            //Console.WriteLine(Convert.ToString(await objectExists("landlords-dev", "gailin/1.txt"))); 
            


            //var client = new AmazonStepFunctionsClient(Credentials.Get(options.AwsProfile), Amazon.RegionEndpoint.USWest2);
            //while (true)
            //{
            //    var req = new GetActivityTaskRequest
            //    {
            //        ActivityArn = "arn:aws:states:us-west-2:589270964471:activity:landform-test-activity"
            //    };
            //    GetActivityTaskResponse r = await client.GetActivityTaskAsync(req); //waits up to 60sec for an activity 
            //    if (r.Input != null)
            //    {
            //        //Just printing input to confirm that step function passes vars as expected 
            //        Console.WriteLine("Got some input");
            //        Console.WriteLine(r.Input); //JSON input data for the task 
            //    }
            //    if (r.TaskToken != null)
            //    {
            //        try
            //        {
            //            //say this task requires us to get something from S3
            //            Console.WriteLine(ReadObjectData("landlords-dev", "gailin/0.txt"));

            //            //create something to return to the step function 
            //            string name = "gailin";
            //            string input = r.Input;
            //            string result = getGreeting(name);

            //            await client.SendTaskSuccessAsync(new SendTaskSuccessRequest { Output = result, TaskToken = r.TaskToken });
            //        }
            //        catch (Exception e)
            //        {
            //            await client.SendTaskFailureAsync(new SendTaskFailureRequest { TaskToken = r.TaskToken });
            //        }
            //    }
            //    else
            //    {
            //        Thread.Sleep(1000);
            //    }
            //}
            

            return 0;
        }

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

        //modeled off http://docs.aws.amazon.com/AmazonS3/latest/dev/RetrievingObjectUsingNetSDK.html
        //Notes: if called from within activity task response, activity fails (not timeout). TODO - why?? 
        public static async Task<string> ReadObjectData(string bucket, string key)
        {
            string responseBody = "";
            using (IAmazonS3 client = new AmazonS3Client(Amazon.RegionEndpoint.USEast1))
            {
                GetObjectRequest request = new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };
                using (GetObjectResponse response = await client.GetObjectAsync(request))
                using (Stream responseStream = response.ResponseStream)
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    responseBody = reader.ReadToEnd();
                }
            }
            Console.WriteLine(responseBody);
            return responseBody;
        }

        private async Task<int> write(string bucket, string key)
        {
            using (IAmazonS3 client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1))
            {
                PutObjectRequest r = new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    ContentBody = "I was added by a robot!"
                };
                PutObjectResponse response = await client.PutObjectAsync(r);
                return 0; 
            }
        }
    }
}

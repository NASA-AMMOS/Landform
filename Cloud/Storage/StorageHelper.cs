using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.Runtime;
using System.Text.RegularExpressions;
using System.IO;

namespace OPS.Cloud
{    
    /// <summary>
    /// Interface with S3 data store
    /// </summary>
    public class StorageHelper
    {
        /// <summary>
        /// Function prototype for processing downloaed streams
        /// </summary>
        /// <param name="stream"></param>
        public delegate void StreamHandler(Stream stream);

        AWSCredentials awsCredentials;
        Amazon.RegionEndpoint awsRegion;
        
        /// <summary>
        /// Use the given profile name to create a storage helper
        /// Profiles can be defined in the ~/.aws/credentials file
        /// Will try to connect to s3 resources through USWest1 region endpoint by default
        /// Will try to connect to USGovCloudWest1 end point if gov cloud is specified
        /// </summary>
        /// <param name="awsProfileName"></param>
        /// <param name="govCloud"></param>
        public StorageHelper(string awsProfileName, bool govCloud = false)
        {
            awsCredentials = Credentials.Get(awsProfileName);
            awsRegion = govCloud ? RegionEndpoint.USGovCloudWest1 : RegionEndpoint.USWest1;
        }

        /// <summary>
        /// Convert a string containing * and ? wildcard characters to a string that can be used to generate a regex
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static String WildCardToRegular(String value)
        {
            return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        }
        
        /// <summary>
        /// Create a list request.  Request will be recursive if delimiter is not used
        /// </summary>
        /// <param name="s3url"></param>
        /// <param name="useDelimeter"></param>
        /// <returns></returns>
        private ListObjectsV2Request CreateListRequest(string s3url, bool useDelimeter)
        {
            S3Url location = new S3Url(s3url);
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = location.BucketName,
                MaxKeys = 200
            };
            if (location.Prefix.Length > 0)
            {
                request.Prefix = location.Prefix;
            }
            if (useDelimeter)
            {
                request.Delimiter = "/";
            }
            return request;
        }

        /// <summary>
        /// List direct subfolder of the s3 prefix specifed in s3url
        /// </summary>
        /// <param name="s3url">Represents a url for an s3 "folder".  Note that this must end with a complete folder name and a trailing forward slash will be added automaticly if one is not specified</param>
        /// <param name="pattern">Only return results matching this string pattern.  Wildcards * and ? can be used.</param>
        /// <returns></returns>
        public IEnumerable<string> SearchFolders(string s3url, string pattern = "*")
        {
            if (!s3url.EndsWith("/"))
            {
                s3url += "/";
            }
            S3Url location = new S3Url(s3url);
            using (var client = new AmazonS3Client(awsCredentials, awsRegion))
            {
                var regex = new Regex(WildCardToRegular(pattern));
                var request = CreateListRequest(s3url, true);
                ListObjectsV2Response response;
                do
                {
                    response = client.ListObjectsV2(request);
                    // Process response.
                    foreach (string prefix in response.CommonPrefixes)
                    {
                        if (regex.IsMatch(prefix))
                        {
                            yield return new S3Url(location.BucketName, prefix).Url;
                        }
                    }                    
                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated == true);
            }
        }

        /// <summary>
        /// Returns a sequence of S3 objects
        /// Must have trailing slash if its a directory
        /// </summary>
        /// <param name="s3url">An s3 url specifying the key prefix to search.  This can be a complete or partial "folder" or object key.</param>
        /// <param name="pattern">Only return results matching this string pattern.  Wildcards * and ? can be used.</param>
        /// <param name="recursive">Return all keys with this s3url prefx if set to true.  If not stop at the next folder, delimited by a forward slash in the key.</param>
        public IEnumerable<string> SearchObjects(string s3url, string pattern = "*", bool recursive = true)
        {
            S3Url location = new S3Url(s3url);
            var regex = new Regex(WildCardToRegular(pattern));
            using (var client = new AmazonS3Client(awsCredentials, awsRegion))
            {
                var request = CreateListRequest(s3url, !recursive);
                ListObjectsV2Response response;
                do
                {
                    response = client.ListObjectsV2(request);                    
                    // Process response.
                    foreach (S3Object entry in response.S3Objects)
                    {
                        if (regex.IsMatch(entry.Key))
                        {
                            yield return new S3Url(location.BucketName, entry.Key).Url;
                        }
                    }
                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated == true);
            }
        }

        /// <summary>
        /// Download a file and save it to local disk
        /// </summary>
        /// <param name="s3url"></param>
        /// <param name="filename"></param>
        public void DownloadFile(string s3url, string filename)
        {
            using (var client = new AmazonS3Client(awsCredentials, awsRegion))
            {
                S3Url location = new S3Url(s3url);
                TransferUtility tu = new TransferUtility(client);
                tu.Download(filename, location.BucketName, location.Prefix);
            }
        }

        /// <summary>
        /// Returns a stream to a file.  This stream does not download the entire file.
        /// streamHandler is called with the stream.  The caller does not need to wrap the 
        /// stream in a using statement.
        /// </summary>
        /// <param name="s3url"></param>
        /// <param name="streamHandler"></param>
        public void GetStream(string s3url, StreamHandler streamHandler)
        {
            using (var client = new AmazonS3Client(awsCredentials, awsRegion))
            {
                TransferUtility tu = new TransferUtility(client);
                S3Url location = new S3Url(s3url);
                using (var s = tu.OpenStream(location.BucketName, location.Prefix))
                {
                    streamHandler(s);
                }
            }
        }
    }
}

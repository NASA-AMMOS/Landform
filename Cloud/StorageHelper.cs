using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.S3.IO;
using Amazon.Runtime;
using log4net;
using JPLOPS.Util;

namespace JPLOPS.Cloud
{
    /// <summary>
    /// Interface with S3 data store
    /// </summary>
    public class StorageHelper : IDisposable
    {
        private ILog logger;
        private bool autodetectRegion;
        private AWSCredentials awsCredentials;
        private RegionEndpoint awsRegion;
        private ConcurrentDictionary<string, AmazonS3Client> regionToClient =
            new ConcurrentDictionary<string, AmazonS3Client>();
        private ConcurrentDictionary<string, RegionEndpoint> bucketToRegion =
            new ConcurrentDictionary<string, RegionEndpoint>();

        public static string ConvertS3URLToHttps(string url, string proxy = null)
        {
            S3Url location = new S3Url(url);
            if (string.IsNullOrEmpty(proxy))
            {
                return string.Format("https://{0}.s3.amazonaws.com/{1}", location.BucketName, location.Path);
            }
            else
            {
                return string.Format("{0}/{1}/{2}", proxy, location.BucketName, location.Path);
            }
        }

        /// <summary>
        /// Use the given profile name to create a storage helper
        /// Profiles can be defined in the ~/.aws/credentials file
        ///
        /// If a region name such as "us-west-1" is provided then use that
        ///
        /// If region name is "auto" then determine the region for buckets based on the bucket name in the url
        /// Note that s3:GetBucketLocation must be enabled for this to work.
        ///
        /// Leaving awsProfileName and awsRegionName null works if there is a default profile on a user machine
        /// or an IAM role an EC2 instance
        /// </summary>
        public StorageHelper(string awsProfileName = null, string awsRegionName = null, ILog logger = null)
        {
            this.logger = logger;

            Func<string, string[], string> convertNull =
                (s, nulls) => s == null || nulls.Any(n => n == s.ToLower()) ? null : s;
            awsProfileName = convertNull(awsProfileName, new string[] { "", "null", "none", "auto" });
            awsRegionName = convertNull(awsRegionName, new string[] { "", "null", "none" });

            awsCredentials = awsProfileName != null ? Credentials.Get(awsProfileName) : null;

            if (awsRegionName != null)
            {
                if (awsRegionName.ToLower() == "auto")
                {
                    autodetectRegion = true;
                }
                else
                {
                    awsRegion = RegionEndpoint.GetBySystemName(awsRegionName);
                }
            }
        }

        private AmazonS3Client GetClient(string s3url)
        {
            RegionEndpoint region = awsRegion;
            if (region == null && autodetectRegion)
            {
                region = GetRegion((new S3Url(s3url)).BucketName);
            }
            return GetClient(region);
        }

        private AmazonS3Client GetClient(RegionEndpoint region)
        {
            return regionToClient.GetOrAdd(region != null ? region.SystemName : "null", _ => {
                if (awsCredentials != null && region != null)
                {
                    return new AmazonS3Client(awsCredentials, region);
                }
                else if (region != null)
                {
                    return new AmazonS3Client(region);
                }
                else if (awsCredentials != null)
                {
                    return new AmazonS3Client(awsCredentials);
                }
                else
                {
                    return new AmazonS3Client();
                }
            });
        }

        public void Dispose()
        {
            foreach (var client in regionToClient.Values)
            {
                client.Dispose();
            }
            regionToClient.Clear();
        }

        /// <summary>
        /// Attempts to determine the region for a bucket given a bucket name
        /// Note that s3:GetBucketLocation must be allowed for this to succeed
        /// </summary>
        public RegionEndpoint GetRegion(string bucketName)
        {
            return bucketToRegion.GetOrAdd(bucketName, _ =>
                    {
                        Exception ex = null;
                        IEnumerable<RegionEndpoint> shortlist =
                        new [] { RegionEndpoint.USGovCloudWest1, RegionEndpoint.USWest1 };
                        //defaulting to try all regions works in theory but seems to be crazy slow in practice
                        foreach (var regions in new [] { shortlist /*, RegionEndpoint.EnumerableAllRegions*/ })
                        {
                            foreach (var region in regions)
                            {
                                try
                                {
                                    if (logger != null)
                                    {
                                        logger.InfoFormat("attempting to get region for bucket {0} using region {1}",
                                                          bucketName, region);
                                    }
                                    AmazonS3Client client = GetClient(region);
                                    GetBucketLocationRequest request = new GetBucketLocationRequest
                                    {
                                        BucketName = bucketName
                                    };
                                    GetBucketLocationResponse response = client.GetBucketLocation(request);
                                    return RegionEndpoint.GetBySystemName(response.Location);
                                }
                                catch (Exception e)
                                {
                                    if (logger != null)
                                    {
                                        logger.WarnFormat("failed to get region for bucket {0} using region {1}",
                                                          bucketName, region);
                                    }
                                    ex = e;
                                }
                            }
                        }
                        throw ex;
                    });
        }

        /// <summary>
        /// List objects in S3.
        /// </summary>
        /// <param name="s3url">An s3 url specifying the key prefix to search.  This can be a complete or partial
        /// object key. Must have trailing slash if its a folder.</param>
        /// <param name="pattern">Only return results matching this wildcard glob pattern (see
        /// StringHelper.WildCardToRegularExression()).</param>
        /// <param name="recursive">Return all keys with this s3url prefix if set to true.  If not stop at the next
        /// folder, delimited by a forward slash in the key.</param>
        /// <param name="ignoreCase">Apply pattern with case-insensitive matching</param>
        /// <param name="folders">Include folder URLs in output</param>
        /// <param name="files">Include file URLs in output</param>
        /// <returns>returns list of S3 URLs</returns>
        public IEnumerable<string> SearchObjects(string s3url, string pattern = "*", bool recursive = true,
                                                 bool ignoreCase = false, bool folders = false, bool files = true,
                                                 bool patternIsRegex = false, Func<string, bool> filter = null,
                                                 Action<string, long, DateTime> metadata = null)
        {
            S3Url location = new S3Url(s3url);
            var opts = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var regex = patternIsRegex ? new Regex(pattern, opts) :
                StringHelper.WildcardToRegularExpression(pattern, opts: opts);
            var client = GetClient(s3url);
            var request = new ListObjectsV2Request { BucketName = location.BucketName,
                                                     Encoding = new EncodingType("url") };
            if (location.Path.Length > 0)
            {
                request.Prefix = location.Path;
            }
            if (!recursive)
            {
                request.Delimiter = "/";
            }
            ListObjectsV2Response response = null;
            do
            {
                response = client.ListObjectsV2(request);
                if (folders)
                {
                    //CommonPrefixes should be plain text even when URL encoding is used
                    foreach (string pfx in response.CommonPrefixes)
                    {
                        if (regex.IsMatch(pfx))
                        {
                            string url = new S3Url(location.BucketName, pfx).Url;
                            if (filter == null || filter(url))
                            {
                                yield return url;
                            }
                        }
                    }
                }
                if (files)
                {
                    foreach (S3Object entry in response.S3Objects)
                    {
                        //bucket name have only of lowercase letters, numbers, dots, and hyphens
                        //but key can have any UTF-8 characters
                        //certain of those cause AmazonUnmarshallingException unless URL encoding is used
                        string key = WebUtility.UrlDecode(entry.Key);
                        if (regex.IsMatch(key))
                        {
                            string url = new S3Url(location.BucketName, key).Url;
                            if (filter == null || filter(url))
                            {
                                if (metadata != null)
                                {
                                    metadata(url, entry.Size, entry.LastModified);
                                }
                                yield return url;
                            }
                        }
                    }
                }
                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true);
        }

        private long GetFileSize(AmazonS3Client client, S3Url location)
        {
            return GetObjectMetadata(client, location).Headers.ContentLength;
        }

        public long FileSize(string s3url)
        {
            return GetFileSize(GetClient(s3url), new S3Url(s3url));
        }

        private DateTime GetLastModified(AmazonS3Client client, S3Url location)
        {
            return GetObjectMetadata(client, location).LastModified;
        }

        public DateTime LastModified(string s3url)
        {
            return GetLastModified(GetClient(s3url), new S3Url(s3url));
        }

        public bool FileExists(string s3url)
        {
            S3Url location = new S3Url(s3url);
            return new S3FileInfo(GetClient(s3url), location.BucketName, location.Path).Exists;
        }

        public bool FileSizeMatches(string s3url, string localfile)
        {
            if (!File.Exists(localfile))
            {
                return false;
            }
            return FileSizeMatches(GetClient(s3url), new S3Url(s3url), localfile);
        }

        /// <summary>
        /// Checks to see if the cloud file size matches the one on disk
        /// In the future this could be extended to include a timestamp check,
        /// but we would need to manually set the last modified time on local files
        /// to match the last modified cloud time whenever we download a file for this to work
        /// </summary>
        public bool FileSizeMatches(AmazonS3Client client, S3Url location, string localfile)
        {
            if (!File.Exists(localfile))
            {
                return false;
            }
            var cloudMeta = GetObjectMetadata(client, location);
            var localInfo = new FileInfo(localfile);
            long cloudSize = cloudMeta.Headers.ContentLength;
            //DateTime cloudTimestamp = cloudMeta.LastModified;
            long localSize = localInfo.Length;
            //DateTime localMod = localInfo.CreationTime;
            return cloudSize == localSize;
        }

        /// <summary>
        /// Download a file and save it to local disk
        /// </summary>
        public bool DownloadFile(string s3url, string filename, bool swallowExceptions = true, ILog logger = null)
        {
            try
            {
                var client = GetClient(s3url);
                S3Url location = new S3Url(s3url);
                using (TransferUtility tu = new TransferUtility(client))
                {
                    tu.Download(filename, location.BucketName, location.Path);
                }
                //this is a race condition if the file size can change on the server
                //return GetFileSize(client, location) == (new FileInfo(filename)).Length;
                return true;
            }
            catch (Exception e)
            {
                if (!swallowExceptions)
                {
                    throw;
                }
                else if (logger != null)
                {
                    logger.WarnFormat("error downloading S3 object {0}: {1}", s3url, e.Message);
                }
                return false;
            }
        }

        /// <summary>
        /// Download a directory and save it to local disk
        /// </summary>
        public void DownloadDirectory(string s3url, string directory)
        {
            S3Url location = new S3Url(s3url);
            using (TransferUtility tu = new TransferUtility(GetClient(s3url)))
            {
                tu.DownloadDirectory(location.BucketName, location.Path, directory);
            }
        }

        /// <summary>
        /// Upload a file from local disk
        /// </summary>
        public void UploadFile(string filename, string s3url)
        {
            S3Url location = new S3Url(s3url);
            using (TransferUtility tu = new TransferUtility(GetClient(s3url)))
            {
                UploadImpl(tu, filename, location.BucketName, location.Path);
            }
        }

        private void UploadImpl(TransferUtility tu, string localFile, string bucket, string key)
        {
            //tu.Upload(filename, location.BucketName, location.Path);

            //BucketOwnerFullControl is needed to allow writing to a bucket in a different account
            //which is happening in some deployments
            //
            //from the doc for S3CannedACL.BucketOwnerFullControl:
            //Object Owner gets FULL_CONTROL, Bucket Owner gets FULL_CONTROL.
            //This ACL applies only to objects and is equivalent to private when used with PUT Bucket.
            //You use this ACL to let someone other than the bucket owner write content (get full control)
            //in the bucket but still grant the bucket owner full rights over the objects.

            var req = new TransferUtilityUploadRequest();
            req.FilePath = localFile;
            req.BucketName = bucket;
            req.Key = key;
            req.CannedACL = S3CannedACL.BucketOwnerFullControl;

            tu.Upload(req);
        }

        /// <summary>
        /// Upload a file from local disk using a single thread
        /// S3 creates PUT notifications for each chunk of a file uploaded; using 
        /// a single thread results in only one PUT notification. 
        /// </summary>
        public void UploadFileSingleThread(string filename, string s3url)
        {
            S3Url location = new S3Url(s3url);
            var cfg = new TransferUtilityConfig { ConcurrentServiceRequests = 1 };
            using (TransferUtility tu = new TransferUtility(GetClient(s3url), cfg))
            {
                UploadImpl(tu, filename, location.BucketName, location.Path);
            }
        }

        /// <summary>
        /// Returns a stream to a file.  This stream does not download the entire file.
        /// streamHandler is called with the stream.  The caller does not need to wrap the 
        /// stream in a using statement.  Uses Amazons default API which is simple but slow.
        /// Consider using speed stream method instead
        /// </summary>
        public void GetStream(string s3url, Action<Stream> streamHandler)
        {
            TransferUtility tu = new TransferUtility(GetClient(s3url));
            S3Url location = new S3Url(s3url);
            using (var s = tu.OpenStream(location.BucketName, location.Path))
            {
                streamHandler(s);
            }
        }

        /// <summary>
        /// GetStream uses Amazon's TransferUtiltiy to get a stream.  This stream can outperform the default
        /// TransferUtility stream (especially on partial reads) because it supports different buffersizes
        /// </summary>
        public void GetStorageStream(string s3url, Action<Stream> streamHandler, long startPosition = 0,
                                     int bufferSize = 128 * 1024)
        {
            using (var s = new StorageStream(GetClient(s3url), s3url, startPosition, bufferSize))
            {
                streamHandler(s);
            }
        }

        /// <summary>
        /// Delete an object
        /// </summary>
        public bool DeleteObject(string s3url, bool ignoreErrors = true, ILog logger = null)
        {
            logger = logger ?? this.logger;
            try
            {
                S3Url location = new S3Url(s3url);
                GetClient(s3url).DeleteObject(location.BucketName, location.Path);
                return true;
            }
            catch (Exception e)
            {
                if (!ignoreErrors)
                {
                    throw;
                }
                else if (logger != null)
                {
                    logger.WarnFormat("error deleting S3 object {0}: {1}", s3url, e.Message);
                }
                return false;
            }
        }

        /// <summary>
        /// Delete a set of objects
        /// </summary>
        public bool DeleteObjects(string s3url, string pattern = "*", bool recursive = true,
                                  bool ignoreErrors = true, ILog logger = null)
        {
            logger = logger ?? this.logger;
            bool ok = true;
            try
            {
                IEnumerable<string> objects = SearchObjects(s3url, pattern, recursive);

                objects = objects.Where(obj => {
                        if (obj.StartsWith(s3url))
                        {
                            return true;
                        }
                        var msg = string.Format("suspicious glob result \"{0}\", should begin \"{1}\", not deleting",
                                                obj, s3url);
                        if (!ignoreErrors)
                        {
                            throw new System.Exception(msg);
                        }
                        else if (logger != null)
                        {
                            ok = false;
                            logger.Warn(msg);
                        }
                        return false;
                    });

                DeleteObjectsRequest request = new DeleteObjectsRequest
                {
                    BucketName = (new S3Url(s3url)).BucketName,
                    Objects = objects.Select(obj => new KeyVersion{ Key = (new S3Url(obj)).Path }).ToList()
                };
 
                GetClient(s3url).DeleteObjects(request);
            }
            catch (Exception e)
            {
                if (!ignoreErrors)
                {
                    throw;
                }
                else if (logger != null)
                {
                    ok = false;
                    logger.WarnFormat("error searching objects with prefix {0}: {1}", s3url, e.Message);
                }
            }
            return ok;
        }

        private GetObjectMetadataResponse GetObjectMetadata(AmazonS3Client client, S3Url location)
        {
            var getObjectMetadataRequest =
                new GetObjectMetadataRequest() { BucketName = location.BucketName, Key = location.Path };
            var meta = client.GetObjectMetadata(getObjectMetadataRequest);
            return meta;
        }

        public class StorageStream : Stream
        {
            delegate void ResponseHandler(GetObjectResponse response);

            private long position;
            private AmazonS3Client client;
            private S3Url location;
            private byte[] buffer;
            private int bytesInBuffer;
            private long positionAtBufferRefill;

            public StorageStream(AmazonS3Client client, string s3url, long startingPos, int bufferSize)
            {
                position = positionAtBufferRefill = startingPos;
                bytesInBuffer = 0;
                this.location = new S3Url(s3url);
                this.client = client;
                this.buffer = new byte[bufferSize];
            }

            private long GetSize()
            {
                var request = new ListObjectsV2Request()
                {
                    BucketName = location.BucketName,
                    Prefix = location.Path,
                    MaxKeys = 1
                };
                ListObjectsV2Response response = client.ListObjectsV2(request);
                if (response.S3Objects.Count != 1)
                {
                    throw new CloudException("No object found for url " + location.Url);
                }
                string key = response.S3Objects[0].Key;
                if (key != location.Path)
                {
                    throw new CloudException("Object key " + key + " + did not match url " + location.Url);
                }
                return response.S3Objects[0].Size;
            }

            private long GetObjectResponse(ByteRange range, ResponseHandler responseHandler)
            {
                GetObjectRequest request = new GetObjectRequest
                {
                    BucketName = location.BucketName,
                    Key = location.Path
                };
                request.ByteRange = range;

                try
                {
                    using (GetObjectResponse response = client.GetObject(request))
                    {
                        responseHandler(response);
                        return response.ContentLength;
                    }
                }
                catch (AmazonS3Exception e)
                {
                    // We have read off the end of the stream
                    if (e.ErrorCode == "InvalidRange" && range.Start != 0)
                    {
                        responseHandler(null);
                        return 0;
                    }
                    throw e;
                }
            }

            /// <summary>
            /// Returns number of bytes read into the buffer
            /// </summary>
            public long RefillBuffer()
            {
                // Byte range is inclusive so subtract to get the end byte to read
                long end = (position + buffer.Length) - 1;
                positionAtBufferRefill = position;
                long responseLength = GetObjectResponse(new ByteRange(position, end), response =>
                {
                    bytesInBuffer = 0;
                    if (response == null)
                    {
                        return;
                    }
                    using (var stream = response.ResponseStream)
                    {
                        int bytesRead;
                        do
                        {
                            bytesRead = stream.Read(buffer, bytesInBuffer, buffer.Length - bytesInBuffer);
                            bytesInBuffer += bytesRead;
                        } while (bytesRead != 0);
                    }
                });
                return bytesInBuffer;
            }

            public override bool CanRead
            {
                get
                {
                    return true;
                }
            }

            public override bool CanSeek
            {
                get
                {
                    return false;
                }
            }

            public override bool CanWrite
            {
                get
                {
                    return false;
                }
            }

            public override long Length
            {
                get
                {
                    throw new NotImplementedException();
                }
            }

            public override long Position
            {
                get
                {
                    return position;
                }

                set
                {
                    throw new NotImplementedException();
                }
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] output, int offset, int count)
            {
                // Stop reading if we reach count or the end of the file
                int totalRead = 0;
                if (count > 0)
                {
                    if ((position - positionAtBufferRefill) == bytesInBuffer)
                    {
                        if (RefillBuffer() == 0)
                        {
                            return 0;
                        }
                    }
                    int readPos = (int)(position - positionAtBufferRefill);
                    int available = bytesInBuffer - readPos;            // how much is left in current buffer
                    int bytesToRead = Math.Min(available, count);
                    Buffer.BlockCopy(this.buffer, readPos, output, offset, bytesToRead);
                    count -= bytesToRead;
                    offset += bytesToRead;
                    position += bytesToRead;
                    totalRead += bytesToRead;
                }
                return totalRead;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
    }
}

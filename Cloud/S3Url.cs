using System;

namespace JPLOPS.Cloud
{
    /// <summary>
    /// Represents an S3 url and can convert between the url form and a bucketname/prefix
    /// </summary>
    public class S3Url
    {
        public string BucketName { get; set; } //does not include s3:// or trailing slash

        public string Path { get; set; } //does not include leading slash

        public string Url
        {
            get
            {
                UriBuilder builder = new UriBuilder("s3", this.BucketName);
                builder.Path = this.Path;
                return builder.ToString();
            }

            set
            {
                Uri url = new Uri(value);
                this.BucketName = url.Host;
                this.Path = url.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped);
            }
        }

        public S3Url(string url)
        {
            this.Url = url;
        }

        public S3Url(string bucketName, string prefix)
        {
            this.BucketName = bucketName;
            this.Path = prefix;
        }
    }
}

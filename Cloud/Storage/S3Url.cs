using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    /// <summary>
    /// Represents an S3 url and can convert between the url form and a bucketname/prefix
    /// </summary>
    public class S3Url
    {

        public string BucketName { get; set; }
        public string Prefix { get; set; }
        public string Url
        {
            get
            {
                UriBuilder builder = new UriBuilder("s3", this.BucketName);
                builder.Path = this.Prefix;
                return builder.ToString();
            }

            set
            {
                Uri url = new Uri(value);
                this.BucketName = url.Host;
                this.Prefix = url.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped);
            }
        }

        public S3Url(string url)
        {
            this.Url = url;
        }

        public S3Url(string bucketName, string prefix)
        {
            this.BucketName = bucketName;
            this.Prefix = prefix;
        }
    }
}

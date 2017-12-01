using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Cloud;

namespace CloudTest
{
    [TestClass]
    public class S3UrlTest
    {
        [TestMethod]
        public void TestS3Url()
        {
            S3Url url = new S3Url("bucket", "prefix");
            Assert.AreEqual("s3://bucket/prefix", url.Url);
            url = new S3Url("bucket", "prefix/");
            Assert.AreEqual("s3://bucket/prefix/", url.Url);
            url = new S3Url("s3://bucket/prefix");
            Assert.AreEqual("bucket", url.BucketName);
            Assert.AreEqual("prefix", url.Prefix);
            url = new S3Url("s3://bucket/prefix/");
            Assert.AreEqual("bucket", url.BucketName);
            Assert.AreEqual("prefix/", url.Prefix);
            url.BucketName = "differentBucket";
            url.Prefix = "different/prefix/name/";
            Assert.AreEqual("s3://differentBucket/different/prefix/name/", url.Url);
            url.Url = "s3://mrbucket/is/buckets/of/fun/";
            Assert.AreEqual("mrbucket", url.BucketName);
            Assert.AreEqual("is/buckets/of/fun/", url.Prefix);
            url.Url = "s3://mrbucket/is/buckets/of/fun/file.gif";
            Assert.AreEqual("mrbucket", url.BucketName);
            Assert.AreEqual("is/buckets/of/fun/file.gif", url.Prefix);
        }
    }
}

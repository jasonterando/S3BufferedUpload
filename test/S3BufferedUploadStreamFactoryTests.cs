using S3BufferedUploads;
using Moq;
using NUnit.Framework;
using Amazon.S3;
using Amazon.S3.Model;

public class S3BufferedUploadStreamFactoryTests
{
    [Test]
    public void CreateWithBucketAndKey()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = S3BufferedUploadStreamFactory.Default.Create(s3ClientMock.Object, "Foo", "Bar", 7 * 1024 * 1024, 5 * 1024 * 1024);
        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual("Foo", stream._initMultipartRequest.BucketName);
        Assert.AreEqual("Bar", stream._initMultipartRequest.Key);
        Assert.AreEqual(7 * 1024 * 1024, stream._readBuffer.Capacity);
        Assert.AreEqual(5 * 1024 * 1024, stream._minSendTheshold);
    }

    [Test]
    public void CreateWithRequest()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = S3BufferedUploadStreamFactory.Default.Create(s3ClientMock.Object, new InitiateMultipartUploadRequest {
            BucketName = "Foo",
            Key = "Bar"
        }, 7 * 1024 * 1024, 5 * 1024 * 1024);
        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual("Foo", stream._initMultipartRequest.BucketName);
        Assert.AreEqual("Bar", stream._initMultipartRequest.Key);
        Assert.AreEqual(7 * 1024 * 1024, stream._readBuffer.Capacity);
        Assert.AreEqual(5 * 1024 * 1024, stream._minSendTheshold);
    }
}
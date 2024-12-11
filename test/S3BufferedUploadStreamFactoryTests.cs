using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using NUnit.Framework;
using S3BufferedUploads;

namespace S3BufferedUpload.Tests;

public class S3BufferedUploadStreamFactoryTests
{
    [Test]
    public void CreateWithChecksum()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream =
            S3BufferedUploadStreamFactory.Default.Create(s3ClientMock.Object, "Foo", "Bar", ChecksumAlgorithm.CRC32);
        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual(ChecksumAlgorithm.CRC32, stream._initMultipartRequest.ChecksumAlgorithm);
    }

    [Test]
    public void CreateWithBucketAndKey()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = S3BufferedUploadStreamFactory.Default.Create(s3ClientMock.Object, "Foo", "Bar",
            bufferCapacity: 7 * 1024 * 1024, minSendThreshold: 5 * 1024 * 1024);
        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual("Foo", stream._initMultipartRequest.BucketName);
        Assert.AreEqual("Bar", stream._initMultipartRequest.Key);
        Assert.AreEqual(7 * 1024 * 1024, stream._readBuffer.Capacity);
        Assert.AreEqual(5 * 1024 * 1024, stream._minSendTheshold);
        Assert.IsNull(stream._initMultipartRequest.ChecksumAlgorithm);
    }

    [Test]
    public void CreateWithRequest()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = S3BufferedUploadStreamFactory.Default.Create(s3ClientMock.Object,
            new InitiateMultipartUploadRequest
            {
                BucketName = "Foo",
                Key = "Bar",
                ChecksumAlgorithm = ChecksumAlgorithm.SHA1
            }, bufferCapacity: 7 * 1024 * 1024, minSendThreshold: 5 * 1024 * 1024);
        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual("Foo", stream._initMultipartRequest.BucketName);
        Assert.AreEqual("Bar", stream._initMultipartRequest.Key);
        Assert.AreEqual(7 * 1024 * 1024, stream._readBuffer.Capacity);
        Assert.AreEqual(5 * 1024 * 1024, stream._minSendTheshold);
        Assert.AreEqual(ChecksumAlgorithm.SHA1, stream._initMultipartRequest.ChecksumAlgorithm);
    }
}
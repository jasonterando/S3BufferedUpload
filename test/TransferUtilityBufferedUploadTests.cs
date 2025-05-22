using System.IO;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Moq;
using NUnit.Framework;
using S3BufferedUploads;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace S3BufferedUpload.Tests;

/// <summary>
/// TransferUtility extension method testing, can not run in parallel
/// because methods are static
/// </summary>
[NonParallelizable]
public class TransferUtilityBufferedUploadTests
{
    private IS3BufferedUploadStreamFactory? _saveFactory = null;
    private Mock<IS3BufferedUploadStreamFactory> _mockedFactory = new Mock<IS3BufferedUploadStreamFactory>();
    private Mock<IAmazonS3> _mockedS3Client = new Mock<IAmazonS3>();
    private Mock<S3BufferedUploadStream>? _mockedStream = null;
    private Mock<CompleteMultipartUploadResponse> _mockedUploadResponse = new Mock<CompleteMultipartUploadResponse>();

    const string TEST_S3_BUCKET = "My3Bucket";
    const string TEST_S3_KEY = "MyS3Key";

    private string _testFilePath = "";
    private string _testText = "TEST";

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.GetTempFileName();
        var writer = new StreamWriter(_testFilePath);
        writer.Write(_testText);
        writer.Close();
    }

    [TearDown]
    public void Teardown()
    {
        if (_saveFactory != null)
        {
            S3BufferedUploadStreamFactory.Default = _saveFactory;
            _saveFactory = null;
        }
        if (_testFilePath.Length > 0) {
            File.Delete(_testFilePath);
            _testFilePath = "";
        }
    }

    [Test]
    public void TestFilePathSucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        Assert.AreEqual(_mockedUploadResponse.Object, utility.UploadBuffered(_testFilePath, TEST_S3_BUCKET, TEST_S3_KEY));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public void TestStreamSucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        Assert.AreEqual(_mockedUploadResponse.Object, utility.UploadBuffered(source, TEST_S3_BUCKET, TEST_S3_KEY));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public void TestFilePathWithNoKeySucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        Assert.AreEqual(_mockedUploadResponse.Object, utility.UploadBuffered(_testFilePath, TEST_S3_BUCKET));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(Path.GetFileName(_testFilePath), _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public void TestStreamWithRequestSucceedsIfComplete()
    {
        // xxx
        var utility = new TransferUtility(_mockedS3Client.Object);

        SetupMockedS3BufferedStream();
        // _mockedStream?.Setup(m => m.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>())).CallBase();

        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = TEST_S3_BUCKET,
            Key = TEST_S3_KEY
        };
        Assert.AreEqual(_mockedUploadResponse.Object, utility.UploadBuffered(source, request));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public void TestFileWithRequestSucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = TEST_S3_BUCKET,
            Key = TEST_S3_KEY
        };
        Assert.AreEqual(_mockedUploadResponse.Object, utility.UploadBuffered(_testFilePath, request));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public async Task TestFilePathAsyncSucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        Assert.AreEqual(_mockedUploadResponse.Object, await utility.UploadBufferedAsync(_testFilePath, TEST_S3_BUCKET, TEST_S3_KEY));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public async Task TestStreamAsyncSucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        Assert.AreEqual(_mockedUploadResponse.Object, await utility.UploadBufferedAsync(source, TEST_S3_BUCKET, TEST_S3_KEY));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public async Task TestFilePathAsyncWithNoKeySucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        Assert.AreEqual(_mockedUploadResponse.Object, await utility.UploadBufferedAsync(_testFilePath, TEST_S3_BUCKET));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(Path.GetFileName(_testFilePath), _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public async Task TestStreamWithRequestAsyncSucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = TEST_S3_BUCKET,
            Key = TEST_S3_KEY
        };
        Assert.AreEqual(_mockedUploadResponse.Object, await utility.UploadBufferedAsync(source, request));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public async Task TestFileWithRequestAsyncSucceedsIfComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream();
        var source = new MemoryStream();
        source.Write(Encoding.ASCII.GetBytes(_testText));
        source.Position = 0;
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = TEST_S3_BUCKET,
            Key = TEST_S3_KEY
        };
        Assert.AreEqual(_mockedUploadResponse.Object, await utility.UploadBufferedAsync(_testFilePath, request));
        Assert.AreEqual(TEST_S3_BUCKET, _mockedUploadResponse.Object.BucketName);
        Assert.AreEqual(TEST_S3_KEY, _mockedUploadResponse.Object.Key);
        _mockedStream?.Verify((m => m.Close()), Times.Once);
        _mockedStream?.Verify(m => m.Write(It.IsAny<byte[]>(), 0, _testText.Length), Times.Once);
    }

    [Test]
    public void TestFaultsIfNotComplete()
    {
        var utility = new TransferUtility(_mockedS3Client.Object);
        SetupMockedS3BufferedStream(false);
        Assert.Catch(() => utility.UploadBuffered(_testFilePath, TEST_S3_BUCKET, TEST_S3_KEY), "Transfer was not completed");
    }

    private void SetupMockedS3BufferedStream(bool markAsComplete = true)
    {
        var finishSetup = (IAmazonS3 s3Client, InitiateMultipartUploadRequest request, int bufferCapacity, int minSendThreshold) =>
        {
            _mockedStream = new Mock<S3BufferedUploadStream>(s3Client, request,
                S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);

            _mockedS3Client
                .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<InitiateMultipartUploadResponse>())
                .Verifiable();
            _mockedS3Client
                .Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<UploadPartResponse>())
                .Verifiable();
            _mockedS3Client
                .Setup(m => m.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompleteMultipartUploadRequest request, CancellationToken cancellation) => {
                    if (markAsComplete)
                    {
                        _mockedStream.Object._completeResponse = _mockedUploadResponse.Object;
                    }
                    return _mockedStream.Object._completeResponse;
                })
                .Verifiable();

            _mockedUploadResponse.Object.BucketName = request.BucketName;
            _mockedUploadResponse.Object.Key = request.Key;

            // var completedActions = new List<Action<S3BufferedUploadStream, CompleteMultipartUploadResponse>>();
            _mockedStream.CallBase = false;
            _mockedStream.SetupAllProperties().CallBase = true;
            _mockedStream.Setup(m => m.CanWrite).Returns(true);
            _mockedStream.Setup(m => m.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .CallBase();
            return _mockedStream.Object;
        };


        _mockedFactory.CallBase = false;
        _mockedFactory.Setup(m => m.Create(It.IsAny<IAmazonS3>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((IAmazonS3 s3Client, string s3Bucket, string s3Key, int bufferCapacity, int minSendThreshold) =>
            {
                return finishSetup(s3Client, new InitiateMultipartUploadRequest {
                    BucketName = s3Bucket,
                    Key = s3Key
                }, bufferCapacity, minSendThreshold);
            });
        _mockedFactory.Setup(m => m.Create(It.IsAny<IAmazonS3>(), It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((IAmazonS3 s3Client, InitiateMultipartUploadRequest request, int bufferCapacity, int minSendThreshold) =>
            {
                return finishSetup(s3Client, request, bufferCapacity, minSendThreshold);
            });

        _saveFactory = S3BufferedUploadStreamFactory.Default;
        S3BufferedUploadStreamFactory.Default = _mockedFactory.Object;
    }

}


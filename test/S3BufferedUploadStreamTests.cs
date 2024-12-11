using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using NUnit.Framework;
using S3BufferedUploads;

namespace S3BufferedUpload.Tests;

public class S3BufferedUploadStreamTests
{
    private const string S3_BUCKET_NAME = "MYBUCKET";
    private const string S3_KEY = "MYKEY";

    [Test]
    public void CanConstructWithBucketKeyAndDefaults()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);

        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual(S3_BUCKET_NAME, stream._initMultipartRequest.BucketName);
        Assert.AreEqual(S3_KEY, stream._initMultipartRequest.Key);
        Assert.AreEqual(S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, stream._readBuffer?.Capacity);
        Assert.AreEqual(0, stream._readBuffer?.Position);
        Assert.AreEqual(S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD, stream._minSendTheshold);
        Assert.IsNull(stream._initMultipartRequest.ChecksumAlgorithm);
    }
    
    [Test]
    public void CanConstructWithBucketKeyAndChecksumAlgorithm()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, ChecksumAlgorithm.SHA1);

        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual(S3_BUCKET_NAME, stream._initMultipartRequest.BucketName);
        Assert.AreEqual(ChecksumAlgorithm.SHA1, stream._initMultipartRequest.ChecksumAlgorithm);
        Assert.AreEqual(S3_KEY, stream._initMultipartRequest.Key);
        Assert.AreEqual(S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, stream._readBuffer?.Capacity);
        Assert.AreEqual(0, stream._readBuffer?.Position);
        Assert.AreEqual(S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD, stream._minSendTheshold);
    }

    [Test]
    public void CanConstructWithBucketKeyAndOverrides()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: 7 * 1024 * 1024,
            minSendThreshold: 5 * 1024 * 1024
        );

        Assert.AreEqual(s3ClientMock.Object, stream._s3Client);
        Assert.AreEqual(S3_BUCKET_NAME, stream._initMultipartRequest.BucketName);
        Assert.AreEqual(S3_KEY, stream._initMultipartRequest.Key);
        Assert.AreEqual(7 * 1024 * 1024, stream._readBuffer?.Capacity);
        Assert.AreEqual(0, stream._readBuffer?.Position);
        Assert.AreEqual(5 * 1024 * 1024, stream._minSendTheshold);
    }

    [Test]
    public void ConstructFaultsOnInvalidParameters()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        Assert.Catch<ArgumentException>(
            () => new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, bufferCapacity: 4096, minSendThreshold: -1),
            "Minimum send threhold must be greater than zero");
        Assert.Catch<ArgumentException>(
            () => new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, bufferCapacity: 4 * 1024 * 1024, minSendThreshold: 5 * 1024 * 1024),
            "Buffer capacity must be at at least the minimum send threshold");
    }

    [Test]
    public void DisposeCallsCleanup()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var mockStream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        mockStream.CallBase = true;
        mockStream.Object._initiateResponse = new InitiateMultipartUploadResponse();
        mockStream.Setup(m => m.Cleanup()).Verifiable();
        mockStream.Object.Dispose();
        mockStream.Verify(m => m.Cleanup(), Times.Once());
    }

    [Test]
    public void CloseCallsAbortUploadIfCancelled()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var mockStream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        mockStream.CallBase = true;
        mockStream.Object._initiateResponse = new InitiateMultipartUploadResponse();
        mockStream.Setup(m => m.Cleanup()).Verifiable();
        mockStream.Object.Close();
        mockStream.Verify(m => m.Cleanup(), Times.Once());
    }


    [Test]
    public void CleanupCallsCompleteUploadIfNotCancelled()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var mockStream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        mockStream.CallBase = true;
        mockStream.Object._initiateResponse = new InitiateMultipartUploadResponse();
        mockStream.Setup(m => m.CompleteUpload()).Verifiable();
        mockStream.Object.Dispose();
        mockStream.Verify(m => m.CompleteUpload(), Times.Once());
    }

    [Test]
    public void CleanupCallsAbortUploadIfCancelled()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var mockStream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        mockStream.CallBase = true;
        mockStream.Object._initiateResponse = new InitiateMultipartUploadResponse();
        mockStream.Setup(m => m.AbortUpload()).Verifiable();
        mockStream.Object.Cancel();
        mockStream.Object.Dispose();
        mockStream.Verify(m => m.AbortUpload(), Times.Once());
    }


    [Test]
    public void WriteCallsInitiateOnFirstCall()
    {
        InitiateMultipartUploadResponse? initiateResponse = null;

        var mockResponse = Mock.Of<InitiateMultipartUploadResponse>();
        mockResponse.BucketName = S3_BUCKET_NAME;
        mockResponse.Key = S3_KEY;
        mockResponse.UploadId = Guid.NewGuid().ToString();

        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock
            .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream.Initiated += (stream, response) =>
        {
            initiateResponse = response;
        };
        stream.Write(new byte[] { 1 }, 0, 1);
        Assert.NotNull(initiateResponse);
        Assert.AreEqual(S3BufferedUploadStream.StateType.Uploading, stream.State);
    }

    [Test]
    public void WriteDoesCallsInitiateOnFirstCallIfCancelled()
    {
        var mockResponse = Mock.Of<InitiateMultipartUploadResponse>();
        mockResponse.BucketName = S3_BUCKET_NAME;
        mockResponse.Key = S3_KEY;
        mockResponse.UploadId = Guid.NewGuid().ToString();

        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock
            .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream.Initiated += (stream, response) =>
        {
            Assert.Fail("Initiated should not have been triggered");
        };
        stream.Cancel();
        stream.Write(new byte[] { 1 }, 0, 1);
        Assert.AreEqual(true, stream.IsCancellationRequested);
        Assert.AreEqual(S3BufferedUploadStream.StateType.Uninitiated, stream.State);
    }

    [Test]
    public void WriteShouldOnlyCallInitiateOnce()
    {
        InitiateMultipartUploadResponse? initiateResponse = null;

        var mockResponse = Mock.Of<InitiateMultipartUploadResponse>();
        mockResponse.BucketName = S3_BUCKET_NAME;
        mockResponse.Key = S3_KEY;
        mockResponse.UploadId = Guid.NewGuid().ToString();

        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock
            .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream.Initiated += (stream, response) =>
        {
            if (initiateResponse == null)
            {
                initiateResponse = response;
            }
            else
            {
                Assert.Fail("Initiate should only be called once");
            }
        };
        stream.Write(new byte[] { 1 }, 0, 1);
        stream.Write(new byte[] { 2 }, 0, 1);
        Assert.NotNull(initiateResponse);
        Assert.AreEqual(S3BufferedUploadStream.StateType.Uploading, stream.State);
    }

    [Test]
    public void WriteShoulNotMakeInitiateCallbackIfCancelled()
    {
        var mockResponse = Mock.Of<InitiateMultipartUploadResponse>();
        mockResponse.BucketName = S3_BUCKET_NAME;
        mockResponse.Key = S3_KEY;
        mockResponse.UploadId = Guid.NewGuid().ToString();

        S3BufferedUploadStream? stream = null;

        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock
            .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { stream?.Cancel(); return mockResponse; });

        stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream.Initiated += (stream, response) =>
        {
            Assert.Fail("Initiate should not be called if cancelled");
        };
        stream.Cancel();
        stream.Write(new byte[] { 1 }, 0, 1);
    }

    [Test]
    public void WriteShoulFaultIfStreamCompleted()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock
            .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<InitiateMultipartUploadResponse>());

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream._completeResponse = new CompleteMultipartUploadResponse();
        Assert.Catch<Exception>(() => stream.Write(new byte[] { 1 }), "S3 write has already been completed");
    }

    [Test]
    public void WriteCallsPerformFlushWhenMinSendThresholdReached()
    {
        var bytesToS3 = 0L;

        const int testBufferSize = 128;
        var testBuffer = new byte[testBufferSize];
        var rnd = new Random();
        rnd.NextBytes(testBuffer);
        var resultBuffer = new byte[testBufferSize];
        var resultBufferCount = 0;
        var s3WriteCount = 0;

        var mockResponse = Mock.Of<InitiateMultipartUploadResponse>();
        mockResponse.BucketName = S3_BUCKET_NAME;
        mockResponse.Key = S3_KEY;
        mockResponse.UploadId = Guid.NewGuid().ToString();

        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock
            .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        s3ClientMock
            .Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                Assert.AreEqual(mockResponse.BucketName, request.BucketName);
                Assert.AreEqual(mockResponse.Key, request.Key);
                Assert.AreEqual(mockResponse.UploadId, request.UploadId);

                bytesToS3 += request.PartSize;
                request.InputStream.Read(resultBuffer, resultBufferCount, (int)request.PartSize);
                resultBufferCount += (int)request.PartSize;
                s3WriteCount++;
                return Mock.Of<UploadPartResponse>();
            });

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream._minSendTheshold = testBufferSize;
        stream.Write(testBuffer);
        Assert.AreEqual(testBuffer[0..(testBufferSize - 1)], resultBuffer[0..(testBufferSize - 1)]);
        Assert.AreEqual(bytesToS3, stream.Length);
    }

    [Test]
    public void WriteCallsPerformFlushOnSecondCallWithSmallChunks()
    {
        var bytesToS3 = 0L;

        const int testBufferSize = 128;
        var testBuffer = new byte[testBufferSize];
        var rnd = new Random();
        rnd.NextBytes(testBuffer);
        var resultBuffer = new byte[testBufferSize];
        var resultBufferCount = 0;
        var s3WriteCount = 0;

        var mockResponse = Mock.Of<InitiateMultipartUploadResponse>();
        mockResponse.BucketName = S3_BUCKET_NAME;
        mockResponse.Key = S3_KEY;
        mockResponse.UploadId = Guid.NewGuid().ToString();

        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock
            .Setup(m => m.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        s3ClientMock
            .Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                Assert.AreEqual(mockResponse.BucketName, request.BucketName);
                Assert.AreEqual(mockResponse.Key, request.Key);
                Assert.AreEqual(mockResponse.UploadId, request.UploadId);

                bytesToS3 += request.PartSize;
                request.InputStream.Read(resultBuffer, resultBufferCount, (int)request.PartSize);
                resultBufferCount += (int)request.PartSize;
                s3WriteCount++;
                return Mock.Of<UploadPartResponse>();
            });

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream._minSendTheshold = testBufferSize;
        stream.Write(testBuffer, 0, testBufferSize / 2);
        Assert.AreEqual(0, stream.Length);
        stream.Write(testBuffer, testBufferSize / 2, testBufferSize - (testBufferSize / 2));
        Assert.AreEqual(testBuffer[0..(testBufferSize - 1)], resultBuffer[0..(testBufferSize - 1)]);
        Assert.AreEqual(bytesToS3, stream.Length);
    }


    [Test]
    public void FlushShouldCallPerformFlush()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.CallBase = false;
        stream.Setup(m => m.Flush()).CallBase();
        stream.Setup(m => m.PerformFlush(false)).Verifiable();
        stream.Object.Flush();
        stream.Verify(m => m.PerformFlush(false), Times.Once);
    }

    [Test]
    public async Task FlushAsyncShouldCallPerformFlush()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.CallBase = false;
        stream.Setup(m => m.FlushAsync(CancellationToken.None)).CallBase();
        stream.Setup(m => m.PerformFlush(false)).Verifiable();
        await stream.Object.FlushAsync();
        stream.Verify(m => m.PerformFlush(false), Times.Once);
    }

    
    [Test]
    public async Task PerformFlushShouldExitIfNotInitialized()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream._readBuffer.Position = 1;
        await stream.PerformFlush(false);
        // First thing that perform flush does is to reset position
        Assert.AreEqual(1, stream._readBuffer.Position);
    }

    [Test]
    public async Task PerformFlushShouldExitIfCompleted()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._completeResponse = new CompleteMultipartUploadResponse();
        stream._readBuffer.Position = 1;
        await stream.PerformFlush(false);
        // First thing that perform flush does is to reset position
        Assert.AreEqual(1, stream._readBuffer.Position);
    }

    // [Test]
    // public async Task PerformFlushShouldExitIfBufferEmptyAndNotLastPart()
    // {
    //     var s3ClientMock = new Mock<IAmazonS3>();
    //     var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
    //     stream._initiateResponse = new InitiateMultipartUploadResponse();
    //     stream._readBuffer.Position = 1;
    //     await stream.PerformFlush(false);
    //     // First thing that perform flush does is to reset position
    //     Assert.AreEqual(1, stream._readBuffer.Position);
    // }

    [Test]
    public async Task PerformFlushShouldReserveLastPartIfEncryptingAndNotLastPart()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPartResponse());

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, Mock.Of<InitiateMultipartUploadRequest>());
        stream._minSendTheshold = 10;
        stream.IsEncrypting = true;
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._initiateResponse = Mock.Of<InitiateMultipartUploadResponse>();
        stream._readBuffer.Write(new byte[11], 0, 11);
        await stream.PerformFlush(false);
        Assert.AreEqual(1, stream._readBuffer.Position);
        Assert.AreEqual(1, stream._readBuffer.Length);
    }

    [Test]
    public async Task PerformFlushShouldNotReserveLastPartIfEncryptingAndLastPart()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPartResponse());

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, Mock.Of<InitiateMultipartUploadRequest>());
        stream._minSendTheshold = 10;
        stream.IsEncrypting = true;
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._initiateResponse = Mock.Of<InitiateMultipartUploadResponse>();
        stream._readBuffer.Write(new byte[11], 0, 11);
        await stream.PerformFlush(true);
        Assert.AreEqual(0, stream._readBuffer.Position);
        Assert.AreEqual(0, stream._readBuffer.Length);
    }

    [Test]
    public async Task PerformFlushShouldNotReserveLastPartIfNotEncrypting()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPartResponse());

        var stream = new S3BufferedUploadStream(s3ClientMock.Object, Mock.Of<InitiateMultipartUploadRequest>());
        stream._minSendTheshold = 10;
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._initiateResponse = Mock.Of<InitiateMultipartUploadResponse>();
        stream._readBuffer.Write(new byte[11], 0, 11);
        await stream.PerformFlush(false);
        Assert.AreEqual(0, stream._readBuffer.Position);
        Assert.AreEqual(0, stream._readBuffer.Length);
    }

    [Test]
    public async Task PerformFlushShouldExitIfCancelled()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._readBuffer.Write(new byte[1], 0, 1);
        stream.Cancel();
        await stream.PerformFlush(false);
        // part tags should not be set if cancelled
        Assert.AreEqual(0, stream._partETags.Count);
    }

    [Test]
    public void PerformFlushShouldExitIfCancelledFromUploadPart()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken cancellation) =>
            {
                stream._cancellation.Cancel();
                return Mock.Of<UploadPartResponse>();
            });
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._minSendTheshold = 10;
        stream._readBuffer.Write(new byte[11], 0, 11);
        Assert.ThrowsAsync<TaskCanceledException>(async () => await stream.PerformFlush(false));
    }

    [Test]
    public async Task PerformFlushSetsUpAndCallsStreamTransferProgress()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);
        stream._minSendTheshold = 1;

        bool streamProgressCalled = false;
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                if (request.StreamTransferProgress != null)
                {
                    request.StreamTransferProgress(stream, new StreamTransferProgressArgs(0, 0, 0));
                }
                return new UploadPartResponse();
            });
        stream.StreamTransfer += (S3BufferedUploadStream _stream, StreamTransferProgressArgs args) =>
        {
            streamProgressCalled = true;
        };
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._readBuffer.Write(new byte[1], 0, 1);
        await stream.PerformFlush(false);
        Assert.AreEqual(true, streamProgressCalled);
    }

    [Test]
    public async Task PerformFlushIgnoresStreamTransferProgressIfNoData()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY);

        bool streamProgressCalled = false;
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                if (request.StreamTransferProgress != null)
                {
                    request.StreamTransferProgress(stream, new StreamTransferProgressArgs(0, 0, 0));
                }
                return new UploadPartResponse();
            });
        stream.StreamTransfer += (S3BufferedUploadStream _stream, StreamTransferProgressArgs args) =>
        {
            streamProgressCalled = true;
        };
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        await stream.PerformFlush(true);
        Assert.AreEqual(false, streamProgressCalled);
    }

    [Test]
    public async Task PerformFlushIgnoresStreamTransferProgressIfNoCallabck()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);

        bool streamProgressDefined = false;
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                streamProgressDefined = (request.StreamTransferProgress != null);
                return new UploadPartResponse();
            });
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._readBuffer.Write(new byte[] { 1 }, 0, 1);
        await stream.PerformFlush(true);
        Assert.AreEqual(false, streamProgressDefined);
    }

    [Test]
    public async Task PerformFlushIgnoresStreamTransferProgressIfNoDataAndNoCallabck()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);

        bool streamProgressDefined = false;
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                streamProgressDefined = (request.StreamTransferProgress != null);
                return new UploadPartResponse();
            });
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        await stream.PerformFlush(true);
        Assert.AreEqual(false, streamProgressDefined);
    }

    [Test]
    public async Task PerformFlushStopsAfterUploadPartCancel()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var uploadedPartCalled = false;
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.UploadedPart += (stream, _cancel) =>
        {
            uploadedPartCalled = true;
        };

        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                stream.Cancel();
                return new UploadPartResponse();
            });
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream._readBuffer.Write(new byte[] { 1 }, 0, 1);
        await stream.PerformFlush(true);
        Assert.AreEqual(false, uploadedPartCalled);
    }

    [Test]
    public async Task PerformFlushHooksUploadedPart()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var uploadedPartCalled = false;
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.UploadedPart += (stream, _cancel) =>
        {
            uploadedPartCalled = true;
        };

        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                return new UploadPartResponse();
            });
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._readBuffer.Write(new byte[] { 1 }, 0, 1);
        await stream.PerformFlush(true);
        Assert.AreEqual(true, uploadedPartCalled);
    }

    [Test]
    public async Task PerformFlushUpdatesETagsAndBuffer()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        s3ClientMock.Setup(m => m.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _token) =>
            {
                return new UploadPartResponse
                {
                    ETag = "Foo",
                    PartNumber = 1
                };
            });
        stream._initiateResponse = new InitiateMultipartUploadResponse();
        stream.State = S3BufferedUploadStream.StateType.Uploading;
        stream._readBuffer.Write(new byte[] { 1 }, 0, 1);
        await stream.PerformFlush(true);
        Assert.AreEqual(1, stream._partETags.Count);
        Assert.AreEqual("Foo", stream._partETags[0].ETag);
        Assert.AreEqual(1, stream._partETags[0].PartNumber);
        Assert.AreEqual(1, stream._bytesUploaded);
        Assert.AreEqual(0, stream._readBuffer.Length);
        Assert.AreEqual(0, stream._readBuffer.Position);
    }

    [Test]
    public void IsCancelledRequestedReturnsTrueIfCancelled()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.AreEqual(false, stream.IsCancellationRequested);
        stream.Cancel();
        Assert.AreEqual(true, stream.IsCancellationRequested);
    }


    [Test]
    public async Task CompleteUploadExitsIfNotInitialized()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.CallBase = true;
        stream.Setup(m => m.PerformFlush(It.IsAny<bool>())).Verifiable();
        await stream.Object.CompleteUpload();
        stream.Verify(m => m.PerformFlush(It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task CompleteUploadExitsIfCompleted()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Object._completeResponse = new CompleteMultipartUploadResponse();
        stream.CallBase = true;
        stream.Setup(m => m.PerformFlush(It.IsAny<bool>())).Verifiable();
        await stream.Object.CompleteUpload();
        stream.Verify(m => m.PerformFlush(It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public void CompleteUploadExitsIfCancelled()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.CallBase = true;
        stream.Object.Cancel();
        stream.Setup(m => m.PerformFlush(It.IsAny<bool>())).Verifiable();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await stream.Object.CompleteUpload());
    }

    [Test]
    public async Task CompleteUploadCallsPerformFlush()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.CallBase = false;
        stream.Object._initiateResponse = new InitiateMultipartUploadResponse();
        stream.Object.State = S3BufferedUploadStream.StateType.Uploading;
        stream.Setup(m => m.PerformFlush(It.IsAny<bool>())).Verifiable();
        stream.Setup(m => m.CompleteUpload()).CallBase();
        await stream.Object.CompleteUpload();
        stream.Verify(m => m.PerformFlush(It.IsAny<bool>()), Times.Once);
    }

    [Test]
    public async Task CompleteUploadCallsGetsUploadParams()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.CallBase = false;
        stream.Object._initiateResponse = new InitiateMultipartUploadResponse
        {
            BucketName = "Foo",
            Key = "Bar",
            UploadId = "12345",
        };
        stream.Object.State = S3BufferedUploadStream.StateType.Uploading;
        var completeResponse = new CompleteMultipartUploadResponse
        {
            BucketName = stream.Object._initiateResponse.BucketName,
            Key = stream.Object._initiateResponse.Key
        };
        s3ClientMock.Setup(m => m.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(completeResponse);

        stream.Object._partETags = new List<PartETag>() { new PartETag { ETag = "ABC", PartNumber = 1 } };
        stream.Setup(m => m.CompleteUpload()).CallBase();
        await stream.Object.CompleteUpload();
        Assert.AreSame(completeResponse, stream.Object._completeResponse);
    }


    [Test]
    public async Task AbortUploadExitsIftateIsNotUploading()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Object.State = S3BufferedUploadStream.StateType.Completed;
        stream.Object._initiateResponse = Mock.Of<InitiateMultipartUploadResponse>();
        stream.Setup(m => m.AbortUpload()).CallBase();
        await stream.Object.AbortUpload();
        stream.Verify(m => m.SetAborted(It.IsAny<AbortMultipartUploadResponse>()), Times.Never);
    }

    [Test]
    public async Task AbortUploadExitsIfNoInitiateResponse()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Setup(m => m.AbortUpload()).CallBase();
        await stream.Object.AbortUpload();
        stream.Verify(m => m.SetAborted(It.IsAny<AbortMultipartUploadResponse>()), Times.Never);
    }

    [Test]
    public async Task AbortUploadCallsSetAborted()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new Mock<S3BufferedUploadStream>(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY, null,
            S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Object._initiateResponse = Mock.Of<InitiateMultipartUploadResponse>();
        stream.Object.State = S3BufferedUploadStream.StateType.Uploading;
        stream.Setup(m => m.SetAborted(It.IsAny<AbortMultipartUploadResponse>())).Verifiable();
        stream.Setup(m => m.AbortUpload()).CallBase();
        await stream.Object.AbortUpload();
        stream.Verify(m => m.SetAborted(It.IsAny<AbortMultipartUploadResponse>()), Times.Once);
    }

    [Test]
    public void SetInitiatedPopulatesAndSetsEncryptingToTrueIfEncrypting()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var called = false;
        var response = new InitiateMultipartUploadResponse();
        response.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Initiated += ((stream, cancel) => { called = true; });
        stream.SetInitiated(response);
        Assert.AreEqual(response, stream._initiateResponse);
        Assert.AreEqual(true, called);
        Assert.AreEqual(true, stream.IsEncrypting);
        Assert.AreEqual(S3BufferedUploadStream.StateType.Uploading, stream.State);
    }

    [Test]
    public void SetInitiatedPopulatesAndSetsEncryptingToFalseIfNotEncrypting()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var called = false;
        var response = new InitiateMultipartUploadResponse();
        response.ServerSideEncryptionMethod = ServerSideEncryptionMethod.None;
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Initiated += ((stream, cancel) => { called = true; });
        stream.SetInitiated(response);
        Assert.AreEqual(response, stream._initiateResponse);
        Assert.AreEqual(true, called);
        Assert.AreEqual(false, stream.IsEncrypting);
        Assert.AreEqual(S3BufferedUploadStream.StateType.Uploading, stream.State);
    }

    [Test]
    public void SetCompletdSetsValueAndTriggersEvent()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var called = false;
        var response = new CompleteMultipartUploadResponse();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Completed += ((stream, cancel) => { called = true; });
        stream.SetCompleted(response);
        Assert.AreEqual(response, stream._completeResponse);
        Assert.AreEqual(true, called);
        Assert.AreEqual(S3BufferedUploadStream.StateType.Completed, stream.State);
    }

    [Test]
    public void SetAbortedSetsValueAndTriggersEvent()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var called = false;
        var response = new AbortMultipartUploadResponse();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        stream.Aborted += ((stream, cancel) => { called = true; });
        stream.SetAborted(response);
        Assert.AreEqual(response, stream._abortResponse);
        Assert.AreEqual(true, called);
        Assert.AreEqual(S3BufferedUploadStream.StateType.Aborted, stream.State);
    }

    [Test]
    public void CanReadIsFalse()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.AreEqual(false, stream.CanRead);
    }

    [Test]
    public void CanSeekIsFalse()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.AreEqual(false, stream.CanSeek);
    }


    [Test]
    public void CanWriteIsTrue()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.AreEqual(true, stream.CanWrite);
    }

    [Test]
    public void PositionThrowsNotImplemented()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.Catch<NotImplementedException>(() => stream.Position = 0);
        Assert.Catch<NotImplementedException>(() => { var i = stream.Position; });
    }

    [Test]
    public void ReadThrowsNotImplemented()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.Catch<NotImplementedException>(() => { var foo = new byte[1]; stream.Read(foo, 0, 1); });
    }

    [Test]
    public void SeekThrowsNotImplemented()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold:S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.Catch<NotImplementedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }


    [Test]
    public void SetLengthNotImplemented()
    {
        var s3ClientMock = new Mock<IAmazonS3>();
        var stream = new S3BufferedUploadStream(s3ClientMock.Object, S3_BUCKET_NAME, S3_KEY,
            bufferCapacity: S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, minSendThreshold: S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
        Assert.Catch<NotImplementedException>(() => stream.SetLength(0));
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace S3BufferedUploads;

public class S3BufferedUploadStream : Stream, IDisposable
{
    public enum StateType {
        Uninitiated,
        Uploading,
        Completed,
        Aborted
    }

    public const int MINIMUM_SEND_THRESHOLD = 5242880;
    public const int DEFAULT_READ_BUFFER_CAPACITY = MINIMUM_SEND_THRESHOLD * 3;
    public const int DEFAULT_MIN_SEND_THRESHOLD = MINIMUM_SEND_THRESHOLD;

    public event Action<S3BufferedUploadStream, InitiateMultipartUploadResponse>? Initiated;
    public event Action<S3BufferedUploadStream, UploadPartResponse>? UploadedPart;
    public event Action<S3BufferedUploadStream, StreamTransferProgressArgs>? StreamTransfer;
    public event Action<S3BufferedUploadStream, CompleteMultipartUploadResponse>? Completed;
    public event Action<S3BufferedUploadStream, AbortMultipartUploadResponse>? Aborted;

    protected internal IAmazonS3 _s3Client;
    protected internal InitiateMultipartUploadRequest _initMultipartRequest;
    protected internal Int32 _minSendTheshold;
    protected internal Int32 _s3PartNumber = 1;
    protected internal long _bytesUploaded;

    protected internal MemoryStream _readBuffer = new();

    protected internal List<PartETag> _partETags = new();
    protected internal InitiateMultipartUploadResponse? _initiateResponse;
    protected internal CompleteMultipartUploadResponse? _completeResponse;
    protected internal AbortMultipartUploadResponse? _abortResponse;

    protected internal readonly SemaphoreLocker _locker = new();
    protected internal CancellationTokenSource _cancellation = new();

    public StateType State { protected internal set; get; } = StateType.Uninitiated;
    public bool IsEncrypting { protected internal set; get; }

    /// <summary>
    /// Convenience constructor that creates an upload request for the specified S3 bucket and key
    /// </summary>
    /// <param name="s3Client"></param>
    /// <param name="bucketName"></param>
    /// <param name="key"></param>
    /// <param name="bufferCapacity"></param>
    /// <param name="minSendThreshold"></param>
    /// <exception cref="ArgumentException"></exception>
    public S3BufferedUploadStream(IAmazonS3 s3Client, string bucketName, string key,
        int bufferCapacity = DEFAULT_READ_BUFFER_CAPACITY, int minSendThreshold = DEFAULT_MIN_SEND_THRESHOLD) : this
        (
            s3Client,
            new InitiateMultipartUploadRequest
            {
                BucketName = bucketName,
                Key = key
            },
            bufferCapacity,
            minSendThreshold
        )
    {
    }

    /// <summary>
    /// Validate parameters and initialize buffer
    /// </summary>
    /// <param name="s3Client"></param>
    /// <param name="request"></param>
    /// <param name="bufferCapacity"></param>
    /// <param name="minSendThreshold"></param>
    /// <exception cref="ArgumentException"></exception>
    public S3BufferedUploadStream(IAmazonS3 s3Client, InitiateMultipartUploadRequest request,
        int bufferCapacity = DEFAULT_READ_BUFFER_CAPACITY, int minSendThreshold = DEFAULT_MIN_SEND_THRESHOLD)
    {
        if (minSendThreshold < MINIMUM_SEND_THRESHOLD)
        {
            throw new ArgumentException($"Minimum send threshold must be at least {MINIMUM_SEND_THRESHOLD}");
        }
        if (bufferCapacity < minSendThreshold)
        {
            throw new ArgumentException($"Buffer capacity must be at at least the minimum send threshold");
        }
        _s3Client = s3Client;
        _initMultipartRequest = request;
        _readBuffer.Capacity = bufferCapacity;
        _readBuffer.Position = 0;
        _minSendTheshold = minSendThreshold;
    }

    /// <summary>
    /// Triggers a complete upload operation (if upload in progress)
    /// </summary>
    void IDisposable.Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Triggers a complete upload operation (if upload in progress)
    /// </summary>
    public override void Close()
    {
        base.Close();
        Cleanup();
    }

    /// <summary>
    /// Ensure stream is completed or aborted if not already done so
    /// </summary>
    protected internal virtual void Cleanup()
    {
        Task.Run(async () =>
        {
            if (_cancellation.IsCancellationRequested)
            {
                await AbortUpload();
            }
            else
            {
                await CompleteUpload();
            }
        }).Wait();
    }

    /// <summary>
    /// Write to S3 via buffer
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <exception cref="Exception"></exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        Task.Run(async () =>
        {
            await _locker.LockAsync(async () =>
            {
                if (_cancellation.IsCancellationRequested
                    || State == StateType.Completed
                    || State == StateType.Aborted)
                {
                    return;
                }

                if (_initiateResponse == null)
                {
                    SetInitiated(await _s3Client.InitiateMultipartUploadAsync(_initMultipartRequest, _cancellation.Token));
                }

                if (_completeResponse != null)
                {
                    throw new Exception("S3 write has already been completed");
                }

                while (count > 0)
                {
                    var bytesAvailable = _readBuffer.Capacity - _readBuffer.Length;
                    var bytesToWrite = (int)Math.Min(count, bytesAvailable);
                    _readBuffer.Write(buffer, offset, bytesToWrite);
                    count -= bytesToWrite;
                    offset += bytesToWrite;
                    if (_readBuffer.Length >= _minSendTheshold)
                    {
                        await PerformFlush();
                    }
                }
            });
        }).Wait();
    }

    /// <summary>
    /// Write anything still in buffer to S3
    /// </summary>
    public override void Flush()
    {
        Task.Run(async() => await _locker.LockAsync(async () =>
        {
            await PerformFlush();
        })).Wait();
    }


    /// <summary>
    /// Write anything still in buffer to S3
    /// </summary>
    /// <param name="cancellationToken"></param>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        var registration = cancellationToken.Register(() =>
        {
            _cancellation.Cancel();
        });
        try
        {
            await _locker.LockAsync(async () => { await PerformFlush(); });
        }
        finally
        {
            await registration.DisposeAsync();
        }
    }

    /// <summary>
    /// This actually performs the write of any buffered content to S3
    /// </summary>
    /// <returns></returns>
    protected internal virtual async Task PerformFlush(bool isLastPart = false)
    {
        var hasData = _readBuffer.Length > 0;
        if (_cancellation.IsCancellationRequested
            || State != StateType.Uploading
            || _initiateResponse == null
            || (!(isLastPart || hasData)))
        {
            return;
        }

        // If we are encrypting not on the last part, reserve a byte
        // so that we can be ensured we will have a last part, which
        // is required for encryption.  This allows us to support
        // encryption for streams that are non-seekable/unknown length
        var reserveLastByte = (IsEncrypting && (!isLastPart));
        var partSize = reserveLastByte
            ? _readBuffer.Length - 1
            : _readBuffer.Length ;

        if (partSize >= (isLastPart ? 0 : _minSendTheshold))
        {
            var uploadPartRequest = new UploadPartRequest
            {
                BucketName = _initiateResponse.BucketName,
                Key = _initiateResponse.Key,
                UploadId = _initiateResponse.UploadId,
                InputStream = _readBuffer,
                PartSize = partSize,
                PartNumber = _s3PartNumber++,
                IsLastPart = isLastPart
            };

            if (hasData && (StreamTransfer != null))
            {
                uploadPartRequest.StreamTransferProgress += (_, progressArgs) =>
                {
                    StreamTransfer(this, progressArgs);
                };
            }

            _readBuffer.Position = 0;
            var partResponse = await _s3Client.UploadPartAsync(uploadPartRequest, _cancellation.Token);
            if (_cancellation.IsCancellationRequested)
            {
                throw new TaskCanceledException();
            }
            if (UploadedPart != null)
            {
                UploadedPart(this, partResponse);
            }

            _partETags.Add(new PartETag { PartNumber = partResponse.PartNumber, ETag = partResponse.ETag });
            _bytesUploaded += partSize;

            if (reserveLastByte) {
                var buffer = new byte[1];
                var bytesRead = _readBuffer.Read(buffer, 0, 1);
                _readBuffer.SetLength(0);
                _readBuffer.Position = 0;
                if (bytesRead > 0)
                {
                    _readBuffer.Write(buffer, 0, bytesRead);
                }
            } else
            {
                _readBuffer.SetLength(0);
                _readBuffer.Position = 0;
            }
        }
    }

    /// <summary>
    /// Call to cancel ongoing operations
    /// </summary>
    public virtual void Cancel()
    {
        if (!_cancellation.IsCancellationRequested)
        {
            _cancellation.Cancel();
        }
    }

    /// <summary>
    /// Returns true if cancellation requested
    /// </summary>
    public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

    /// <summary>
    /// Trigger the complete multipart upload call to S3
    /// </summary>
    protected internal virtual async Task CompleteUpload()
    {
        await _locker.LockAsync(async () =>
        {
            if (_cancellation.IsCancellationRequested)
            {
                throw new TaskCanceledException();
            }
            if (State != StateType.Uploading
                || _initiateResponse == null
            )
            {
                return;
            }

            await PerformFlush(true);

            SetCompleted(await _s3Client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                BucketName = _initiateResponse.BucketName,
                Key = _initiateResponse.Key,
                UploadId = _initiateResponse.UploadId,
                PartETags = _partETags
            }, _cancellation.Token));
        });
    }

    /// <summary>
    /// Trigger the abort of a multipart upload call to S3
    /// </summary>
    protected internal virtual async Task AbortUpload()
    {
        await _locker.LockAsync(async () =>
        {
            if (State != StateType.Uploading
                || _initiateResponse == null
            )
            {
                return;
            }

            SetAborted(await _s3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
            {
                BucketName = _initiateResponse.BucketName,
                Key = _initiateResponse.Key,
                UploadId = _initiateResponse.UploadId,
            }, _cancellation.Token));
        });
    }

    /// <summary>
    /// Set initiated response and trigger notifications
    /// </summary>
    /// <param name="response"></param>
    protected internal void SetInitiated(InitiateMultipartUploadResponse response)
    {
        _initiateResponse = response;
        State = StateType.Uploading;
        IsEncrypting = (_initiateResponse.ServerSideEncryptionMethod != null 
            && _initiateResponse.ServerSideEncryptionMethod.Value != ServerSideEncryptionMethod.None);
        Initiated?.Invoke(this, _initiateResponse);
    }

    /// <summary>
    /// Set completed response and trigger notifications
    /// </summary>
    /// <param name="response"></param>
    protected internal void SetCompleted(CompleteMultipartUploadResponse response)
    {
        _completeResponse = response;
        State = StateType.Completed;
        Completed?.Invoke(this, _completeResponse);
    }

    /// <summary>
    /// Set aborted response and trigger notifications
    /// </summary>
    /// <param name="response"></param>
    protected internal virtual void SetAborted(AbortMultipartUploadResponse response)
    {
        _abortResponse = response;
        State = StateType.Aborted;
        Aborted?.Invoke(this, _abortResponse);
    }

    /// <summary>
    /// This stream type cannot be read from
    /// </summary>
    public override bool CanRead => false;

    /// <summary>
    /// This stream type does not support seeking
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// This stream can be written to
    /// </summary>
    public override bool CanWrite => true;

    /// <summary>
    /// Returns how many bytes have been uploaded (so far)
    /// </summary>
    public override long Length => _bytesUploaded;

    /// <summary>
    /// This stream type does not support the Position property
    /// </summary>
    public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    /// <summary>
    /// This stream type does not support Read operations
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// This stream type does not support Seek operations
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="origin"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// This stream type does not support SetLength operations
    /// </summary>
    /// <param name="value"></param>
    /// <exception cref="NotImplementedException"></exception>
    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }
}

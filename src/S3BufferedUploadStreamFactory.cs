namespace S3BufferedUploads;

using Amazon.S3;
using Amazon.S3.Model;

/// <summary>
/// Factory for generating S3BufferedUploadStream objects
/// </summary>
public class S3BufferedUploadStreamFactory: IS3BufferedUploadStreamFactory
{
    /// <summary>
    /// Overridable default factory to facilitate unit testing
    /// </summary>
    public static IS3BufferedUploadStreamFactory Default = new S3BufferedUploadStreamFactory();

    /// <summary>
    /// Create an S3BufferedUploadStream for a specified bucket and key
    /// </summary>
    /// <param name="s3Client"></param>
    /// <param name="bucketName"></param>
    /// <param name="key"></param>
    /// <param name="bufferCapacity"></param>
    /// <param name="minSendThreshold"></param>
    /// <returns>S3BufferedUploadStream</returns>
    public S3BufferedUploadStream Create(IAmazonS3 s3Client, string bucketName, string key,
        int bufferCapacity = S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, 
        int minSendThreshold = S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD)
    {
        return new S3BufferedUploadStream(s3Client, bucketName, key, bufferCapacity, minSendThreshold);
    }

    /// <summary>
    /// Create an S3BufferedUploadStream for a multiplate upload request
    /// </summary>
    /// <param name="s3Client"></param>
    /// <param name="request"></param>
    /// <param name="bufferCapacity"></param>
    /// <param name="minSendThreshold"></param>
    /// <returns></returns>
    public S3BufferedUploadStream Create(IAmazonS3 s3Client, InitiateMultipartUploadRequest request,
        int bufferCapacity = S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY, 
        int minSendThreshold = S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD)
    {
        return new S3BufferedUploadStream(s3Client, request, bufferCapacity, minSendThreshold);
    }
}

public interface IS3BufferedUploadStreamFactory
{
    /// <summary>
    /// Create an S3BufferedUploadStream for a specified bucket and key
    /// </summary>
    /// <param name="s3Client"></param>
    /// <param name="bucketName"></param>
    /// <param name="key"></param>
    /// <param name="bufferCapacity"></param>
    /// <param name="minSendThreshold"></param>
    /// <returns>S3BufferedUploadStream</returns>
    S3BufferedUploadStream Create(IAmazonS3 s3Client, string bucketName, string key,
        int bufferCapacity = S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY,
        int minSendThreshold = S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);

     /// <summary>
    /// Create an S3BufferedUploadStream for a multiplate upload request
    /// </summary>
    /// <param name="s3Client"></param>
    /// <param name="request"></param>
    /// <param name="bufferCapacity"></param>
    /// <param name="minSendThreshold"></param>
    /// <returns>S3BufferedUploadStream</returns>
   S3BufferedUploadStream Create(IAmazonS3 s3Client, InitiateMultipartUploadRequest request,
        int bufferCapacity = S3BufferedUploadStream.DEFAULT_READ_BUFFER_CAPACITY,
        int minSendThreshold = S3BufferedUploadStream.DEFAULT_MIN_SEND_THRESHOLD);
}
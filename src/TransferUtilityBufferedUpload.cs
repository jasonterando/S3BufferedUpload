using System.IO;
using System.Threading.Tasks;
using Amazon.S3.Model;
using S3BufferedUploads;

namespace Amazon.S3.Transfer
{
    /// <summary>
    /// Extensions to the AWS S3 TransferUtility class to support buffered uploading
    /// </summary>
    public static class TransferUtilityBufferedUpload
    {
        /// <summary>
        /// Upload stream to S3 using buffering synchronously
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="stream">Any stream (seekable or not)</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="key">S3 bucket key</param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static CompleteMultipartUploadResponse UploadBuffered(this TransferUtility utility, Stream stream, string bucketName, string key)
        {
            using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, bucketName, key))
            {
                return ExecuteBufferedUpload(s3, stream);
            }
        }

        /// <summary>
        /// Upload file to S3 using buffering synchronously
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="filePath">Name of file to upload</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="key">S3 bucket key</param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static CompleteMultipartUploadResponse UploadBuffered(this TransferUtility utility, string filePath, string bucketName, string key)
        {
            using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, bucketName, key))
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                return ExecuteBufferedUpload(s3, stream);
            }
        }

        /// <summary>
        /// Upload file to S3 using buffering synchronously, using file name as key
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="filePath">Name of file to upload</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static CompleteMultipartUploadResponse UploadBuffered(this TransferUtility utility, string filePath, string bucketName)
        {
            using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, bucketName, Path.GetFileName(filePath)))
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                return ExecuteBufferedUpload(s3, stream);
            }
        }

        /// <summary>
        /// Upload stream to S3 using buffering synchronously, specifying upload request parameters
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="stream">Any stream (seekable or not)</param>
        /// <param name="request">S3 Multipart upload request parameters </param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static CompleteMultipartUploadResponse UploadBuffered(this TransferUtility utility, Stream stream, InitiateMultipartUploadRequest request)
        {
            using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, request))
            {
                return ExecuteBufferedUpload(s3, stream);
            }
        }

        /// <summary>
        /// Upload file to S3 using buffering synchronously, specifying upload request parameters
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="filePath">Name of file to upload</param>
        /// <param name="request">S3 Multipart upload request parameters </param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static CompleteMultipartUploadResponse UploadBuffered(this TransferUtility utility, string filePath, InitiateMultipartUploadRequest request)
        {
            using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, request))
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                return ExecuteBufferedUpload(s3, stream);
            }
        }

        /// <summary>
        /// Upload stream to S3 using buffering asynchronously
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="stream">Any stream (seekable or not)</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="key">S3 bucket key</param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static Task<CompleteMultipartUploadResponse> UploadBufferedAsync(this TransferUtility utility, Stream stream, string bucketName, string key)
        {
            return Task.Run(() =>
            {
                using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, bucketName, key))
                {
                    return ExecuteBufferedUpload(s3, stream);
                }
            });
        }

        /// <summary>
        /// Upload file to S3 using buffering asynchronously
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="filePath">Name of file to upload</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="key">S3 bucket key</param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static Task<CompleteMultipartUploadResponse> UploadBufferedAsync(this TransferUtility utility, string filePath, string bucketName, string key)
        {
            return Task.Run(() =>
            {
                using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, bucketName, key))
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    return ExecuteBufferedUpload(s3, stream);
                }
            });
        }

        /// <summary>
        /// Upload file to S3 using buffering asynchronously, using file name as key
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="filePath">Name of file to upload</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static Task<CompleteMultipartUploadResponse> UploadBufferedAsync(this TransferUtility utility, string filePath, string bucketName)
        {
            return Task.Run(() =>
            {
                using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, bucketName, Path.GetFileName(filePath)))
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    return ExecuteBufferedUpload(s3, stream);
                }
            });
        }

        /// <summary>
        /// Upload stream to S3 using buffering asynchronously, specifying upload request parameters
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="stream">Any stream (seekable or not)</param>
        /// <param name="request">S3 Multipart upload request parameters </param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static Task<CompleteMultipartUploadResponse> UploadBufferedAsync(this TransferUtility utility, Stream stream, InitiateMultipartUploadRequest request)
        {
            return Task.Run(() =>
            {
                using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, request))
                {
                    return ExecuteBufferedUpload(s3, stream);
                }
            });
        }

        /// <summary>
        /// Upload file to S3 using buffering asynchronously, specifying upload request parameters
        /// </summary>
        /// <param name="_utility">(Unused)</param>
        /// <param name="filePath">Name of file to upload</param>
        /// <param name="request">S3 Multipart upload request parameters </param>
        /// <returns>S3 CompleteMultipartUploadResponse</returns>
        public static Task<CompleteMultipartUploadResponse> UploadBufferedAsync(this TransferUtility utility, string filePath, InitiateMultipartUploadRequest request)
        {
            return Task.Run(() =>
            {
                using (var s3 = S3BufferedUploadStreamFactory.Default.Create(utility.S3Client, request))
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    return ExecuteBufferedUpload(s3, stream);
                }
            });
        }

        /// <summary>
        /// Copies the source stream to the S3 stream, returning a completed multipart upload response
        /// </summary>
        /// <param name="s3BufferedUploadStream"></param>
        /// <param name="sourceStream"></param>
        /// <returns></returns>
        /// <exception cref="System.Exception"></exception>
        public static CompleteMultipartUploadResponse ExecuteBufferedUpload(S3BufferedUploadStream s3BufferedUploadStream, Stream sourceStream)
        {
            CompleteMultipartUploadResponse? completeMultipartUploadResponse = null;
            s3BufferedUploadStream.Completed += (stream, response) =>
            {
                completeMultipartUploadResponse = response;
            };
            sourceStream.CopyTo(s3BufferedUploadStream);
            s3BufferedUploadStream.Close();
            return completeMultipartUploadResponse == null 
                ? throw new System.Exception("Transfer was not completed")
                : completeMultipartUploadResponse;            
        }
    }
}

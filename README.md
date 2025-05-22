# S3BufferedUpload

This assembly supports the buffered uploading of streams much like the AWS TransferUtility upload methods.  However, it also supports non-seekable streams (such as the piped output from a console application).

## Project Status

Howdy, found out some people were actually using this :)  Per their requests, I've switched the license to MIT, updated things AWS SDK to v4, and added .NET Framework and current .NET Core as targets.

With all that said, I am no longer "hands-on" with .NET.  I will every so often peek in on this project and try and keep it up to date, but won't be supporting new features, at least any time soon.  If you would like to contribute or even take over the project, let me know. 

## Usage

There are two mechanisms to use the functionality:

1. By adding this assembly to your project, you will have a number of **UploadBuffered** and **UploadBufferedAsync** functions added to the TransferUtility class.  These operate similarly to their TransferUtility Upload counterparts, except that they will work with any readable stream, not just seekable ones.
2. You can also use the **S3BufferedUploadStream** class directly.  This stream is a writable "front-end" to an S3 upload. 

### Using TransferUtility UploadBuffered/UploadBufferedAsync Functions

All of these functions do the same thing, they facilitate the upload of a stream to an S3 bucket.  They differ only in that some versions of the functions support passing a file path, as opposed to a stream; or accepting an S3 bucket name and key, as opposed to an initiate multipart upload request.  If you pass in a stream, you should set the position to zero (or wherever you want to copy to S3 to start from).

Each of these functions return a CompleteMultipartUploadResponse containing information about the newly created S3 object.

You'll need to have your AWS environment variables, .credentials, etc. set up as you normally would.

```c#
void UploadStream(Stream inputStream, string s3BucketName, string s3Key)
{
    using (var s3Client = new AmazonS3Client())
    {
        var utility = new TransferUtility(s3Client);
        utility.UploadBuffered(inputStream, s3BucketName, s3Key);
    }
}

// or...

async void UploadStream(Stream inputStream, string s3BucketName, string s3Key)
{
    using (var s3Client = new AmazonS3Client())
    {
        var utility = new TransferUtility(s3Client);
        await utility.UploadBuffered(inputStream, s3BucketName, s3Key);
    }
}

// or...

void UploadStream(string filePath, string s3BucketName, string s3Key)
{
    using (var s3Client = new AmazonS3Client())
    {
        var utility = new TransferUtility(s3Client);
        utility.UploadBuffered(filePath, s3BucketName, s3Key);
    }
}

// etc.

```

### Using S3BufferedUploadStream

You can also create a S3BufferedUploadStream object, which lets you use set up an S3 destination as a stream output.  You also get options on how large to set the buffer, how long the object waits to send data to S3 (based upon bytes) and a number of events to track progress and ability to cancel an in-progress transfer.

This stream is only writable, it may not be read from and will throw NotImplementedException on attempts to read, manipulate position, etc.  S3BufferedUploadStream.Length will return the number of bytes uploaded to S3.

There is a factory called **S3BufferedUploadStreamFactory** to make the stream more dependency injection friendly.

To create the S3BufferedUploadStream, you can pass either the S3 bucket name and key or, if you need more control over how the object is created, you can pass an **InitiateMultiPartUpload** request.

```c#
S3BufferedUploadStream(IAmazonS3 s3Client, string bucketName, string key,
        int bufferCapacity = DEFAULT_READ_BUFFER_CAPACITY, int minSendThreshold = DEFAULT_MIN_SEND_THRESHOLD)
S3BufferedUploadStream(IAmazonS3 s3Client, InitiateMultipartUploadRequest request,
        int bufferCapacity = DEFAULT_READ_BUFFER_CAPACITY, int minSendThreshold = DEFAULT_MIN_SEND_THRESHOLD)
```

Once the stream is is created, you simply write to it and once all data is written, either call Close or dispose of the stream to ensure all bytes are written to the S3 object.

#### Buffering

The stream will buffer data (based upon the **bufferCapacity** value) and then dump the buffer once **minSendThreshold** is reached.  This should help performance and mitigate "backflow" problems.  You can manipulate these values when creating the stream to tune performance.  You can call the **Flush** method to flush the buffer to S3 at any time.

> **Always be sure to Close or Dispose of this stream when done with it to automatically flush any buffered contents.**

#### Thread Safety

A semaphore-based locker is set up for each stream to make it as thread-safe as possible, which also makes the calls non re-entrant.  Basically, don't write to the same stream from different threads. (Note: didn't used Mutex because of numerous inferences that it was not safe to use with async-style mechanisms).

## Testing

To run coverage reporting, you'll need to install the .NET report generator:

```shell
dotnet new tool-manifest
dotnet tool install dotnet-reportgenerator-globaltool
```

An example of how to execute the tests and coverage reports:

```shell
dotnet test /p:CollectCoverage=true 
```

The script **runtests.sh** demonstrates a CI/CD pipeline friendly mechanism to execute tests, determine coverage and generate a coverage report. It will exit with a
non-zero code if tests fail and/or coverage is under the specified thresholds.

## Demonstration

There is a project on [GitHub](https://github.com/jasonterando/TeeStreamingDemo) demonstrating how to tee stream uploads to S3.
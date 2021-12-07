namespace S3BufferedUploads;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// This class implements an async-friendly locking mechanism which accepts
/// either a void or typed/generic callback
/// </summary>
public class SemaphoreLocker
{
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    public const int DEFAULT_SEMAPHORE_TIMEOUT = 60000;
    public int SemaphoreTimeout { get; private set; }

    public SemaphoreLocker(int semaphoreTimeout = DEFAULT_SEMAPHORE_TIMEOUT)
    {
        if (semaphoreTimeout < 1)
        {
            throw new ArgumentException("Semaphore timeout must be greater than zero");
        }
        SemaphoreTimeout = semaphoreTimeout;
    }

    /// <summary>
    /// Lock the thread contet for the worker specified in the callback
    /// </summary>
    /// <param name="worker"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task LockAsync(Func<Task> worker)
    {
        if (!await _semaphore.WaitAsync(SemaphoreTimeout))
        {
            throw new Exception("Unable to lock context");
        }
        try
        {
            await worker();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// /// Lock the thread contet for the typed result worker specified in the callback
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="worker"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<T> LockAsync<T>(Func<Task<T>> worker)
    {
        if (!await _semaphore.WaitAsync(SemaphoreTimeout))
        {
            throw new Exception("Unable to lock context");
        }
        try
        {
            return await worker();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

using System;
using NUnit.Framework;
using System.Threading.Tasks;
using S3BufferedUploads;

namespace test;

public class SemaphoreLockerTests
{
    [Test]
    public void CanCreateWithDefaultTimeout()
    {
        var locker = new SemaphoreLocker();
        Assert.AreEqual(SemaphoreLocker.DEFAULT_SEMAPHORE_TIMEOUT, locker.SemaphoreTimeout);
    }

    [Test]
    public void CanCreateWithSpecifiedTimeout()
    {
        var locker = new SemaphoreLocker(1000);
        Assert.AreEqual(1000, locker.SemaphoreTimeout);
    }

    [Test]
    public void FaultsWithInvalidTimeout()
    {
        Assert.Catch<ArgumentException>(() =>
        {
            new SemaphoreLocker(-1);
        }, "Semaphore timeout must be greater than zero");
    }

    [Test]
    public async Task CanGetContextsSequentially()
    {
        var locker = new SemaphoreLocker();
        var tally = 0;
        for (var i = 0; i < 5; i++)
        {
            await locker.LockAsync(() => Task.Run(() => tally++ ));
        }
        Assert.AreEqual(5, tally);
    }

    [Test]
    public async Task FaultsOnNestedContextes()
    {
        var locker = new SemaphoreLocker(100);
        var foo = 0;
        await locker.LockAsync(() => Task.Run(() =>
        {
            Assert.CatchAsync(() => locker.LockAsync(() => Task.Run(() => foo = 1)), 
                "Unable to get thread context");
        }));
        Assert.AreEqual(0, foo);
    }
}
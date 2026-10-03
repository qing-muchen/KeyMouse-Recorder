using MacroRecorder.Core.Time;

namespace MacroRecorder.Core.Tests.TestDoubles;

internal sealed class BlockingMonotonicClock(long initialMicroseconds = 0) : IMonotonicClock, IDisposable
{
    private readonly ManualResetEventSlim readEntered = new(false);
    private readonly ManualResetEventSlim releaseRead = new(false);
    private long currentMicroseconds = initialMicroseconds;
    private int blockNextRead;

    public long GetTimestampMicroseconds()
    {
        if (Interlocked.Exchange(ref blockNextRead, 0) == 1)
        {
            readEntered.Set();
            if (!releaseRead.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The test did not release the blocked clock read.");
            }
        }

        return Interlocked.Read(ref currentMicroseconds);
    }

    public void SetMicroseconds(long value) => Interlocked.Exchange(ref currentMicroseconds, value);

    public void BlockNextRead()
    {
        readEntered.Reset();
        releaseRead.Reset();
        Interlocked.Exchange(ref blockNextRead, 1);
    }

    public bool WaitUntilReadEntered(TimeSpan timeout) => readEntered.Wait(timeout);

    public void ReleaseRead() => releaseRead.Set();

    public void Dispose()
    {
        readEntered.Dispose();
        releaseRead.Dispose();
    }
}

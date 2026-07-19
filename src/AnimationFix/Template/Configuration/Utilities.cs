using System.Diagnostics;

namespace TimeStranger.AnimationFix.Template.Configuration;

public static class Utilities
{
    public static T TryGetValue<T>(Func<T> getValue, int timeout, int sleepTime, CancellationToken token = default)
        where T : new()
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeout)
        {
            if (token.IsCancellationRequested) return new T();
            try
            {
                return getValue();
            }
            catch
            {
                Thread.Sleep(sleepTime);
            }
        }

        throw new Exception($"Timeout limit {timeout} exceeded.");
    }
}

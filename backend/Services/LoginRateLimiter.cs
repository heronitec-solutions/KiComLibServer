using System.Collections.Concurrent;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Counts failed sign-ins per client address and username.
    /// A success clears that pair. The window is in memory and resets with the process.
    /// </summary>
    public sealed class LoginRateLimiter
    {
        public const int FailureLimit = 8;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        private readonly ConcurrentDictionary<string, Queue<DateTime>> _failures = new(StringComparer.Ordinal);

        public bool IsBlocked(string key)
        {
            if (!_failures.TryGetValue(key, out Queue<DateTime>? window))
            {
                return false;
            }

            lock (window)
            {
                Trim(window, DateTime.UtcNow);
                return window.Count >= FailureLimit;
            }
        }

        public void RecordFailure(string key)
        {
            Queue<DateTime> window = _failures.GetOrAdd(key, _ => new Queue<DateTime>());
            lock (window)
            {
                DateTime now = DateTime.UtcNow;
                Trim(window, now);
                window.Enqueue(now);
            }
        }

        public void Clear(string key)
        {
            _failures.TryRemove(key, out _);
        }

        public static string Key(string? remoteAddress, string username)
        {
            string address = string.IsNullOrWhiteSpace(remoteAddress) ? "unknown" : remoteAddress.Trim();
            return address + "\n" + username.Trim().ToLowerInvariant();
        }

        private static void Trim(Queue<DateTime> window, DateTime now)
        {
            while (window.Count > 0 && now - window.Peek() >= Window)
            {
                window.Dequeue();
            }
        }
    }
}

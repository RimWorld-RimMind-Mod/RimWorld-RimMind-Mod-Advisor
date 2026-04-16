namespace RimMind.Advisor.Concurrency
{
    /// <summary>
    /// 全局并发计数器，防止同时过多 Pawn 等待 AI 响应。
    /// 线程安全（Interlocked）。
    /// </summary>
    public static class AdvisorConcurrencyTracker
    {
        private static int _active;

        public static int ActiveCount => _active;

        public static void Increment() =>
            System.Threading.Interlocked.Increment(ref _active);

        public static void Decrement() =>
            System.Threading.Interlocked.Decrement(ref _active);
    }
}

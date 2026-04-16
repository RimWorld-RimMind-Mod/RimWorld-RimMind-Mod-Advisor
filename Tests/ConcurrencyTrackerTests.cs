using System.Threading.Tasks;
using RimMind.Advisor.Concurrency;
using Xunit;

// 测试并发计数器，不依赖 RimWorld
namespace RimMind.Advisor.Tests
{
    public class ConcurrencyTrackerTests
    {
        // ── 每个测试独立：通过在测试开始时重置到 0 ──────────────────────────
        // 注意：AdvisorConcurrencyTracker 是静态的，需要每个测试前后清理

        private void ResetToZero()
        {
            // 强制将计数器归零（对测试隔离）
            while (AdvisorConcurrencyTracker.ActiveCount > 0)
                AdvisorConcurrencyTracker.Decrement();
        }

        // ── 1. 初始状态为 0 ───────────────────────────────────────────────

        [Fact]
        public void InitialCount_IsZero()
        {
            ResetToZero();
            Assert.Equal(0, AdvisorConcurrencyTracker.ActiveCount);
        }

        // ── 2. Increment 递增 ─────────────────────────────────────────────

        [Fact]
        public void Increment_IncreasesCount()
        {
            ResetToZero();
            AdvisorConcurrencyTracker.Increment();
            Assert.Equal(1, AdvisorConcurrencyTracker.ActiveCount);
            AdvisorConcurrencyTracker.Decrement(); // 清理
        }

        // ── 3. Decrement 递减 ─────────────────────────────────────────────

        [Fact]
        public void Decrement_DecreasesCount()
        {
            ResetToZero();
            AdvisorConcurrencyTracker.Increment();
            AdvisorConcurrencyTracker.Increment();
            AdvisorConcurrencyTracker.Decrement();
            Assert.Equal(1, AdvisorConcurrencyTracker.ActiveCount);
            AdvisorConcurrencyTracker.Decrement(); // 清理
        }

        // ── 4. 多线程并发 Increment 不丢失 ────────────────────────────────

        [Fact]
        public void ConcurrentIncrements_AreThreadSafe()
        {
            ResetToZero();
            const int count = 100;

            Parallel.For(0, count, _ => AdvisorConcurrencyTracker.Increment());

            Assert.Equal(count, AdvisorConcurrencyTracker.ActiveCount);

            // 清理
            Parallel.For(0, count, _ => AdvisorConcurrencyTracker.Decrement());
            Assert.Equal(0, AdvisorConcurrencyTracker.ActiveCount);
        }

        // ── 5. Increment + Decrement 配对后归零 ───────────────────────────

        [Fact]
        public void IncrementThenDecrement_ReturnsToZero()
        {
            ResetToZero();
            for (int i = 0; i < 5; i++) AdvisorConcurrencyTracker.Increment();
            for (int i = 0; i < 5; i++) AdvisorConcurrencyTracker.Decrement();
            Assert.Equal(0, AdvisorConcurrencyTracker.ActiveCount);
        }
    }
}

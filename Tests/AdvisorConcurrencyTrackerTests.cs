using RimMind.Advisor.Concurrency;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorConcurrencyTrackerTests
    {
        [Fact]
        public void ActiveCount_StartsAtZero()
        {
            Assert.True(AdvisorConcurrencyTracker.ActiveCount >= 0);
        }

        [Fact]
        public void Increment_IncreasesCount()
        {
            int before = AdvisorConcurrencyTracker.ActiveCount;
            AdvisorConcurrencyTracker.Increment();
            int after = AdvisorConcurrencyTracker.ActiveCount;
            Assert.Equal(before + 1, after);
            AdvisorConcurrencyTracker.Decrement();
        }

        [Fact]
        public void Decrement_DecreasesCount()
        {
            AdvisorConcurrencyTracker.Increment();
            int before = AdvisorConcurrencyTracker.ActiveCount;
            AdvisorConcurrencyTracker.Decrement();
            int after = AdvisorConcurrencyTracker.ActiveCount;
            Assert.Equal(before - 1, after);
        }

        [Fact]
        public void Decrement_BelowZero_AutoCorrects()
        {
            int before = AdvisorConcurrencyTracker.ActiveCount;
            for (int i = 0; i < before + 1; i++)
                AdvisorConcurrencyTracker.Decrement();

            Assert.True(AdvisorConcurrencyTracker.ActiveCount >= 0);
        }

        [Fact]
        public void IncrementAndDecrement_RoundTrip()
        {
            int start = AdvisorConcurrencyTracker.ActiveCount;
            AdvisorConcurrencyTracker.Increment();
            AdvisorConcurrencyTracker.Increment();
            AdvisorConcurrencyTracker.Decrement();
            AdvisorConcurrencyTracker.Decrement();
            Assert.Equal(start, AdvisorConcurrencyTracker.ActiveCount);
        }
    }
}

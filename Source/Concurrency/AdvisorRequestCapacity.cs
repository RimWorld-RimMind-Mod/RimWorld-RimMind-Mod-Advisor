using System;

namespace RimMind.Advisor.Concurrency
{
    /// <summary>
    /// Local scan budget initialized from the global active-request count.
    /// </summary>
    internal sealed class AdvisorRequestCapacity
    {
        private int _active;
        private readonly int _maximum;

        public AdvisorRequestCapacity(int active, int maximum)
        {
            _active = Math.Max(0, active);
            _maximum = Math.Max(0, maximum);
        }

        public int Remaining => Math.Max(0, _maximum - _active);

        public bool TryReserve()
        {
            if (_active >= _maximum)
                return false;

            _active++;
            return true;
        }
    }
}

using UnityEngine;
using RimMind.Contracts.Extension;

namespace RimMind.Advisor
{
    internal sealed class AdvisorSettingsTab : ISettingsTab
    {
        private readonly RimMindAdvisorMod _mod;
        public AdvisorSettingsTab(RimMindAdvisorMod mod) { _mod = mod; }
        public string Id => "advisor";
        public string Label => "RimMind.Advisor.Settings.Tab".Translate();
        public void Draw(Rect rect) => RimMindAdvisorMod.DrawSettingsContent(rect);
    }
}

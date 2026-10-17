using RimMind.Advisor.Settings;
using RimMind.Presentation.Settings;
using UnityEngine;
using Verse;

namespace RimMind.Advisor
{
    internal sealed class AdvisorSettingsTab : ISettingsTab
    {
        public string Id => "advisor";
        public string OwnerModId => "RimMindAdvisor";
        public string Label => "RimMind.Advisor.Settings.Tab".CanTranslate() ? (string)"RimMind.Advisor.Settings.Tab".Translate() : "Advisor";
        public void Draw(Rect rect) => AdvisorSettingsDrawer.Draw(rect);
    }
}

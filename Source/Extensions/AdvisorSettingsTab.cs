using RimMind.Advisor.Settings;
using RimMind.Application.Common.Constants;
using RimMind.Presentation.Api;
using RimMind.Presentation.Settings;
using UnityEngine;

namespace RimMind.Advisor
{
    internal sealed class AdvisorSettingsTab : ISettingsTab
    {
        public string Id => "advisor";
        public string OwnerModId => RimMindOwnerConsts.AdvisorModId;
        public string Label => RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.Tab", "Advisor");
        public void Draw(Rect rect) => AdvisorSettingsDrawer.Draw(rect);
    }
}

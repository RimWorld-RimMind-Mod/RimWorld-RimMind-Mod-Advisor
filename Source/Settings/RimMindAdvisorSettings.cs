using RimMind.Actions;
using Verse;

namespace RimMind.Advisor.Settings
{
    public class RimMindAdvisorSettings : ModSettings
    {
        /// <summary>总开关。</summary>
        public bool enableAdvisor = true;

        /// <summary>请求冷却（ticks）。3600~72000，步进 600，默认 30000 ≈ 12 游戏小时。</summary>
        public int requestCooldownTicks = 30000;

        /// <summary>全局最大同时等待响应数（1~5）。</summary>
        public int maxConcurrentRequests = 3;

        /// <summary>显示 AI 决策理由气泡。</summary>
        public bool showThoughtBubble = true;

        /// <summary>小人空闲时触发。</summary>
        public bool enableIdleTrigger = true;

        /// <summary>心情低于阈值时触发。</summary>
        public bool enableMoodTrigger = true;

        /// <summary>CompTick 检查间隔（ticks）。600~6000，步进 100，默认 3600 ≈ 60s。</summary>
        public int pawnScanIntervalTicks = 3600;

        /// <summary>心情低于此值触发（0.25~0.6）。</summary>
        public float moodThreshold = 0.3f;

        /// <summary>审批请求过期时间（ticks）。审批悬浮窗超过此时间未响应则自动关闭。3600~120000，步进 1500，默认 30000。</summary>
        public int requestExpireTicks = 30000;

        /// <summary>启用审批系统。关闭后所有需审批的动作直接执行。</summary>
        public bool enableRequestSystem = true;

        /// <summary>启用风险拦截。达到风险等级阈值的动作自动拦截需玩家批准。</summary>
        public bool enableRiskApproval = true;

        /// <summary>自动拦截的风险等级阈值（Low~Critical）。达到此级别的动作需玩家批准。</summary>
        public RiskLevel autoBlockRiskLevel = RiskLevel.High;

        public string advisorCustomPrompt = string.Empty;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableAdvisor, "enableAdvisor", true);
            Scribe_Values.Look(ref requestCooldownTicks, "requestCooldownTicks", 30000);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 3);
            Scribe_Values.Look(ref showThoughtBubble, "showThoughtBubble", true);
            Scribe_Values.Look(ref enableIdleTrigger, "enableIdleTrigger", true);
            Scribe_Values.Look(ref enableMoodTrigger, "enableMoodTrigger", true);
            Scribe_Values.Look(ref pawnScanIntervalTicks, "pawnScanIntervalTicks", 3600);
            Scribe_Values.Look(ref moodThreshold, "moodThreshold", 0.3f);
            Scribe_Values.Look(ref requestExpireTicks, "requestExpireTicks", 30000);
            Scribe_Values.Look(ref enableRequestSystem, "enableRequestSystem", true);
            Scribe_Values.Look(ref enableRiskApproval, "enableRiskApproval", true);
            Scribe_Values.Look(ref autoBlockRiskLevel, "autoBlockRiskLevel", RiskLevel.High);
            Scribe_Values.Look(ref advisorCustomPrompt, "advisorCustomPrompt", string.Empty);
        }
    }
}

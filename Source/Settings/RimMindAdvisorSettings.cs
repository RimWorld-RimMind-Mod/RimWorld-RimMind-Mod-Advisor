using RimMind.Actions;
using Verse;

namespace RimMind.Advisor.Settings
{
    public class RimMindAdvisorSettings : ModSettings
    {
        /// <summary>总开关。</summary>
        public bool enableAdvisor = true;

        /// <summary>请求冷却（ticks）。1800~7200，步进 300，默认 3600 ≈ 1.4 游戏小时。</summary>
        public int requestCooldownTicks = 30000;

        /// <summary>全局最大同时等待响应数（1~5）。</summary>
        public int maxConcurrentRequests = 3;

        /// <summary>显示 AI 决策理由气泡。</summary>
        public bool showThoughtBubble = true;

        /// <summary>小人空闲时触发。</summary>
        public bool enableIdleTrigger = true;

        /// <summary>心情低于阈值时触发。</summary>
        public bool enableMoodTrigger = true;

        /// <summary>CompTick 检查间隔（ticks）。100~6000，步进 100，默认 3600 ≈ 60s。</summary>
        public int pawnScanIntervalTicks = 3600;

        /// <summary>心情低于此值触发（0.25~0.6）。</summary>
        public float moodThreshold = 0.3f;

        /// <summary>
        /// 玩家自定义附加 Prompt，追加在系统 Prompt 末尾。
        /// 可用于补充角色设定、风格偏好、行为限制等。留空则不追加。
        /// </summary>
        public string advisorCustomPrompt = "";

        public int requestExpireTicks = 30000;

        public bool enableRequestSystem = true;
        public bool enableRiskApproval = true;
        public RiskLevel autoBlockRiskLevel = RiskLevel.High;
        public bool injectMapAdvisorLog = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableAdvisor,          "enableAdvisor",          true);
            Scribe_Values.Look(ref requestCooldownTicks,   "requestCooldownTicks",   30000);
            Scribe_Values.Look(ref maxConcurrentRequests,  "maxConcurrentRequests",  3);
            Scribe_Values.Look(ref showThoughtBubble,      "showThoughtBubble",      true);
            Scribe_Values.Look(ref enableIdleTrigger,      "enableIdleTrigger",      true);
            Scribe_Values.Look(ref enableMoodTrigger,      "enableMoodTrigger",      true);
            Scribe_Values.Look(ref pawnScanIntervalTicks,  "pawnScanIntervalTicks",  3600);
            Scribe_Values.Look(ref moodThreshold,          "moodThreshold",          0.3f);
            Scribe_Values.Look(ref advisorCustomPrompt,    "advisorCustomPrompt",    "");
            Scribe_Values.Look(ref requestExpireTicks,     "requestExpireTicks",     30000);
            Scribe_Values.Look(ref enableRequestSystem,    "enableRequestSystem",    true);
            Scribe_Values.Look(ref enableRiskApproval,     "enableRiskApproval",     true);
            Scribe_Values.Look(ref autoBlockRiskLevel,     "autoBlockRiskLevel",     RiskLevel.High);
            Scribe_Values.Look(ref injectMapAdvisorLog,    "injectMapAdvisorLog",    false);
        }
    }
}

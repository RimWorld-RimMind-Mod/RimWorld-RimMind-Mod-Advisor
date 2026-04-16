using System.Collections.Generic;
using Newtonsoft.Json;

namespace RimMind.Advisor
{
    /// <summary>
    /// AI 响应的根 JSON 对象。
    /// 格式：{"advices":[...]}
    /// 无 RimWorld 依赖，可在单元测试中直接使用。
    /// </summary>
    public class AdviceBatch
    {
        [JsonProperty("advices")] public List<AdviceItem> advices = new List<AdviceItem>();
    }

    /// <summary>
    /// advices 数组中的单条建议。
    /// </summary>
    public class AdviceItem
    {
        [JsonProperty("action")] public string  action  = "";
        [JsonProperty("pawn")]   public string? pawn;    // 多 Pawn 模式：目标小人短名；单 Pawn 模式填 null
        [JsonProperty("target")] public string? target;  // 社交类动作的交互对象短名
        [JsonProperty("param")]  public string? param;
        [JsonProperty("reason")] public string? reason;
        [JsonProperty("request_type")] public string request_type = "normal";
    }
}

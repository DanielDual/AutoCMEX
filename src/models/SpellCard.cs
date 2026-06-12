namespace AutoCMEX.Models;

/// <summary>
/// 符卡数据模型
/// </summary>
public class SpellCard
{
    /// <summary>符卡名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>创作者，可为空（未揭晓）</summary>
    public string Creator { get; set; } = string.Empty;
}

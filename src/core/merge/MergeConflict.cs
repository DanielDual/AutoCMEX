namespace AutoCMEX.Core.Merge;

/// <summary>冲突类别。</summary>
public enum MergeConflictKind
{
  /// <summary>资源文件名冲突。</summary>
  Resource,

  /// <summary>对象/定义名冲突。</summary>
  Object,
}

/// <summary>
/// 合并时检测到的命名冲突项。默认保留原名，由用户决定是否自动改名或手动改。
/// </summary>
public sealed class MergeConflict
{
  /// <summary>冲突类别。</summary>
  public MergeConflictKind Kind { get; init; }

  /// <summary>原始名（资源文件名或对象名）。</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>涉及冲突的创作者包名（用逗号分隔）。</summary>
  public string Packages { get; init; } = string.Empty;

  /// <summary>可选的自动改名建议（如资源冲突时 {包名}_{原名}）；用户可选择是否采用。</summary>
  public string? SuggestedName { get; init; }

  /// <summary>冲突的描述。</summary>
  public string Description { get; init; } = string.Empty;
}

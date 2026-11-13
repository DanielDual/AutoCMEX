namespace AutoCMEX.Core.Merge;

using System;

/// <summary>
/// 一次 Lua 源码混淆的结果。
/// </summary>
public sealed class LuaObfuscationResult
{
  /// <summary>是否成功；失败时 <see cref="Output"/> 为 <c>null</c>，<see cref="Error"/> 说明原因。</summary>
  public bool IsSuccess { get; init; }

  /// <summary>混淆后的 Lua 5.1 源码字节（无 UTF-8 BOM）；失败时为 <c>null</c>。</summary>
  public byte[]? Output { get; init; }

  /// <summary>失败原因（含工具 stderr 摘要）；成功时为 <c>null</c>。</summary>
  public string? Error { get; init; }

  /// <summary>构造成功结果。</summary>
  /// <param name="output">混淆后的源码字节。</param>
  public static LuaObfuscationResult Success(byte[] output) =>
    new() { IsSuccess = true, Output = output };

  /// <summary>构造失败结果。</summary>
  /// <param name="error">失败原因。</param>
  public static LuaObfuscationResult Failure(string error) =>
    new() { IsSuccess = false, Error = error };
}

/// <summary>
/// Lua 脚本混淆器抽象：把 Lua 5.1 源码混淆为语义等价、可读性低的源码。
/// 实现必须 fail-closed：任何无法确认成功（工具缺失、解析失败、超时、输出异常）的输入都返回失败，
/// 绝不把原文冒充混淆结果。
/// </summary>
public interface ILuaObfuscator
{
  /// <summary>
  /// 混淆一段 Lua 源码。
  /// </summary>
  /// <param name="source">Lua 源码字节（允许带 UTF-8 BOM，由实现去除）。</param>
  /// <param name="entryName">条目名（仅用于错误信息与日志，如 mod zip 内的相对路径）。</param>
  /// <returns>混淆结果；失败时 <see cref="LuaObfuscationResult.IsSuccess"/> 为 <c>false</c> 且带原因。</returns>
  LuaObfuscationResult Obfuscate(byte[] source, string entryName);
}

namespace AutoCMEX.Core.Ai;

using System.Threading.Tasks;

/// <summary>
/// AI 服务接口
/// </summary>
public interface IAiService
{
  /// <summary>
  /// 发送聊天请求
  /// </summary>
  /// <param name="systemPrompt">系统提示</param>
  /// <param name="userMessage">用户消息</param>
  /// <returns>AI 回复文本</returns>
  Task<string> ChatAsync(string systemPrompt, string userMessage);

  /// <summary>
  /// 测试 API 连接
  /// </summary>
  /// <returns>连接是否成功</returns>
  Task<bool> TestConnectionAsync();
}

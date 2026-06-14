namespace AutoCMEX.Core.Storage;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// 导入结果
/// </summary>
[SuppressMessage(
  "Design",
  "CA1000:DoNotDeclareStaticMembersOnGenericTypes",
  Justification = "The static factory method is the standard design pattern for generic result types."
)]
public class ImportResult<T>
{
  public bool IsSuccess { get; }
  public T? Data { get; }
  public string ErrorMessage { get; } = string.Empty;

  private ImportResult(bool success, T? data, string error)
  {
    IsSuccess = success;
    Data = data;
    ErrorMessage = error;
  }

  public static ImportResult<T> Success(T data) => new(true, data, string.Empty);

  public static ImportResult<T> Error(string message) => new(false, default, message);
}

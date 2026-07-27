namespace AutoCMEX.Models;

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// 符卡数据模型
/// </summary>
public class SpellCard : INotifyPropertyChanged
{
  private string _name = string.Empty;
  private string _creator = string.Empty;
  private bool _isRevealed;
  private bool _isGuessedOut;

  /// <summary>符卡名称</summary>
  public string Name
  {
    get => _name;
    set => SetProperty(ref _name, value);
  }

  /// <summary>创作者（答案），可为空</summary>
  public string Creator
  {
    get => _creator;
    set => SetProperty(ref _creator, value);
  }

  /// <summary>是否已揭晓</summary>
  public bool IsRevealed
  {
    get => _isRevealed;
    set => SetProperty(ref _isRevealed, value);
  }

  /// <summary>是否已被猜出（猜测全部正确后标记）</summary>
  public bool IsGuessedOut
  {
    get => _isGuessedOut;
    set => SetProperty(ref _isGuessedOut, value);
  }

  public event PropertyChangedEventHandler? PropertyChanged;

  private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
  {
    if (!EqualityComparer<T>.Default.Equals(field, value))
    {
      field = value;
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
  }
}

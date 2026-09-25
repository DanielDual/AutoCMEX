namespace AutoCMEX.UI.Info;

using System;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 两表数据变更观察者：把「Boss 列表变化 / 当前 Boss 下标变化 / 当前 Boss 的符卡列表变化」
/// 合并成单一回调，供两张表栏目重建预览。
/// </summary>
/// <remarks>
/// <para>
/// 采用与 <c>SpellCardPanel</c> 相同的绑定范式：当前 Boss 更换时必须解绑旧 Boss 的符卡列表，
/// 否则会同时挂着多个 Boss 的回调（并在 Boss 被删除后导致无效引用）。
/// </para>
/// <para>
/// 回调可能来自数据层线程，调用方应自行 <c>CallDeferred</c> 后再触碰控件。
/// </para>
/// </remarks>
public sealed class BossDataWatcher : IDisposable
{
  private readonly DataManager _dataManager;
  private readonly Action _onChanged;

  private AutoList<Boss>.Binding? _bossesBinding;
  private AutoValue<int>.Binding? _selectedIndexBinding;
  private AutoList<SpellCard>.Binding? _spellCardsBinding;
  private Boss? _currentBoss;

  /// <summary>
  /// 创建观察者并立即开始监听。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="onChanged">数据变化回调。</param>
  public BossDataWatcher(DataManager dataManager, Action onChanged)
  {
    _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
    _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

    _bossesBinding = _dataManager.Bosses.Bind().OnModify(OnChanged);
    _selectedIndexBinding = _dataManager
      .Settings.SelectedBossIndex.Bind()
      .OnValue(_ => OnChanged());
    RebindCurrentBoss();
  }

  /// <summary>释放全部绑定。</summary>
  public void Dispose()
  {
    _bossesBinding?.Dispose();
    _bossesBinding = null;

    _selectedIndexBinding?.Dispose();
    _selectedIndexBinding = null;

    _spellCardsBinding?.Dispose();
    _spellCardsBinding = null;

    _currentBoss = null;
  }

  private void OnChanged()
  {
    RebindCurrentBoss();
    _onChanged();
  }

  private void RebindCurrentBoss()
  {
    var currentBoss = ResolveCurrentBoss();
    if (ReferenceEquals(currentBoss, _currentBoss))
      return;

    _spellCardsBinding?.Dispose();
    _spellCardsBinding = null;
    _currentBoss = currentBoss;

    if (currentBoss is null)
      return;

    _spellCardsBinding = currentBoss.SpellCards.Bind().OnModify(OnChanged);
  }

  private Boss? ResolveCurrentBoss()
  {
    var bosses = _dataManager.Bosses;
    if (bosses.Count == 0)
      return null;

    var index = _dataManager.Settings.SelectedBossIndex.Value;
    if (index < 0 || index >= bosses.Count)
      index = 0;

    return bosses[index];
  }
}

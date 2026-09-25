namespace AutoCMEX.UI.Info;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 两表数据变更观察者：把「Boss 列表变化 / 当前 Boss 下标变化 / 当前 Boss 的符卡列表变化 /
/// 当前 Boss 每张符卡的属性变化」合并成单一回调，供两张表栏目重建预览。
/// </summary>
/// <remarks>
/// <para>
/// 采用与 <c>SpellCardPanel</c> 相同的绑定范式：当前 Boss 更换时必须解绑旧 Boss 的符卡列表，
/// 否则会同时挂着多个 Boss 的回调（并在 Boss 被删除后导致无效引用）。
/// </para>
/// <para>
/// <b>符卡属性必须逐个订阅。</b><see cref="AutoList{T}"/> 的 <c>OnModify</c> 只在列表自身增删时触发，
/// 而猜测模块改的是 <see cref="SpellCard.IsGuessedOut"/> 这类元素内部的 <see cref="AutoValue{T}"/>：
/// 列表对象没有任何变化，只订阅列表就会漏掉「猜出符卡」这个最主要的变更来源，两张表要等下次启动
/// 重新读盘才同步。符卡改名、改创作者同理，只有属性级订阅能看到。
/// </para>
/// <para>
/// <see cref="SpellCard.IsRevealed"/> 不在订阅范围内：两张表的列、行染色与创作者掩码都只由
/// 符卡名 / 创作者 / 是否已猜出决定，订阅它只会带来无谓的重建。
/// </para>
/// <para>
/// 回调可能来自数据层线程，调用方应自行 <c>CallDeferred</c> 后再触碰控件。
/// </para>
/// </remarks>
public sealed class BossDataWatcher : IDisposable
{
  private readonly DataManager _dataManager;
  private readonly Action _onChanged;

  /// <summary>当前 Boss 每张符卡的属性绑定（符卡名 / 创作者 / 是否已猜出）。</summary>
  private readonly List<IDisposable> _cardBindings = new();

  private AutoList<Boss>.Binding? _bossesBinding;
  private AutoValue<int>.Binding? _selectedIndexBinding;
  private AutoList<SpellCard>.Binding? _spellCardsBinding;
  private Boss? _currentBoss;
  private AutoList<SpellCard>? _currentSpellCards;

  /// <summary>是否正在重挂符卡属性绑定。</summary>
  /// <remarks>
  /// <see cref="AutoValue{T}"/> 的绑定在订阅时会立刻用当前值回调一次，不抑制的话每重挂一次就要按
  /// 「符卡数 × 属性数」炸出一串回调（59 张符卡就是 177 次全表重建）。
  /// </remarks>
  private bool _isRebindingCards;

  /// <summary>
  /// 创建观察者并立即开始监听。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="onChanged">数据变化回调。</param>
  public BossDataWatcher(DataManager dataManager, Action onChanged)
  {
    _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
    _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

    _bossesBinding = _dataManager.Bosses.Bind().OnModify(OnBossListChanged);
    _selectedIndexBinding = _dataManager
      .Settings.SelectedBossIndex.Bind()
      .OnValue(_ => OnBossListChanged());
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

    DisposeCardBindings();
    _currentBoss = null;
    _currentSpellCards = null;
  }

  /// <summary>Boss 列表变化：当前 Boss 可能换人，先重挂绑定再通知。</summary>
  private void OnBossListChanged()
  {
    RebindCurrentBoss();
    _onChanged();
  }

  /// <summary>当前 Boss 的符卡增删：新符卡要纳入监听、被删符卡的绑定要释放，再通知。</summary>
  private void OnSpellCardListChanged()
  {
    RebindCardBindings(_currentBoss);
    _onChanged();
  }

  /// <summary>符卡属性变化（猜出 / 改名 / 改创作者）：数据源未换，直接通知。</summary>
  private void OnSpellCardValueChanged()
  {
    if (_isRebindingCards)
      return;

    _onChanged();
  }

  private void RebindCurrentBoss()
  {
    var currentBoss = ResolveCurrentBoss();
    var currentSpellCards = currentBoss?.SpellCards;

    // 同一个 Boss 也要比列表实例：换盘时有可能只把 SpellCards 换成新的 AutoList，沿用旧订阅就会漏挂。
    if (
      ReferenceEquals(currentBoss, _currentBoss)
      && ReferenceEquals(currentSpellCards, _currentSpellCards)
    )
      return;

    _spellCardsBinding?.Dispose();
    _spellCardsBinding = null;
    _currentBoss = currentBoss;
    _currentSpellCards = currentSpellCards;

    if (currentSpellCards is not null)
      _spellCardsBinding = currentSpellCards.Bind().OnModify(OnSpellCardListChanged);

    RebindCardBindings(currentBoss);
  }

  /// <summary>重挂当前 Boss 全部符卡的属性绑定。</summary>
  /// <remarks>
  /// 抑制期会丢掉订阅回放的回调，所以调用方必须在重挂后自己补一次通知
  /// （<see cref="OnBossListChanged"/> 与 <see cref="OnSpellCardListChanged"/> 都已如此），
  /// 否则抑制窗口内发生的真实变更会被漏掉。
  /// </remarks>
  /// <param name="boss">当前 Boss；为 null 时只释放旧绑定。</param>
  private void RebindCardBindings(Boss? boss)
  {
    _isRebindingCards = true;
    try
    {
      DisposeCardBindings();

      if (boss is null)
        return;

      foreach (var card in boss.SpellCards)
      {
        // 三个属性都是两张表的输入：符卡名与创作者是单元格内容，是否已猜出决定整行染色与创作者计数。
        _cardBindings.Add(card.Name.Bind().OnValue(_ => OnSpellCardValueChanged()));
        _cardBindings.Add(card.Creator.Bind().OnValue(_ => OnSpellCardValueChanged()));
        _cardBindings.Add(card.IsGuessedOut.Bind().OnValue(_ => OnSpellCardValueChanged()));
      }
    }
    finally
    {
      _isRebindingCards = false;
    }
  }

  private void DisposeCardBindings()
  {
    foreach (var binding in _cardBindings)
      binding.Dispose();

    _cardBindings.Clear();
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

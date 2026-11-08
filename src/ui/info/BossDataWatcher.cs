namespace AutoCMEX.UI.Info;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 两表数据变更观察者：把「Boss 列表变化 / 当前 Boss 下标变化 / 当前 Boss 的符卡列表变化 /
/// 当前 Boss 每张符卡的属性变化」合并成单一通知，供两张表栏目重建预览。
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
/// <b>线程纪律：</b>写入可能来自 WebSocket 线程池（猜测模块在 <c>Task.Run</c> 里改
/// <c>SelectedBossIndex</c> / 符卡属性），而本类会改动绑定集合，只能在主线程执行。因此所有
/// 「结构变更」（重挂 / 释放绑定）都经构造时注入的 <c>dispatch</c> 收敛到主线程：数据线程只负责
/// 排队，变更通知本身仍同步触发，由接收通知的面板自行 <c>CallDeferred</c> 后触碰控件。
/// 生产代码必须传入 <c>action =&gt; Callable.From(action).CallDeferred()</c>；
/// 只有 <see cref="BossDataWatcher(DataManager, Action)"/> 默认使用 inline 调度，
/// 仅用于纯逻辑测试与「调用方已保证在主线程」的场景。
/// </para>
/// <para>
/// <b>共享：</b>两张表栏可以共用一个观察者——<see cref="Changed"/> 支持多监听，
/// 或继续使用构造参数 <c>onChanged</c> 的单一回调。
/// </para>
/// </remarks>
public sealed class BossDataWatcher : IDisposable
{
  private readonly DataManager _dataManager;
  private readonly Action? _onChanged;
  private readonly Action<Action> _dispatch;
  private readonly object _gate = new();

  /// <summary>当前 Boss 每张符卡的属性绑定（符卡名 / 创作者 / 是否已猜出）。只在主线程读写。</summary>
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
  private volatile bool _isRebindingCards;

  /// <summary>是否已释放；释放后到期的 deferred 结构变更一律安全跳过。</summary>
  private bool _disposed;

  /// <summary>
  /// 数据变化通知（多监听）。回调可能来自数据线程，订阅方必须自行调度到主线程后再操作控件。
  /// </summary>
  public event Action? Changed;

  /// <summary>当前挂着的符卡属性绑定数量（测试用：判断结构变更是否真的发生过）。</summary>
  internal int CardBindingCountForTests => _cardBindings.Count;

  /// <summary>
  /// 创建观察者并立即开始监听，结构变更走 inline 调度（调用方必须已保证在主线程）。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="onChanged">数据变化回调（可能来自数据线程）。</param>
  public BossDataWatcher(DataManager dataManager, Action onChanged)
    : this(dataManager, onChanged, static action => action()) { }

  /// <summary>
  /// 创建观察者并立即开始监听，仅使用 <see cref="Changed"/> 事件接收通知。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="dispatch">
  /// 主线程调度器：接收「结构变更」动作并保证它最终在主线程执行，例如
  /// <c>action =&gt; Callable.From(action).CallDeferred()</c>。
  /// </param>
  public BossDataWatcher(DataManager dataManager, Action<Action> dispatch)
    : this(dataManager, null, dispatch) { }

  /// <summary>
  /// 创建观察者并立即开始监听，同时支持单一回调与 <see cref="Changed"/> 事件。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="onChanged">数据变化回调；不需要时传 null。</param>
  /// <param name="dispatch">主线程调度器（见 <see cref="BossDataWatcher(DataManager, Action{Action})"/>）。</param>
  public BossDataWatcher(DataManager dataManager, Action? onChanged, Action<Action> dispatch)
  {
    _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
    _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    _onChanged = onChanged;

    _bossesBinding = _dataManager.Bosses.Bind().OnModify(OnBossListChanged);
    _selectedIndexBinding = _dataManager
      .Settings.SelectedBossIndex.Bind()
      .OnValue(_ => OnBossListChanged());

    // 构造发生在面板 Setup 的同步路径（主线程），直接重挂即可。
    RebindCurrentBoss();
  }

  /// <summary>释放全部绑定并清空监听；重复调用安全。</summary>
  /// <remarks>
  /// 释放本身是结构变更，同样只在主线程执行（面板在 <c>_ExitTree</c> 调用）。
  /// 释放前已排队但尚未执行的 deferred 重挂会在执行时发现已释放而跳过。
  /// </remarks>
  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed)
        return;

      _disposed = true;
    }

    _bossesBinding?.Dispose();
    _bossesBinding = null;

    _selectedIndexBinding?.Dispose();
    _selectedIndexBinding = null;

    _spellCardsBinding?.Dispose();
    _spellCardsBinding = null;

    DisposeCardBindings();
    _currentBoss = null;
    _currentSpellCards = null;
    Changed = null;
  }

  /// <summary>Boss 列表变化：当前 Boss 可能换人，先排队重挂绑定再通知。</summary>
  private void OnBossListChanged()
  {
    ScheduleOnMainThread(RebindCurrentBoss);
    NotifyChanged();
  }

  /// <summary>当前 Boss 的符卡增删：新符卡要纳入监听、被删符卡的绑定要释放，再通知。</summary>
  private void OnSpellCardListChanged()
  {
    // 执行时再读 _currentBoss：排队期间可能已有别的重挂把当前 Boss 换掉了
    ScheduleOnMainThread(() => RebindCardBindings(_currentBoss));
    NotifyChanged();
  }

  /// <summary>符卡属性变化（猜出 / 改名 / 改创作者）：数据源未换，直接通知。</summary>
  private void OnSpellCardValueChanged()
  {
    if (_isRebindingCards)
      return;

    NotifyChanged();
  }

  /// <summary>
  /// 把「结构变更」动作排队到主线程执行；若已释放则直接丢弃。
  /// </summary>
  /// <param name="structuralChange">只允许触碰绑定集合的动作。</param>
  private void ScheduleOnMainThread(Action structuralChange)
  {
    lock (_gate)
    {
      if (_disposed)
        return;
    }

    _dispatch(() =>
    {
      lock (_gate)
      {
        // 释放后到达的 deferred 重挂：绑定已释放，跳过而不是去碰空集合
        if (_disposed)
          return;
      }

      structuralChange();
    });
  }

  /// <summary>同步发出变更通知（可能来自数据线程，接收方自行调度到主线程）。</summary>
  private void NotifyChanged()
  {
    lock (_gate)
    {
      if (_disposed)
        return;
    }

    _onChanged?.Invoke();
    Changed?.Invoke();
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

      // 规则 22：先快照再遍历，数据线程并发增删符卡时不会在枚举期间改集合
      foreach (var card in boss.SpellCards.ToList())
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
    // 规则 22：先快照再按下标取，避免并发 Remove 让 Count 与下标错位
    var bosses = _dataManager.Bosses.ToList();
    if (bosses.Count == 0)
      return null;

    var index = _dataManager.Settings.SelectedBossIndex.Value;
    if (index < 0 || index >= bosses.Count)
      index = 0;

    return bosses[index];
  }
}

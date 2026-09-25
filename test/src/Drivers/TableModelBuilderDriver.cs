namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Chickensoft.Log;
using Moq;

/// <summary>
/// 信息板块两表的测试驱动：装配一套中性化夹具 Boss，并直接产出快照与两张
/// <see cref="TableModel"/>，供逻辑层与面板层测试复用。
/// </summary>
/// <remarks>
/// 夹具结构与真实数据同形（多创作者、部分符卡已猜出、含未猜出的创作者），但名字一律中性化，
/// 真实创作者名不得进入仓库。
/// </remarks>
public sealed class TableModelBuilderDriver : IDisposable
{
  /// <summary>夹具 Boss 名。</summary>
  public const string BossName = "SampleBoss";

  /// <summary>
  /// 夹具创作者构成：创作者、符卡总数、其中已猜出数（按首次出现顺序）。
  /// </summary>
  /// <remarks>
  /// 已猜出的符卡固定排在每个创作者名下的前若干张，测试可直接按下标断言遮罩与染色。
  /// </remarks>
  public static readonly (string Creator, int Total, int GuessedOut)[] FixtureCreators =
  {
    ("Alpha", 3, 1),
    ("Beta", 2, 2),
    ("Gamma", 1, 0),
  };

  private readonly DataManager _dataManager;

  /// <summary>
  /// 创建驱动（数据目录用独立临时目录，不触碰真实数据）。
  /// </summary>
  /// <param name="dataDir">数据目录。</param>
  public TableModelBuilderDriver(string dataDir)
  {
    _dataManager = new DataManager(
      dataDir,
      new AesEncryptor(AesEncryptor.GetDefaultKeyPath(dataDir)),
      new Mock<ILog>().Object
    );
    Service = new InfoDataService(_dataManager, new Mock<ILog>().Object);
  }

  /// <summary>数据管理器（面板层测试可直接取用）。</summary>
  public DataManager DataManager => _dataManager;

  /// <summary>信息数据服务。</summary>
  public InfoDataService Service { get; }

  /// <summary>夹具符卡总数。</summary>
  public static int FixtureCardCount
  {
    get
    {
      var total = 0;
      foreach (var (_, cards, _) in FixtureCreators)
        total += cards;

      return total;
    }
  }

  /// <summary>添加中性化夹具 Boss 及其符卡（6 张：Alpha 3 张含 1 张已猜出、Beta 2 张全猜出、Gamma 1 张）。</summary>
  public void AddFixtureBoss()
  {
    var boss = new Boss { Name = BossName };
    var index = 0;

    foreach (var (creator, total, guessedOut) in FixtureCreators)
    {
      for (var i = 0; i < total; i++)
      {
        index++;
        boss.SpellCards.Add(
          new SpellCard
          {
            Name = { Value = $"符卡 {index}" },
            Creator = { Value = creator },
            IsGuessedOut = { Value = i < guessedOut },
          }
        );
      }
    }

    _dataManager.Bosses.Add(boss);
  }

  /// <summary>取两表快照。</summary>
  /// <returns>当前 Boss 的两表快照。</returns>
  public InfoTableSnapshot BuildSnapshot() => Service.BuildSnapshot();

  /// <summary>构建符卡猜测情况表渲染模型。</summary>
  /// <returns>表格渲染模型。</returns>
  public TableModel BuildGuessingTable() => TableModelBuilder.BuildGuessingTable(BuildSnapshot());

  /// <summary>构建创作者剩余未被猜测符卡数表渲染模型。</summary>
  /// <returns>表格渲染模型。</returns>
  public TableModel BuildCreatorRemainingTable() =>
    TableModelBuilder.BuildCreatorRemainingTable(BuildSnapshot());

  /// <inheritdoc/>
  public void Dispose() => _dataManager.Dispose();
}

namespace AutoCMEX;

using System.Collections.Generic;
using AutoCMEX.Core.Recording;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 序号与命名规则单测：序号只数战斗阶段、非符用自己的序号、符卡保留游戏内名字，
/// 以及非战斗阶段与越界下标这些「不该产出名字」的分支。
/// </summary>
public class GifNamingTest : TestClass
{
  public GifNamingTest(Node testScene)
    : base(testScene) { }

  /// <summary>构造一张卡的枚举结果项（名字非空即视为符卡）。</summary>
  /// <param name="index">绝对下标。</param>
  /// <param name="name">符卡名；非符与对话传空串。</param>
  /// <param name="isCombat">是否战斗阶段。</param>
  /// <returns>卡项。</returns>
  private static RecordingCardInfo Card(int index, string name, bool isCombat) =>
    new()
    {
      AbsoluteIndex = index,
      Name = name,
      IsSpellCard = !string.IsNullOrEmpty(name),
      IsCombat = isCombat,
      T3Seconds = isCombat ? 30d : 0d,
    };

  /// <summary>
  /// 样例卡表（含对话阶段）：入场 / 非符 / 符卡甲 / 对话 / 非符 / 符卡乙。
  /// </summary>
  /// <returns>卡表。</returns>
  private static List<RecordingCardInfo> SampleCards() =>
    [
      Card(1, string.Empty, false),
      Card(2, string.Empty, true),
      Card(3, "符卡甲", true),
      Card(4, string.Empty, false),
      Card(5, string.Empty, true),
      Card(6, "符卡乙", true),
    ];

  [Test]
  public void CombatOrdinalOf_SkipsNonCombatStages()
  {
    var cards = SampleCards();

    // 绝对下标 2 = 第一张非符 → 战斗序列第 1 张
    GifNaming.CombatOrdinalOf(cards, 2).ShouldBe(1);
    GifNaming.CombatOrdinalOf(cards, 3).ShouldBe(2);
    // 绝对下标 4 是对话、不计数，故下标 5 是第 3 张
    GifNaming.CombatOrdinalOf(cards, 5).ShouldBe(3);
    GifNaming.CombatOrdinalOf(cards, 6).ShouldBe(4);
  }

  [Test]
  public void CombatOrdinalOf_NonCombatOrMissingCard_ReturnsNoOrdinal()
  {
    var cards = SampleCards();

    GifNaming.CombatOrdinalOf(cards, 1).ShouldBe(GifNaming.NoOrdinal);
    GifNaming.CombatOrdinalOf(cards, 4).ShouldBe(GifNaming.NoOrdinal);
    GifNaming.CombatOrdinalOf(cards, 99).ShouldBe(GifNaming.NoOrdinal);
    GifNaming.CombatOrdinalOf(null, 2).ShouldBe(GifNaming.NoOrdinal);
  }

  [Test]
  public void NonSpellOrdinalOf_CountsNonSpellsOnly()
  {
    var cards = SampleCards();

    // 非符自己计数：下标 2 是第 1 张非符，下标 5 是第 2 张（中间的符卡不计入）
    GifNaming.NonSpellOrdinalOf(cards, 2).ShouldBe(1);
    GifNaming.NonSpellOrdinalOf(cards, 5).ShouldBe(2);
  }

  [Test]
  public void NonSpellOrdinalOf_SpellOrNonCombat_ReturnsNoOrdinal()
  {
    var cards = SampleCards();

    GifNaming.NonSpellOrdinalOf(cards, 3).ShouldBe(GifNaming.NoOrdinal);
    GifNaming.NonSpellOrdinalOf(cards, 4).ShouldBe(GifNaming.NoOrdinal);
    GifNaming.NonSpellOrdinalOf(cards, 99).ShouldBe(GifNaming.NoOrdinal);
    GifNaming.NonSpellOrdinalOf(null, 5).ShouldBe(GifNaming.NoOrdinal);
  }

  [Test]
  public void NonSpellName_ValidOrdinal_FormatsName()
  {
    GifNaming.NonSpellName(1).ShouldBe("普通攻击 1");
    GifNaming.NonSpellName(12).ShouldBe("普通攻击 12");
    GifNaming.NonSpellName(GifNaming.NoOrdinal).ShouldBe(string.Empty);
  }

  [Test]
  public void ResolveEntryName_KeepsSpellNameAndNumbersNonSpell()
  {
    var cards = SampleCards();

    GifNaming.ResolveEntryName(cards[2], GifNaming.NonSpellOrdinalOf(cards, 3)).ShouldBe("符卡甲");
    GifNaming
      .ResolveEntryName(cards[1], GifNaming.NonSpellOrdinalOf(cards, 2))
      .ShouldBe("普通攻击 1");
    GifNaming
      .ResolveEntryName(cards[4], GifNaming.NonSpellOrdinalOf(cards, 5))
      .ShouldBe("普通攻击 2");
  }

  [Test]
  public void ResolveEntryName_NonCombatOrNull_ReturnsEmpty()
  {
    var cards = SampleCards();

    GifNaming.ResolveEntryName(cards[0], GifNaming.NoOrdinal).ShouldBe(string.Empty);
    GifNaming.ResolveEntryName(null, GifNaming.NoOrdinal).ShouldBe(string.Empty);
  }
}

namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Core.Merge;
using AutoCMEX.Models;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 对应表「交替」打乱算法（<see cref="MappingInterleave"/>）单元测试。
/// 算法语义 = <strong>形态由规则决定、内容每次随机</strong>，故断言分两类：
/// ① 形态（N/S 序列）用固定例题表逐例核对；② 内容用「同种子可复现 / 多种子结果不同」核对。
/// 记号：N=非符、S=符卡，空格分隔为类型序列；例 `N S N S S` = 非符、符卡、非符、符卡、符卡。
/// </summary>
public class MappingInterleaveTest : TestClass
{
  public MappingInterleaveTest(Node testScene)
    : base(testScene) { }

  /// <summary>按 "NNSSS" 形态构造对应表（同类内按出现序号命名）。</summary>
  private static List<SpellCardMappingEntry> Build(string pattern)
  {
    var list = new List<SpellCardMappingEntry>();
    var n = 0;
    var s = 0;
    foreach (var ch in pattern)
    {
      if (ch == 'N')
        list.Add(new SpellCardMappingEntry { Name = $"n{++n}", IsNonSpell = new(true) });
      else
        list.Add(new SpellCardMappingEntry { Name = $"s{++s}", IsNonSpell = new(false) });
    }
    return list;
  }

  /// <summary>构造 a 个非符在前、b 个符卡在后的对应表（等价于用户看到的「未重排」原序）。</summary>
  private static List<SpellCardMappingEntry> BuildPair(int a, int b) =>
    Build(new string('N', a) + new string('S', b));

  private static List<SpellCardMappingEntry> Run(int a, int b, int seed = 0) =>
    MappingInterleave.Reinterleave(BuildPair(a, b), new Random(seed));

  /// <summary>类型序列（形态），如 "N S N N S"。</summary>
  private static string Shape(IEnumerable<SpellCardMappingEntry> entries) =>
    string.Join(" ", entries.Select(e => e.IsNonSpell.Value ? "N" : "S"));

  private static string Describe(IEnumerable<SpellCardMappingEntry> entries) =>
    string.Join(" ", entries.Select(e => e.Name));

  /// <summary>提取某一类的连续块长序列。</summary>
  private static List<int> RunLengths(IEnumerable<SpellCardMappingEntry> entries, bool nonSpell)
  {
    var runs = new List<int>();
    var count = 0;
    foreach (var e in entries)
    {
      if (e.IsNonSpell.Value == nonSpell)
      {
        count++;
        continue;
      }
      if (count > 0)
        runs.Add(count);
      count = 0;
    }
    if (count > 0)
      runs.Add(count);
    return runs;
  }

  /// <summary>
  /// 规则例题表（a=非符数，b=符卡数，期望形态）。覆盖 1×1、单类为 0、相等、大数、
  /// 既不整除也非等差，以及余数摊尾块的多例。
  /// </summary>
  private static readonly (int A, int B, string Shape)[] Examples =
  {
    (1, 1, "N S"),
    (1, 2, "N S S"),
    (2, 1, "N N S"),
    (1, 4, "N S S S S"),
    (4, 1, "N N N N S"),
    (2, 2, "N S N S"),
    (3, 3, "N S N S N S"),
    (5, 5, "N S N S N S N S N S"),
    (2, 3, "N S N S S"),
    (3, 2, "N S N N S"),
    (3, 4, "N S N S N S S"),
    (4, 3, "N S N S N N S"),
    (2, 4, "N S S N S S"),
    (4, 2, "N N S N N S"),
    (2, 5, "N S S N S S S"),
    (5, 2, "N N S N N N S"),
    (7, 2, "N N N S N N N N S"),
    (2, 7, "N S S S N S S S S"),
    (3, 6, "N S S N S S N S S"),
    (6, 3, "N N S N N S N N S"),
    (4, 6, "N S N S N S S N S S"),
    (6, 4, "N S N S N N S N N S"),
    (8, 3, "N N S N N N S N N N S"),
    (3, 8, "N S S N S S S N S S S"),
    (9, 3, "N N N S N N N S N N N S"),
    (3, 9, "N S S S N S S S N S S S"),
    (10, 4, "N N S N N S N N N S N N N S"),
    (4, 10, "N S S N S S N S S S N S S S"),
    (11, 4, "N N S N N N S N N N S N N N S"),
    (4, 11, "N S S N S S S N S S S N S S S"),
    (13, 5, "N N S N N S N N N S N N N S N N N S"),
    (5, 13, "N S S N S S N S S S N S S S N S S S"),
    (12, 5, "N N S N N S N N S N N N S N N N S"),
  };

  [Test]
  public void Interleave_Examples_MatchShape()
  {
    foreach (var (a, b, expected) in Examples)
      Shape(Run(a, b)).ShouldBe(expected);
  }

  [Test]
  public void Interleave_MissingOneClass_ShufflesWholeList()
  {
    // 单元素/空表无需重排。
    Run(0, 0).ShouldBeEmpty();
    Describe(Run(1, 0)).ShouldBe("n1");
    Describe(Run(0, 1)).ShouldBe("s1");

    // 只有一类时交替形态退化为「单块」（无长度约束）⇒ 整列随机，仍满足「每次按下结果不同」。
    var nonSpellOrders = Enumerable.Range(0, 20).Select(seed => Describe(Run(4, 0, seed))).ToList();
    nonSpellOrders.Distinct().Count().ShouldBeGreaterThan(1);
    var spellOrders = Enumerable.Range(0, 20).Select(seed => Describe(Run(0, 4, seed))).ToList();
    spellOrders.Distinct().Count().ShouldBeGreaterThan(1);
  }

  [Test]
  public void Interleave_SameSeed_IsReproducible()
  {
    Describe(Run(8, 5, seed: 7)).ShouldBe(Describe(Run(8, 5, seed: 7)));
    Describe(Run(8, 5, seed: 7)).ShouldNotBe(Describe(Run(8, 5, seed: 8)));
  }

  /// <summary>
  /// 回归：用户实测「按很多次都是一个结果」。规则模式也必须每次给出不同排列
  /// （形态由规则恒定，元素身份随机）。
  /// </summary>
  [Test]
  public void Interleave_RepeatedPresses_ProduceDifferentOrders()
  {
    var source = BuildPair(6, 6);
    var orders = new List<string>();

    for (var seed = 0; seed < 20; seed++)
    {
      var result = MappingInterleave.Reinterleave(source, new Random(seed));
      Shape(result).ShouldBe("N S N S N S N S N S N S");
      orders.Add(Describe(result));
    }

    orders.Distinct().Count().ShouldBeGreaterThan(1);
  }

  [Test]
  public void Interleave_Properties_Hold()
  {
    for (var a = 0; a <= 12; a++)
    for (var b = 0; b <= 12; b++)
    {
      var source = BuildPair(a, b);
      var result = MappingInterleave.Reinterleave(source, new Random(0));

      // 守恒：元素个数与引用集合一致；入参未被就地修改（原序仍与新建的同形态列表一致）。
      result.Count.ShouldBe(source.Count);
      result.ShouldBe(source, ignoreOrder: true);
      Describe(source).ShouldBe(Describe(BuildPair(a, b)));

      // 形态与随机源无关（形态由规则决定，只有内容随机）。
      Shape(MappingInterleave.Reinterleave(source, new Random(12345))).ShouldBe(Shape(result));

      if (source.Count <= 1)
      {
        Describe(result).ShouldBe(Describe(source));
        continue;
      }

      if (a == 0 || b == 0)
      {
        // 单类：形态必然是「全为该类」，内容随机 ⇒ 只校验类型序列不变。
        Shape(result).ShouldBe(Shape(source));
        continue;
      }

      // 首块恒为非符。
      result[0].IsNonSpell.Value.ShouldBeTrue();

      // 多者块长非降；少者每块恒为 1。
      var longerIsNonSpell = a >= b;
      var runs = RunLengths(result, longerIsNonSpell);
      runs.ShouldBe(runs.OrderBy(x => x).ToList());
      RunLengths(result, !longerIsNonSpell).ShouldAllBe(x => x == 1);
    }
  }
}

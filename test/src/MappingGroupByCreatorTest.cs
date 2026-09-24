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
/// 对应表「按创作者分组」重排算法（<see cref="MappingGroupByCreator"/>）单元测试。
/// 语义 = <strong>分组与组序确定、组内每次随机</strong>：
/// 组间顺序取创作者首次出现的先后，组内元素洗牌。
/// </summary>
public class MappingGroupByCreatorTest : TestClass
{
  public MappingGroupByCreatorTest(Node testScene)
    : base(testScene) { }

  private static SpellCardMappingEntry Entry(string name, string creator) =>
    new()
    {
      Name = name,
      Creator = new(creator),
      IsNonSpell = new(false),
    };

  private static string Describe(IEnumerable<SpellCardMappingEntry> entries) =>
    string.Join(" ", entries.Select(e => e.Name));

  private static string Creators(IEnumerable<SpellCardMappingEntry> entries) =>
    string.Join(" ", entries.Select(e => e.Creator.Value));

  [Test]
  public void Regroup_GroupsByFirstAppearance()
  {
    var mapping = new List<SpellCardMappingEntry>
    {
      Entry("a1", "Alice"),
      Entry("b1", "Bob"),
      Entry("a2", "Alice"),
      Entry("c1", "Carol"),
      Entry("b2", "Bob"),
      Entry("a3", "Alice"),
    };

    var result = MappingGroupByCreator.RegroupByCreator(mapping, new Random(0));

    // 组间：Alice 首次出现 → Bob → Carol；组内顺序随机，故只断言分组归属。
    Creators(result).ShouldBe("Alice Alice Alice Bob Bob Carol");
    result.Count.ShouldBe(mapping.Count);
    result.ShouldBe(mapping, ignoreOrder: true);
    // 入参未被就地修改。
    Describe(mapping).ShouldBe("a1 b1 a2 c1 b2 a3");
  }

  [Test]
  public void Regroup_SameSeed_IsReproducible()
  {
    var mapping = new List<SpellCardMappingEntry>
    {
      Entry("a1", "Alice"),
      Entry("a2", "Alice"),
      Entry("a3", "Alice"),
      Entry("a4", "Alice"),
      Entry("b1", "Bob"),
      Entry("b2", "Bob"),
    };

    var first = MappingGroupByCreator.RegroupByCreator(mapping, new Random(3));
    var second = MappingGroupByCreator.RegroupByCreator(mapping, new Random(3));
    Describe(second).ShouldBe(Describe(first));

    var other = MappingGroupByCreator.RegroupByCreator(mapping, new Random(4));
    Describe(other).ShouldNotBe(Describe(first));
  }

  /// <summary>
  /// 回归：用户实测「按很多次都是一个结果」。分组模式也必须每次给出不同排列。
  /// </summary>
  [Test]
  public void Regroup_RepeatedPresses_ProduceDifferentOrders()
  {
    var mapping = new List<SpellCardMappingEntry>
    {
      Entry("a1", "Alice"),
      Entry("a2", "Alice"),
      Entry("a3", "Alice"),
      Entry("a4", "Alice"),
      Entry("a5", "Alice"),
      Entry("a6", "Alice"),
    };

    var orders = new List<string>();
    for (var seed = 0; seed < 20; seed++)
    {
      var result = MappingGroupByCreator.RegroupByCreator(mapping, new Random(seed));
      Creators(result).ShouldBe("Alice Alice Alice Alice Alice Alice");
      orders.Add(Describe(result));
    }

    orders.Distinct().Count().ShouldBeGreaterThan(1);
  }

  [Test]
  public void Regroup_AlreadyGrouped_KeepsGroupOrder()
  {
    var mapping = new List<SpellCardMappingEntry>
    {
      Entry("a1", "Alice"),
      Entry("a2", "Alice"),
      Entry("b1", "Bob"),
    };

    Creators(MappingGroupByCreator.RegroupByCreator(mapping, new Random(0)))
      .ShouldBe("Alice Alice Bob");
  }

  [Test]
  public void Regroup_EmptyCreatorName_TreatedAsOneGroup()
  {
    var mapping = new List<SpellCardMappingEntry>
    {
      Entry("n1", ""),
      Entry("a1", "Alice"),
      Entry("n2", ""),
    };

    // 空创作者名单列一组，且按首次出现排在 Alice **之前**（n1 是空串首次出现处）。
    MappingGroupByCreator
      .RegroupByCreator(mapping, new Random(0))
      .Select(e => e.Creator.Value)
      .ShouldBe(new List<string> { "", "", "Alice" });
  }

  [Test]
  public void Regroup_EmptyInput_ReturnsEmpty()
  {
    MappingGroupByCreator.RegroupByCreator(new List<SpellCardMappingEntry>()).Count.ShouldBe(0);
  }
}

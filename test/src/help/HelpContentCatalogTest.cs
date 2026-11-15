namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Help;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 帮助目录单测：分区顺序、全局顺序、id 索引、首末页 prev/next、以及
/// 未知分区 / order 重复 / 正文缺失时的「跳过并降级」行为。
/// </summary>
public class HelpContentCatalogTest : TestClass
{
  public HelpContentCatalogTest(Node testScene)
    : base(testScene) { }

  /// <summary>造一个最小页面元数据。</summary>
  /// <param name="id">页面 id。</param>
  /// <param name="section">分区。</param>
  /// <param name="order">分区内序号。</param>
  /// <param name="title">标题。</param>
  /// <returns>页面。</returns>
  private static HelpPage Page(string id, string section, int order, string title = "标题") =>
    new(id, title, section, order, "摘要", Array.Empty<string>(), Array.Empty<string>());

  /// <summary>用页面集合补齐正文/纯文本/哈希三张表后构造目录。</summary>
  /// <param name="pages">页面集合。</param>
  /// <param name="skipBbcode">需要故意缺正文的页面 id。</param>
  /// <returns>目录。</returns>
  private static HelpContentCatalog Build(IReadOnlyList<HelpPage> pages, string? skipBbcode = null)
  {
    var bbcode = new Dictionary<string, string>(StringComparer.Ordinal);
    var plain = new Dictionary<string, string>(StringComparer.Ordinal);
    var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var page in pages)
    {
      if (page.Id == skipBbcode)
      {
        continue;
      }
      bbcode[page.Id] = "bb-" + page.Id;
      plain[page.Id] = "plain-" + page.Id;
      hashes[page.Id] = "hash-" + page.Id;
    }
    return new HelpContentCatalog(pages, bbcode, plain, hashes);
  }

  [Test]
  public void Pages_AreOrderedBySectionOrderThenOrder()
  {
    var catalog = Build(
      new[]
      {
        Page("merge/overview", "整合", 1),
        Page("start/getting-started", "开始", 2),
        Page("start/index", "开始", 1),
      }
    );

    catalog.Pages.Count.ShouldBe(3);
    catalog.Pages[0].Id.ShouldBe("start/index");
    catalog.Pages[1].Id.ShouldBe("start/getting-started");
    catalog.Pages[2].Id.ShouldBe("merge/overview");
  }

  [Test]
  public void GlobalIndex_AndPrevNext_FollowNavigationOrder()
  {
    var catalog = Build(
      new[]
      {
        Page("start/index", "开始", 1),
        Page("start/getting-started", "开始", 2),
        Page("merge/overview", "整合", 1),
      }
    );

    catalog.GetGlobalIndex("start/getting-started").ShouldBe(1);
    catalog.GetNext("start/getting-started")!.Id.ShouldBe("merge/overview");
    catalog.GetPrevious("start/getting-started")!.Id.ShouldBe("start/index");
    catalog.GetPrevious("start/index").ShouldBeNull();
    catalog.GetNext("merge/overview").ShouldBeNull();
    catalog.GetGlobalIndex("nope/nope").ShouldBe(-1);
  }

  [Test]
  public void UnknownId_ReturnsNullAndEmptyStrings()
  {
    var catalog = Build(new[] { Page("start/index", "开始", 1) });

    catalog.GetPage("start/index").ShouldNotBeNull();
    catalog.GetPage("nope/nope").ShouldBeNull();
    catalog.GetBbcode("nope/nope").ShouldBeEmpty();
    catalog.GetPlainText("nope/nope").ShouldBeEmpty();
    catalog.GetHash("nope/nope").ShouldBeEmpty();
    catalog.GetBbcode("start/index").ShouldBe("bb-start/index");
    catalog.GetPlainText("start/index").ShouldBe("plain-start/index");
    catalog.GetHash("start/index").ShouldBe("hash-start/index");
  }

  [Test]
  public void UnknownSection_IsSkipped()
  {
    var catalog = Build(new[] { Page("start/index", "开始", 1), Page("nope/page", "未知分区", 1) });

    catalog.Pages.Count.ShouldBe(1);
    catalog.GetPage("nope/page").ShouldBeNull();
  }

  [Test]
  public void DuplicateOrderInSameSection_IsSkipped()
  {
    var catalog = Build(new[] { Page("start/index", "开始", 1), Page("start/other", "开始", 1) });

    catalog.Pages.Count.ShouldBe(1);
  }

  [Test]
  public void PageWithoutBbcode_IsSkipped()
  {
    var catalog = Build(new[] { Page("start/index", "开始", 1) }, skipBbcode: "start/index");

    catalog.Pages.ShouldBeEmpty();
  }

  [Test]
  public void NonPositiveOrder_IsSkipped()
  {
    var catalog = Build(new[] { Page("start/index", "开始", 0) });

    catalog.Pages.ShouldBeEmpty();
  }

  [Test]
  public void GetSectionPages_ReturnsSectionInOrder()
  {
    var catalog = Build(
      new[]
      {
        Page("start/index", "开始", 1),
        Page("start/getting-started", "开始", 2),
        Page("merge/overview", "整合", 1),
      }
    );

    catalog.GetSectionPages("开始").Count.ShouldBe(2);
    catalog.GetSectionPages("整合").Count.ShouldBe(1);
    catalog.GetSectionPages("猜测").ShouldBeEmpty();
  }

  [Test]
  public void DefaultPage_IsStartIndexWhenPresent()
  {
    var catalog = Build(
      new[] { Page("merge/overview", "整合", 1), Page("start/index", "开始", 1) }
    );

    catalog.GetDefaultPage()!.Id.ShouldBe(HelpContentCatalog.DefaultPageId);
  }

  [Test]
  public void DefaultPage_FallsBackToFirstPage()
  {
    var catalog = Build(new[] { Page("merge/overview", "整合", 1) });

    catalog.GetDefaultPage()!.Id.ShouldBe("merge/overview");
  }

  [Test]
  public void EmptyCatalog_HasNoDefaultPage()
  {
    var catalog = Build(Array.Empty<HelpPage>());

    catalog.Pages.ShouldBeEmpty();
    catalog.GetDefaultPage().ShouldBeNull();
  }

  [Test]
  public void SectionOrder_ComesFromGeneratedConstants()
  {
    HelpPage.SectionOrder.Count.ShouldBe(7);
    HelpPage.SectionOrder[0].ShouldBe("开始");
    HelpPage.SectionOrder[6].ShouldBe("运行与排障");
  }
}

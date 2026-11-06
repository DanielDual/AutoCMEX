namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// 设置面板「信息」类别页的运行期契约用例：加载真实 <c>SettingsPanel.tscn</c>，
/// 校验双滚动的层级（页面滚动 → 页面内容 → 目标群列表滚动）与录制设置的挂载位置。
/// </summary>
/// <remarks>
/// 这些接线只存在于 .tscn 里，<see cref="InfoConfigPanelTest"/> 用 FakeNodeTree 按名字喂适配器验不到：
/// 场景里丢掉 <c>unique_name_in_owner</c> 或把节点挪错层级时，只有真实场景会失败。滚轮交接由
/// Godot 引擎默认行为提供，这里不重复（以实机手动验证为准）。
/// </remarks>
public class TestSettingsPanelInfoPageRuntime : TestClass
{
  private Node _host = default!;
  private DataManager _dm = default!;
  private SettingsPanel _panel = default!;
  private string _dataDir = string.Empty;

  public TestSettingsPanelInfoPageRuntime(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dataDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}");
    _dm = new DataManager(_dataDir, new AesEncryptor("test-key"));
    _dm.LoadAll();
    _dm.Settings.TargetGroups.Add(
      new TargetGroup
      {
        ChannelId = { Value = "10001" },
        DisplayName = { Value = "示例群" },
        Enabled = { Value = true },
      }
    );

    _host = new Node();
    TestScene.AddChild(_host);

    _panel = GD.Load<PackedScene>("res://src/ui/settings/SettingsPanel.tscn")
      .Instantiate<SettingsPanel>();
    PrepareDependencies(_panel);
    _host.AddChild(_panel);
    _panel.GetNode<Control>("%InfoConfigPanel").Visible = true;
  }

  [Cleanup]
  public void Cleanup()
  {
    if (_host != null && !_host.IsQueuedForDeletion())
      _host.QueueFree();
    _dm?.Dispose();
    if (Directory.Exists(_dataDir))
      Directory.Delete(_dataDir, recursive: true);
  }

  [Test]
  public void RealScene_InfoPage_HasPageScrollAndBoundedListScroll()
  {
    var pageScroll = _panel.GetNode<ScrollContainer>("%PageScroll");
    var pageBody = _panel.GetNode<VBoxContainer>("%PageBody");
    var groupScroll = _panel.GetNode<ScrollContainer>("%GroupScroll");

    pageBody.GetParent().ShouldBe(pageScroll);
    groupScroll.GetParent().ShouldBe(pageBody);
    groupScroll.CustomMinimumSize.Y.ShouldBe(600);
    pageScroll.HorizontalScrollMode.ShouldBe(ScrollContainer.ScrollMode.Disabled);
    groupScroll.HorizontalScrollMode.ShouldBe(ScrollContainer.ScrollMode.Disabled);
    pageScroll.GetHScrollBar().Visible.ShouldBeFalse();
    groupScroll.GetHScrollBar().Visible.ShouldBeFalse();
  }

  [Test]
  public void RealScene_RecordingSection_IsAlwaysOnAndLastInPageBody()
  {
    var info = _panel.GetNode<InfoConfigPanel>("%InfoConfigPanel");
    var pageBody = _panel.GetNode<VBoxContainer>("%PageBody");
    var groupScroll = _panel.GetNode<ScrollContainer>("%GroupScroll");

    var section = info.RecordingSection.ShouldNotBeNull();
    pageBody.GetChild(pageBody.GetChildCount() - 1).ShouldBe(section);
    groupScroll.GetIndex().ShouldBeLessThan(section.GetIndex());
    section.SectionTitle.Text.ShouldBe("符卡 GIF 录制设置");
    section.SectionTitle.ShouldNotBeAssignableTo<BaseButton>();

    // 常显：没有折叠复选框，控件构造完即可见，不需要先「展开」
    for (var i = 0; i < section.GetChildCount(); i++)
      section.GetChild(i).ShouldNotBeAssignableTo<CheckBox>();
    section.EngineDirEdit.Visible.ShouldBeTrue();
    section.PluginStatusLabel.Visible.ShouldBeTrue();
  }

  /// <summary>场景里的面板都是 AutoInject 节点：测试期统一标记并把 DataManager 伪造成依赖。</summary>
  /// <param name="root">场景根节点。</param>
  private void PrepareDependencies(Node root)
  {
    if (root is IAutoInit autoInit)
      autoInit.IsTesting = true;
    if (root is IDependent dependent)
      dependent.FakeDependency<DataManager>(_dm);

    foreach (var child in root.GetChildren())
      PrepareDependencies(child);
  }
}

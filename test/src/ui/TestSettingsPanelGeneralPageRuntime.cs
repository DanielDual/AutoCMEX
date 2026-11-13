namespace AutoCMEX;

using System;
using System.IO;
using AutoCMEX.Core.Info;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.UI;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 设置面板「通用」类别页的运行期接线用例：加载真实 <c>SettingsPanel.tscn</c>，
/// 校验通用页场景实例确实挂在共享场景里、下拉项来自 <see cref="ThemeCatalog"/>，
/// 并与其它类别页互斥显隐。
/// </summary>
/// <remarks>
/// 这些接线只存在于 .tscn 里（<c>instance=ExtResource</c> 实例 + <c>unique_name_in_owner</c>），
/// <see cref="SettingsPanelTest"/> 用 FakeNodeTree 按名字喂替身验不到：场景里丢掉唯一名、
/// 实例脚本挂错或漏挂，只有加载真实场景才会失败。
/// </remarks>
public class TestSettingsPanelGeneralPageRuntime : TestClass
{
  private Node _host = default!;
  private DataManager _dm = default!;
  private SettingsPanel _panel = default!;
  private string _dataDir = string.Empty;

  public TestSettingsPanelGeneralPageRuntime(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dataDir = Path.Combine(Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}");
    _dm = new DataManager(_dataDir, new AesEncryptor("test-key"));
    _dm.LoadAll();

    _host = new Node();
    TestScene.AddChild(_host);

    _panel = GD.Load<PackedScene>("res://src/ui/settings/SettingsPanel.tscn")
      .Instantiate<SettingsPanel>();
    PrepareDependencies(_panel);
    _host.AddChild(_panel);
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

  /// <summary>通用页是真实场景实例，数据管理器与设置面板拿到的是同一份。</summary>
  [Test]
  public void RealScene_GeneralPage_IsGeneralConfigPanelInstance()
  {
    var general = _panel.GetNode<GeneralConfigPanel>("%GeneralConfigPanel");

    general.ShouldNotBeNull();
    general.ShouldBeAssignableTo<IGeneralConfigPanel>();
    _panel.GeneralConfigPanel.ShouldBeSameAs(general);
    general.DataManager.ShouldBeSameAs(_dm);

    // 下拉项来自固定清单：场景里漏挂面板时这里会是 0
    general.ThemeSelect.ItemCount.ShouldBe(ThemeCatalog.Options.Count);
  }

  /// <summary>真实类别列表发 <c>item_selected</c>：「通用」=下标 6 独占显隐，切回 0 隐藏。</summary>
  [Test]
  public void RealScene_GeneralCategory_TogglesOnlyGeneralPage()
  {
    var general = _panel.GetNode<Control>("%GeneralConfigPanel");
    var aiModel = _panel.GetNode<Control>("%AiModelConfigPanel");
    var info = _panel.GetNode<Control>("%InfoConfigPanel");
    var categoryList = _panel.GetNode<ItemList>("%CategoryList");

    general.Visible.ShouldBeFalse();

    categoryList.EmitSignal(ItemList.SignalName.ItemSelected, 6L);

    general.Visible.ShouldBeTrue();
    aiModel.Visible.ShouldBeFalse();
    info.Visible.ShouldBeFalse();

    categoryList.EmitSignal(ItemList.SignalName.ItemSelected, 0L);

    general.Visible.ShouldBeFalse();
    aiModel.Visible.ShouldBeTrue();
  }

  /// <summary>场景里的面板都是 AutoInject 节点：测试期统一标记并把依赖伪造成同一实例。</summary>
  /// <param name="root">场景根节点。</param>
  private void PrepareDependencies(Node root)
  {
    if (root is IAutoInit autoInit)
      autoInit.IsTesting = true;
    if (root is IDependent dependent)
    {
      dependent.FakeDependency<DataManager>(_dm);
      // 「信息」类别页还依赖 WebSocket 服务端与信息事件总线（刷新群列表 / 入站回执），
      // 不提供会让它的 OnResolved 中断，本页用例也就不再是「整页可解析」的状态。
      dependent.FakeDependency<IWebSocketServer>(new Mock<IWebSocketServer>().Object);
      dependent.FakeDependency<InfoEventBus>(new InfoEventBus());
    }

    foreach (var child in root.GetChildren())
      PrepareDependencies(child);
  }
}

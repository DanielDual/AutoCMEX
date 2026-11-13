namespace AutoCMEX;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

/// <summary>
/// 通用配置面板的替身单测：下拉填充顺序、设置值绑定驱动的回填、用户选择写回、
/// 回填护栏（程序化 Select 不反写设置）、存档未知主题的回退提示，以及离树后停止回填。
/// </summary>
/// <remarks>
/// 面板不入真实场景树，全部用 <c>FakeNodeTree</c> + <c>FakeDependency</c> 驱动（规则 25/26）；
/// 绑定回调经 <c>CallDeferred</c> 落到下一帧，因此每次改模型后都要等两帧再断言（规则 30）。
/// </remarks>
public class TestGeneralConfigPanel : TestClass
{
  private GeneralConfigPanel _panel = default!;
  private DataManager _dm = default!;
  private Mock<IOptionButton> _themeSelect = default!;
  private Mock<ILabel> _themeStatusLabel = default!;

  /// <summary>下拉里被实际添加的显示名（按添加顺序），用于校验清单顺序契约。</summary>
  private readonly List<string> _addedOptions = new();

  private readonly List<Node> _toCleanup = new();

  public TestGeneralConfigPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new GeneralConfigPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    _addedOptions.Clear();

    _themeSelect = new Mock<IOptionButton>();
    _themeSelect.SetupProperty(m => m.ItemCount, 0);
    _themeSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback<string, int>(
        (text, _) =>
        {
          _addedOptions.Add(text);
          _themeSelect.Object.ItemCount++;
        }
      );

    _themeStatusLabel = new Mock<ILabel>();
    _themeStatusLabel.SetupProperty(m => m.Text, string.Empty);
    _themeStatusLabel.SetupProperty(m => m.Visible, false);

    _panel.FakeNodeTree(
      new()
      {
        ["%ThemeSelect"] = _themeSelect.Object,
        ["%ThemeStatusLabel"] = _themeStatusLabel.Object,
      }
    );

    _panel.FakeDependency<DataManager>(_dm);
    _panel._Notification((int)Node.NotificationEnterTree);
    _panel._Notification((int)Node.NotificationReady);
  }

  [Cleanup]
  public void Cleanup()
  {
    foreach (var node in _toCleanup)
    {
      if (node != null && !node.IsQueuedForDeletion())
        node.QueueFree();
    }
    _toCleanup.Clear();
  }

  /// <summary>等引擎把本帧排队的 <c>CallDeferred</c> 回填执行完（规则 30：绑定回调后要等帧再断言）。</summary>
  private async Task WaitRefillAsync()
  {
    await _panel.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
    await _panel.ToSignal(TestScene.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  /// <summary>挂一个模型写入计数绑定（订阅即回放一次，调用方随后清零）。</summary>
  private IDisposable CountThemeWrites(Action onWrite) =>
    _dm.Settings.ThemeId.Bind().OnValue(_ => onWrite());

  [Test]
  public void OnReady_FillsDropdownInCatalogOrder()
  {
    _panel.ThemeSelect.ItemCount.ShouldBe(ThemeCatalog.Options.Count);
    _themeSelect.Verify(
      m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()),
      Times.Exactly(ThemeCatalog.Options.Count)
    );

    // 顺序即界面契约：下标既是回填目标，也是写回时的清单索引
    _addedOptions.Count.ShouldBe(ThemeCatalog.Options.Count);
    for (var i = 0; i < ThemeCatalog.Options.Count; i++)
      _addedOptions[i].ShouldBe(ThemeCatalog.Options[i].DisplayName);
  }

  /// <summary>外部改设置 → 绑定排一次回填 → 下拉选中对应下标，且提示行隐藏。</summary>
  [Test]
  public async Task ExternalThemeChange_SelectsMatchingIndexAndHidesStatus()
  {
    var lightIndex = IndexOf("light-default");
    lightIndex.ShouldBeGreaterThan(0);

    // 先让提示行出现，才能验证「切到可用主题后隐藏」这一次状态转移
    _dm.Settings.ThemeId.Value = "nonexistent-theme";
    await WaitRefillAsync();
    _themeStatusLabel.Object.Visible.ShouldBeTrue();

    _dm.Settings.ThemeId.Value = "light-default";
    await WaitRefillAsync();

    _themeSelect.Verify(m => m.Select(lightIndex), Times.Once);
    _themeStatusLabel.Object.Visible.ShouldBeFalse();
  }

  /// <summary>用户选择「靛蓝夜色」→ 写回设置里的主题 ID（应用主题由 MainWindow 的绑定负责）。</summary>
  [Test]
  public void UserSelection_WritesThemeId()
  {
    var indigoIndex = IndexOf("indigo-night");
    indigoIndex.ShouldBeGreaterThan(0);

    _themeSelect.Raise(m => m.ItemSelected += null, (long)indigoIndex);

    _dm.Settings.ThemeId.Value.ShouldBe("indigo-night");
  }

  /// <summary>程序化回填不反写：写入计数只来自外部那一次。</summary>
  [Test]
  public async Task ProgrammaticRefill_DoesNotWriteBackToSettings()
  {
    var lightIndex = IndexOf("light-default");
    var writes = 0;
    var binding = CountThemeWrites(() => writes++);
    writes = 0;

    try
    {
      _dm.Settings.ThemeId.Value = "light-default";

      await WaitRefillAsync();

      // 回填是程序化 Select：若没有护栏，这里会多出一笔写回并触发一次自动保存
      writes.ShouldBe(1);
      _dm.Settings.ThemeId.Value.ShouldBe("light-default");
      _themeSelect.Verify(m => m.Select(lightIndex), Times.Once);
    }
    finally
    {
      binding.Dispose();
    }
  }

  /// <summary>存档里的未知主题：选中「默认」+ 提示行可见，不改写设置。</summary>
  [Test]
  public async Task UnknownStoredTheme_FallsBackToDefaultWithStatus()
  {
    var writes = 0;
    var binding = CountThemeWrites(() => writes++);
    writes = 0;

    try
    {
      _dm.Settings.ThemeId.Value = "nonexistent-theme";

      await WaitRefillAsync();

      _themeSelect.Verify(m => m.Select(0), Times.AtLeastOnce);
      _themeStatusLabel.Object.Text.ShouldContain("不可用");
      _themeStatusLabel.Object.Visible.ShouldBeTrue();

      // 存储值原样保留（用户可能只是从别处拷来配置），只提示不回写
      writes.ShouldBe(1);
      _dm.Settings.ThemeId.Value.ShouldBe("nonexistent-theme");
    }
    finally
    {
      binding.Dispose();
    }
  }

  /// <summary>选中项与当前设置相同时不写模型（避免无意义的自动保存）。</summary>
  [Test]
  public void SelectingCurrentTheme_DoesNotWriteModel()
  {
    _dm.Settings.ThemeId.Value.ShouldBe(ThemeCatalog.DefaultId);

    var writes = 0;
    var binding = CountThemeWrites(() => writes++);
    writes = 0;

    try
    {
      _themeSelect.Raise(m => m.ItemSelected += null, 0L);

      writes.ShouldBe(0);
      _dm.Settings.ThemeId.Value.ShouldBe(ThemeCatalog.DefaultId);
    }
    finally
    {
      binding.Dispose();
    }
  }

  /// <summary>离树（绑定已释放）后外部改设置不再回填控件。</summary>
  [Test]
  public async Task ExitTree_StopsRefillingDropdown()
  {
    var lightIndex = IndexOf("light-default");

    // 替身面板不入树，引擎不会替我们发退出通知，这里手工触发
    _panel._Notification((int)Node.NotificationExitTree);

    _dm.Settings.ThemeId.Value = "light-default";

    await WaitRefillAsync();

    _themeSelect.Verify(m => m.Select(lightIndex), Times.Never);
  }

  /// <summary>在主题清单里按稳定 ID 找下标（与面板回填用的是同一份清单）。</summary>
  private static int IndexOf(string themeId)
  {
    for (var i = 0; i < ThemeCatalog.Options.Count; i++)
    {
      if (ThemeCatalog.Options[i].Id == themeId)
        return i;
    }

    return -1;
  }
}

namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Chickensoft.GoDotTest;
using Godot;
using Moq;
using Shouldly;

public class TestChatConfigPanel : TestClass
{
  private ChatConfigPanel _panel = default!;
  private DataManager _dm = default!;
  private Mock<ISpinBox> _portInput = default!;
  private Mock<IOptionButton> _modeSelect = default!;
  private Mock<ILineEdit> _koishiUrlInput = default!;
  private Mock<IHBoxContainer> _koishiUrlRow = default!;
  private Mock<IOptionButton> _filterSelect = default!;
  private Mock<ILabel> _pluginPathLabel = default!;
  private readonly List<Node> _toCleanup = new();

  public TestChatConfigPanel(Node testScene)
    : base(testScene) { }

  [Setup]
  public void Setup()
  {
    _dm = new DataManager(
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AutoCMEX_Test_{Guid.NewGuid():N}"),
      new AesEncryptor("test-key")
    );
    _dm.LoadAll();

    _panel = new ChatConfigPanel();
    (_panel as IAutoInit).IsTesting = true;
    _toCleanup.Add(_panel);

    _portInput = new Mock<ISpinBox>();
    _portInput.SetupProperty(m => m.MinValue);
    _portInput.SetupProperty(m => m.MaxValue);
    _modeSelect = new Mock<IOptionButton>();
    _modeSelect.SetupProperty(m => m.ItemCount, 0);
    _modeSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback(() => _modeSelect.Object.ItemCount++);
    _koishiUrlInput = new Mock<ILineEdit>();
    _koishiUrlRow = new Mock<IHBoxContainer>();
    _filterSelect = new Mock<IOptionButton>();
    _filterSelect.SetupProperty(m => m.ItemCount, 0);
    _filterSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback(() => _filterSelect.Object.ItemCount++);
    var installBtn = new Mock<IButton>();
    _pluginPathLabel = new Mock<ILabel>();
    var pluginFileDialog = new Mock<IFileDialog>();
    var pluginOkDialog = new Mock<IAcceptDialog>();

    _panel.FakeNodeTree(
      new()
      {
        ["%PortInput"] = _portInput.Object,
        ["%ModeSelect"] = _modeSelect.Object,
        ["%KoishiUrlInput"] = _koishiUrlInput.Object,
        ["%KoishiUrlRow"] = _koishiUrlRow.Object,
        ["%FilterSelect"] = _filterSelect.Object,
        ["%InstallBtn"] = installBtn.Object,
        ["%PluginPathLabel"] = _pluginPathLabel.Object,
        ["%PluginFileDialog"] = pluginFileDialog.Object,
        ["%PluginOkDialog"] = pluginOkDialog.Object,
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

  [Test]
  public void Panel_IsNotNull()
  {
    _panel.ShouldNotBeNull();
  }

  [Test]
  public void PortInput_HasCorrectRange()
  {
    _panel.PortInput.MinValue.ShouldBe(1);
    _panel.PortInput.MaxValue.ShouldBe(65535);
  }

  [Test]
  public void ModeSelect_HasTwoOptions()
  {
    _panel.ModeSelect.ItemCount.ShouldBe(2);
  }

  [Test]
  public void FilterSelect_HasThreeOptions()
  {
    _panel.FilterSelect.ItemCount.ShouldBe(3);
  }

  [Test]
  public void InstallBtn_IsNotNull()
  {
    _panel.InstallBtn.ShouldNotBeNull();
  }

  [Test]
  public void Refresh_PullsSettingsIntoControls()
  {
    _dm.Settings.WebSocketPort.Value = 5200;
    _dm.Settings.WebSocketMode.Value = "Client";
    _dm.Settings.MessageFilterMode.Value = "ai";
    _dm.Settings.KoishiWebSocketUrl.Value = "ws://localhost:5140";

    _panel.Refresh();

    _portInput.VerifySet(m => m.Value = 5200);
    _modeSelect.Verify(m => m.Select(1), Times.AtLeastOnce);
    _filterSelect.Verify(m => m.Select(1), Times.AtLeastOnce);
    _koishiUrlRow.VerifySet(m => m.Visible = true);
    _koishiUrlInput.VerifySet(m => m.Text = "ws://localhost:5140");
  }

  [Test]
  public void Refresh_ShowsUrlRowOnlyInClientMode()
  {
    _dm.Settings.WebSocketMode.Value = "Server";

    _panel.Refresh();

    _koishiUrlRow.VerifySet(m => m.Visible = false);
  }

  [Test]
  public void PortChanged_UserDriven_WritesBack()
  {
    _portInput.Raise(m => m.ValueChanged += null, 5200d);

    _dm.Settings.WebSocketPort.Value.ShouldBe(5200);
  }

  [Test]
  public void KoishiUrl_TextSubmitted_WritesBackTrimmedValue()
  {
    _koishiUrlInput.Raise(m => m.TextSubmitted += null, "  ws://localhost:5140  ");

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
    _koishiUrlInput.VerifySet(m => m.Text = "ws://localhost:5140");
  }

  [Test]
  public void KoishiUrl_TextSubmitted_KeepsUnchangedValue()
  {
    _dm.Settings.KoishiWebSocketUrl.Value = "ws://localhost:5140";

    _koishiUrlInput.Raise(m => m.TextSubmitted += null, "ws://localhost:5140");

    _dm.Settings.KoishiWebSocketUrl.Value.ShouldBe("ws://localhost:5140");
  }

  [Test]
  public void PluginPathLabel_ShowsNotInstalled_ByDefault()
  {
    _pluginPathLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains("未安装")));
  }

  [Test]
  public void PluginPathLabel_ShowsInstalledPath()
  {
    const string dest = @"C:\koishi\plugins\auto-cmex";
    _dm.Settings.KoishiPluginPath.Value = dest;

    _panel.Refresh();

    _pluginPathLabel.VerifySet(m => m.Text = It.Is<string>(s => s.Contains(dest)));
  }
}

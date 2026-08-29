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

    var portInput = new Mock<ISpinBox>();
    portInput.SetupProperty(m => m.MinValue);
    portInput.SetupProperty(m => m.MaxValue);
    var modeSelect = new Mock<IOptionButton>();
    modeSelect.SetupProperty(m => m.ItemCount, 0);
    modeSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback(() => modeSelect.Object.ItemCount++);
    var koishiUrlInput = new Mock<ILineEdit>();
    var koishiUrlRow = new Mock<IHBoxContainer>();
    var filterSelect = new Mock<IOptionButton>();
    filterSelect.SetupProperty(m => m.ItemCount, 0);
    filterSelect
      .Setup(m => m.AddItem(It.IsAny<string>(), It.IsAny<int>()))
      .Callback(() => filterSelect.Object.ItemCount++);
    var installBtn = new Mock<IButton>();
    var pluginFileDialog = new Mock<IFileDialog>();
    var pluginOkDialog = new Mock<IAcceptDialog>();

    _panel.FakeNodeTree(
      new()
      {
        ["%PortInput"] = portInput.Object,
        ["%ModeSelect"] = modeSelect.Object,
        ["%KoishiUrlInput"] = koishiUrlInput.Object,
        ["%KoishiUrlRow"] = koishiUrlRow.Object,
        ["%FilterSelect"] = filterSelect.Object,
        ["%InstallBtn"] = installBtn.Object,
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
}

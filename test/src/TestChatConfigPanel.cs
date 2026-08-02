namespace AutoCMEX;

using System;
using System.Collections.Generic;
using AutoCMEX.Core.Storage;
using AutoCMEX.UI.Settings;
using Chickensoft.AutoInject;
using Chickensoft.GoDotTest;
using Godot;
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

    var portInput = new SpinBox();
    _panel.AddChild(portInput);
    _panel.PortInput = portInput;

    var modeSelect = new OptionButton();
    _panel.AddChild(modeSelect);
    _panel.ModeSelect = modeSelect;

    var koishiUrlInput = new LineEdit();
    _panel.AddChild(koishiUrlInput);
    _panel.KoishiUrlInput = koishiUrlInput;

    var koishiUrlRow = new HBoxContainer();
    _panel.AddChild(koishiUrlRow);
    _panel.KoishiUrlRow = koishiUrlRow;

    var filterSelect = new OptionButton();
    _panel.AddChild(filterSelect);
    _panel.FilterSelect = filterSelect;

    var installBtn = new Button();
    _panel.AddChild(installBtn);
    _panel.InstallBtn = installBtn;

    var pluginFileDialog = new FileDialog();
    _panel.AddChild(pluginFileDialog);
    _panel.PluginFileDialog = pluginFileDialog;

    var pluginOkDialog = new AcceptDialog();
    _panel.AddChild(pluginOkDialog);
    _panel.PluginOkDialog = pluginOkDialog;

    _panel.FakeDependency<DataManager>(_dm);
    _panel.OnReady();
    _panel.OnResolved();
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

namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Godot;

/// <summary>
/// Test driver for <see cref="WebSocketPanel"/> — encapsulates setup with
/// fake node tree and fake dependency for unit testing.
/// </summary>
public sealed class WebSocketPanelDriver : IDisposable
{
  public WebSocketPanel Panel { get; }
  public Label StatusLabel { get; }
  public Label ModeLabel { get; }
  public Label PortLabel { get; }
  public Label ConnectionCountLabel { get; }
  public Label EventLabel { get; }
  public Button StartStopBtn { get; }
  public ItemList ClientList { get; }
  public Timer RefreshTimer { get; }

  public WebSocketPanelDriver(IWebSocketServer? server = null)
  {
    Panel = new WebSocketPanel();

    StatusLabel = new Label { Name = "StatusLabel", UniqueNameInOwner = true };
    Panel.AddChild(StatusLabel);
    Panel.StatusLabel = StatusLabel;

    ModeLabel = new Label { Name = "ModeLabel", UniqueNameInOwner = true };
    Panel.AddChild(ModeLabel);
    Panel.ModeLabel = ModeLabel;

    PortLabel = new Label { Name = "PortLabel", UniqueNameInOwner = true };
    Panel.AddChild(PortLabel);
    Panel.PortLabel = PortLabel;

    ConnectionCountLabel = new Label { Name = "ConnectionCountLabel", UniqueNameInOwner = true };
    Panel.AddChild(ConnectionCountLabel);
    Panel.ConnectionCountLabel = ConnectionCountLabel;

    EventLabel = new Label { Name = "EventLabel", UniqueNameInOwner = true };
    Panel.AddChild(EventLabel);
    Panel.EventLabel = EventLabel;

    StartStopBtn = new Button { Name = "StartStopBtn", UniqueNameInOwner = true };
    Panel.AddChild(StartStopBtn);
    Panel.StartStopBtn = StartStopBtn;

    ClientList = new ItemList { Name = "ClientList", UniqueNameInOwner = true };
    Panel.AddChild(ClientList);
    Panel.ClientList = ClientList;

    RefreshTimer = new Timer { Name = "RefreshTimer", UniqueNameInOwner = true };
    Panel.AddChild(RefreshTimer);
    Panel.RefreshTimer = RefreshTimer;

    if (server != null)
      Panel.FakeDependency<IWebSocketServer>(server);

    Panel._Notification((int)Node.NotificationReady);
  }

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
      Panel.QueueFree();
  }
}

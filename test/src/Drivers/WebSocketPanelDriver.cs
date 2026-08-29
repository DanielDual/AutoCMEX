namespace AutoCMEX.Test.Drivers;

using System;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.UI.WebSocket;
using Chickensoft.AutoInject;
using Chickensoft.GodotNodeInterfaces;
using Godot;
using Moq;

public sealed class WebSocketPanelDriver : IDisposable
{
  public WebSocketPanel Panel { get; }
  public Mock<ILabel> StatusLabel { get; }
  public Mock<ILabel> ModeLabel { get; }
  public Mock<ILabel> PortLabel { get; }
  public Mock<ILabel> ConnectionCountLabel { get; }
  public Mock<ILabel> EventLabel { get; }
  public Mock<IButton> StartStopBtn { get; }
  public Mock<IItemList> ClientList { get; }
  public Mock<ITimer> RefreshTimer { get; }

  public WebSocketPanelDriver(IWebSocketServer? server = null)
  {
    Panel = new WebSocketPanel();
    (Panel as IAutoInit).IsTesting = true;

    StatusLabel = new Mock<ILabel>();
    ModeLabel = new Mock<ILabel>();
    PortLabel = new Mock<ILabel>();
    ConnectionCountLabel = new Mock<ILabel>();
    EventLabel = new Mock<ILabel>();
    StartStopBtn = new Mock<IButton>();
    ClientList = new Mock<IItemList>();
    RefreshTimer = new Mock<ITimer>();

    Panel.FakeNodeTree(
      new()
      {
        ["%StatusLabel"] = StatusLabel.Object,
        ["%ModeLabel"] = ModeLabel.Object,
        ["%PortLabel"] = PortLabel.Object,
        ["%ConnectionCountLabel"] = ConnectionCountLabel.Object,
        ["%EventLabel"] = EventLabel.Object,
        ["%StartStopBtn"] = StartStopBtn.Object,
        ["%ClientList"] = ClientList.Object,
        ["%RefreshTimer"] = RefreshTimer.Object,
      }
    );

    if (server != null)
      Panel.FakeDependency<IWebSocketServer>(server);

    Panel._Notification((int)Node.NotificationEnterTree);
    Panel._Notification((int)Node.NotificationReady);
  }

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
      Panel.QueueFree();
  }
}

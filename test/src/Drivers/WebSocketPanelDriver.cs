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
  public Mock<ILabel> ErrorLabel { get; }
  public Mock<IButton> StartStopBtn { get; }
  public Mock<ILabel> ClientListLabel { get; }
  public Mock<IItemList> ClientList { get; }
  public Mock<ITimer> RefreshTimer { get; }

  /// <summary>注入给面板的服务器替身（构造时传入；为空表示未注入依赖）。</summary>
  public IWebSocketServer? Server { get; }

  public WebSocketPanelDriver(IWebSocketServer? server = null)
  {
    Server = server;
    Panel = new WebSocketPanel();
    (Panel as IAutoInit).IsTesting = true;

    StatusLabel = new Mock<ILabel>();
    ModeLabel = new Mock<ILabel>();
    PortLabel = new Mock<ILabel>();
    ConnectionCountLabel = new Mock<ILabel>();
    EventLabel = new Mock<ILabel>();
    ErrorLabel = new Mock<ILabel>();
    StartStopBtn = new Mock<IButton>();
    ClientListLabel = new Mock<ILabel>();
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
        ["%ErrorLabel"] = ErrorLabel.Object,
        ["%StartStopBtn"] = StartStopBtn.Object,
        ["%ClientListLabel"] = ClientListLabel.Object,
        ["%ClientList"] = ClientList.Object,
        ["%RefreshTimer"] = RefreshTimer.Object,
      }
    );

    if (server != null)
      Panel.FakeDependency<IWebSocketServer>(server);

    Panel._Notification((int)Node.NotificationEnterTree);
    Panel._Notification((int)Node.NotificationReady);
  }

  /// <summary>
  /// 触发服务器状态变化事件，驱动面板按事件刷新（用于验证「事件驱动」而非定时轮询）。
  /// </summary>
  /// <remarks>
  /// 目前只支持 <see cref="MockWebSocketServer"/>；Moq 打的替身请直接在其 Mock 上
  /// <c>Raise(m =&gt; m.OnStateChanged += null)</c>，本方法不去猜测具体实现。
  /// </remarks>
  public void RaiseStateChanged()
  {
    if (Server is MockWebSocketServer fake)
      fake.RaiseStateChanged();
  }

  public void Dispose()
  {
    if (Panel != null && !Panel.IsQueuedForDeletion())
      Panel.QueueFree();
  }
}

namespace AutoCMEX.Core.Info;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Core.WebSocket;
using AutoCMEX.Models;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 表格出图入口。
/// </summary>
/// <remarks>
/// 抽成接口是为了让发布编排可在不依赖 Godot 离屏渲染的前提下单测（单测注入假实现即可验证
/// 预检、串行顺序与失败即停）。真实实现见 <see cref="TableImageFactory"/>。
/// </remarks>
public interface ITableImageFactory
{
  /// <summary>把表格模型渲染为 PNG。</summary>
  /// <param name="model">表格渲染模型。</param>
  /// <param name="host">离屏渲染宿主节点（需在场景树内）。</param>
  /// <returns>出图结果。</returns>
  Task<TableImageResult> RenderAsync(TableModel model, Node host);
}

/// <summary>
/// 默认出图实现：委托 <see cref="TableImageRenderer"/> 输出到系统临时目录。
/// </summary>
/// <remarks>
/// 输出文件名由表标题推导，同一张表每次出图覆盖同一文件：避免临时目录随发布次数堆积，
/// 且发布是串行的，不存在并发覆写。
/// </remarks>
public sealed class TableImageFactory : ITableImageFactory
{
  /// <inheritdoc/>
  public Task<TableImageResult> RenderAsync(TableModel model, Node host)
  {
    ArgumentNullException.ThrowIfNull(model);

    var fileName = string.Concat(
      model.Title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
    );
    if (string.IsNullOrWhiteSpace(fileName))
      fileName = "info_table";

    var outputPath = TableImageRenderer.BuildTempOutputPath($"{fileName}.png");
    return TableImageRenderer.RenderToPngAsync(host, model, outputPath);
  }
}

/// <summary>
/// 合并转发中的一个节点：可定位的标题 + 可选图片附件。
/// </summary>
/// <param name="Index">条目序号（GIF 集的清单序号；非逐条内容为 0）。</param>
/// <param name="Title">条目标题（如 <c>12. 符卡名</c>，或整表标题）。</param>
/// <param name="ImageFilePath">图片绝对路径；为 null 表示纯文本节点。</param>
internal sealed record PublishNode(int Index, string Title, string? ImageFilePath);

/// <summary>
/// 单次合并转发发送的回执。
/// </summary>
/// <param name="IsSuccess">本次是否发送成功。</param>
/// <param name="ErrorMessage">失败原因（发送失败或等待回执超时）。</param>
/// <param name="FailedNodes">逐条失败明细（Koishi 侧回报）。</param>
internal sealed record PublishAck(
  bool IsSuccess,
  string ErrorMessage,
  IReadOnlyList<PublishFailure> FailedNodes
)
{
  public static PublishAck Ok() => new(true, string.Empty, Array.Empty<PublishFailure>());

  public static PublishAck Failed(
    string errorMessage,
    IReadOnlyList<PublishFailure>? failedNodes = null
  ) => new(false, errorMessage, failedNodes ?? Array.Empty<PublishFailure>());
}

/// <summary>
/// 发布编排：预检 → 按固定顺序串行发布 → 逐项独立回报，支持只重试失败项。
/// </summary>
/// <remarks>
/// <para>
/// 三道防线对应 NapCat 侧的两个硬约束（合并转发整条消息只能由 <c>node</c> 组成；图片经适配器
/// 上传时并发无上限且失败只记 warn 不抛错）：
/// </para>
/// <list type="number">
/// <item><description>发送前预检：文件不存在/为空/无可发布内容直接拦下，不发出半套内容。</description></item>
/// <item><description>串行发送：一个目标群确认回执后再发下一个，不让适配器并发上传图片。</description></item>
/// <item><description>显式回执：Koishi 必须回传逐条结果，超时按失败处理，不把「已发出」当「已送达」。</description></item>
/// </list>
/// <para>
/// 任一项失败即停止后续项（失败即停）：避免"群里已经有了 GIF 却没有表"的中间态继续扩大。
/// </para>
/// </remarks>
public sealed class PublishService
{
  /// <summary>
  /// 等待单条合并转发回执的默认超时（毫秒）。
  /// </summary>
  /// <remarks>
  /// 图片需经 NapCat 逐张上传，不能用偏小的超时；但也不能无限等，否则界面会永久卡在"发布中"。
  /// </remarks>
  public const int DefaultAckTimeoutMs = 180_000;

  /// <summary>
  /// 本次运行使用的回执超时（毫秒）；测试可用较小值验证超时路径。
  /// </summary>
  public int AckTimeoutMs { get; set; } = DefaultAckTimeoutMs;

  private readonly DataManager _dataManager;
  private readonly InfoDataService _dataService;
  private readonly GifSetService _gifSets;
  private readonly IWebSocketServer _server;
  private readonly ITableImageFactory _tableImages;
  private readonly ILog _log;

  /// <summary>
  /// 等待回执的请求表。
  /// </summary>
  /// <remarks>
  /// 发送在主线程、回执在 WebSocket 接收线程投递，两侧都会访问这张表，因此所有访问都在
  /// <see cref="_pendingLock"/> 内完成；<see cref="TaskCompletionSource{TResult}"/> 以
  /// <c>RunContinuationsAsynchronously</c> 创建，避免在锁内执行发布后续逻辑。
  /// </remarks>
  private readonly Dictionary<string, TaskCompletionSource<PublishAck>> _pending = new();

  private readonly object _pendingLock = new();

  /// <summary>
  /// 创建发布服务（使用默认出图实现与默认日志）。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="dataService">信息数据服务。</param>
  /// <param name="gifSets">GIF 集服务。</param>
  /// <param name="server">WebSocket 服务端（出站通道）。</param>
  public PublishService(
    DataManager dataManager,
    InfoDataService dataService,
    GifSetService gifSets,
    IWebSocketServer server
  )
    : this(
      dataManager,
      dataService,
      gifSets,
      server,
      new TableImageFactory(),
      AppLogs.GetOrCreate().GetLogger(nameof(PublishService))
    ) { }

  /// <summary>
  /// 创建发布服务（出图实现与日志注入，便于单测）。
  /// </summary>
  /// <param name="dataManager">数据管理器。</param>
  /// <param name="dataService">信息数据服务。</param>
  /// <param name="gifSets">GIF 集服务。</param>
  /// <param name="server">WebSocket 服务端（出站通道）。</param>
  /// <param name="tableImages">表格出图实现。</param>
  /// <param name="log">日志。</param>
  public PublishService(
    DataManager dataManager,
    InfoDataService dataService,
    GifSetService gifSets,
    IWebSocketServer server,
    ITableImageFactory tableImages,
    ILog log
  )
  {
    _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
    _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    _gifSets = gifSets ?? throw new ArgumentNullException(nameof(gifSets));
    _server = server ?? throw new ArgumentNullException(nameof(server));
    _tableImages = tableImages ?? throw new ArgumentNullException(nameof(tableImages));
    _log = log ?? throw new ArgumentNullException(nameof(log));
  }

  /// <summary>
  /// 发布单项内容。
  /// </summary>
  /// <param name="kind">发布项类别。</param>
  /// <param name="targetGroupIds">目标群 channelId 列表。</param>
  /// <param name="renderHost">表格出图宿主节点（GIF 集与规则不需要，可为 null）。</param>
  /// <returns>单项发布报告。</returns>
  public async Task<PublishReport> PublishAsync(
    PublishItemKind kind,
    IReadOnlyList<string> targetGroupIds,
    Node? renderHost
  )
  {
    var result = await PublishItemAsync(kind, targetGroupIds, renderHost);
    return new PublishReport { Items = new[] { result } };
  }

  /// <summary>
  /// 按固定顺序发布多项（一键转发 / 只重试失败项共用同一入口）。
  /// </summary>
  /// <param name="targetGroupIds">目标群 channelId 列表。</param>
  /// <param name="renderHost">表格出图宿主节点。</param>
  /// <param name="onlyKinds">只发布这些项（为 null 或空表示全部）；重试失败项时传入失败类别。</param>
  /// <returns>发布报告。</returns>
  public async Task<PublishReport> PublishAllAsync(
    IReadOnlyList<string> targetGroupIds,
    Node? renderHost,
    IReadOnlyList<PublishItemKind>? onlyKinds = null
  )
  {
    var kinds = onlyKinds is { Count: > 0 }
      ? PublishOrder.Fixed.Where(onlyKinds.Contains).ToArray()
      : PublishOrder.Fixed;

    var items = new List<PublishItemResult>(kinds.Count);
    foreach (var kind in kinds)
    {
      var result = await PublishItemAsync(kind, targetGroupIds, renderHost);
      items.Add(result);

      // 失败即停：后续项不再发送，界面按"失败项"给出重试入口
      if (!result.IsSuccess)
      {
        _log.Warn($"PublishService: 发布项「{result.Title}」失败，停止后续项（失败即停）。");
        break;
      }
    }

    return new PublishReport { Items = items };
  }

  /// <summary>
  /// 处理 Koishi 回传的发布结果（入站事件 <c>info_publish_result</c>）。
  /// </summary>
  /// <param name="payload">事件 payload。</param>
  /// <remarks>
  /// 按 <c>requestId</c> 精确配对：同一次发布可能对多 个群并发（不同项）或连续发送，
  /// 只按 kind 配对会在连续发布时串台。
  /// </remarks>
  public void HandlePublishResult(JsonElement payload)
  {
    var data = UnwrapData(payload);
    if (data is null)
      return;

    var requestId = ReadString(data.Value, "requestId");
    TaskCompletionSource<PublishAck>? pending;
    lock (_pendingLock)
    {
      if (requestId.Length == 0 || !_pending.TryGetValue(requestId, out pending))
      {
        _log.Warn("PublishService: 收到无法配对的发布回执（requestId 为空或已超时）。");
        return;
      }
    }

    var success =
      data.Value.TryGetProperty("success", out var successEl)
      && successEl.ValueKind == JsonValueKind.True;

    var failures = new List<PublishFailure>();
    if (
      data.Value.TryGetProperty("failedNodes", out var failedNodes)
      && failedNodes.ValueKind == JsonValueKind.Array
    )
    {
      foreach (var node in failedNodes.EnumerateArray())
      {
        if (node.ValueKind != JsonValueKind.Object)
          continue;

        var index =
          node.TryGetProperty("index", out var indexEl) && indexEl.TryGetInt32(out var parsedIndex)
            ? parsedIndex
            : 0;

        var reason = ReadString(node, "reason");
        failures.Add(
          new PublishFailure(
            index,
            ReadString(node, "title"),
            reason.Length == 0 ? "Koishi 未给出原因" : reason
          )
        );
      }
    }

    if (
      !pending.TrySetResult(
        success
          ? PublishAck.Ok()
          : PublishAck.Failed(
            ReadString(data.Value, "message") is { Length: > 0 } message
              ? message
              : "Koishi 回报发送失败。",
            failures
          )
      )
    )
    {
      _log.Warn("PublishService: 发布回执重复投递，已忽略。");
    }
  }

  private async Task<PublishItemResult> PublishItemAsync(
    PublishItemKind kind,
    IReadOnlyList<string> targetGroupIds,
    Node? renderHost
  )
  {
    var targets = targetGroupIds ?? Array.Empty<string>();
    if (targets.Count == 0)
      return PublishItemResult.Blocked(kind, 0, "未选择目标群，未发送任何内容。");

    var prepared = kind switch
    {
      PublishItemKind.GifSet => PrepareGifSetNodes(),
      PublishItemKind.GuessingTable => await PrepareTableNodesAsync(kind, renderHost),
      PublishItemKind.CreatorRemainingTable => await PrepareTableNodesAsync(kind, renderHost),
      PublishItemKind.ActivityRule => PrepareActivityRuleNodes(),
      _ => PublishPreparation.Failed($"未支持的发布项：{kind}。"),
    };

    if (prepared.ErrorMessage.Length > 0)
    {
      _log.Warn(
        $"PublishService: 「{PublishOrder.GetTitle(kind)}」预检未通过：{prepared.ErrorMessage}"
      );
      return PublishItemResult.Blocked(kind, targets.Count, prepared.ErrorMessage);
    }

    var failures = new List<PublishFailure>();
    var succeeded = 0;

    foreach (var channelId in targets)
    {
      var ack = await SendForwardAsync(kind, channelId, prepared.Nodes);
      if (ack.IsSuccess)
      {
        succeeded++;
        _log.Print(
          $"PublishService: 「{PublishOrder.GetTitle(kind)}」→ 群 {channelId} 发送成功（{prepared.Nodes.Count} 条）。"
        );
        continue;
      }

      failures.Add(new PublishFailure(0, $"目标群 {channelId}", ack.ErrorMessage));

      // 逐条失败明细必须带上序号，否则「第几张图失败」无法从报告里看出来
      failures.AddRange(ack.FailedNodes);

      // 失败即停：剩余目标群不再发送，由 TargetSucceeded/TargetTotal 体现进度
      break;
    }

    return PublishItemResult.Attempted(kind, targets.Count, succeeded, failures);
  }

  private async Task<PublishAck> SendForwardAsync(
    PublishItemKind kind,
    string channelId,
    IReadOnlyList<PublishNode> nodes
  )
  {
    if (_server.ConnectionCount == 0)
      return PublishAck.Failed("当前没有可用的 Koishi 连接，请先在「WebSocket」面板确认连接状态。");

    var requestId = Guid.NewGuid().ToString();
    var pending = new TaskCompletionSource<PublishAck>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    lock (_pendingLock)
    {
      _pending[requestId] = pending;
    }

    try
    {
      var payload = new
      {
        requestId,
        channelId,
        kind = InfoProtocol.ToWireValue(kind),
        nodes = nodes
          .Select(node => new
          {
            index = node.Index,
            title = node.Title,
            text = node.Title,
            imagePath = node.ImageFilePath,
            imageUrl = ToFileUrl(node.ImageFilePath),
          })
          .ToArray(),
      };

      try
      {
        await _server.BroadcastAsync(
          WebSocketMessage.CreateEvent(InfoProtocol.PublishForwardEvent, payload)
        );
      }
      catch (Exception ex)
      {
        // 发送阶段异常（如连接在发送途中被断开）必须落成一项可见失败：
        // 否则异常会穿过 async void 事件处理器直达未处理异常，报告里什么都不显示
        _log.Warn(
          $"PublishService: 发送失败（kind={kind}, channelId={channelId}, requestId={requestId}）：{ex.Message}"
        );
        return PublishAck.Failed($"发送失败：{ex.Message}");
      }

      var completed = await Task.WhenAny(pending.Task, Task.Delay(AckTimeoutMs));
      if (completed != pending.Task)
      {
        _log.Warn(
          $"PublishService: 等待回执超时（kind={kind}, channelId={channelId}, requestId={requestId}）。"
        );
        return PublishAck.Failed($"等待 Koishi 回执超时（{AckTimeoutMs / 1000} 秒）。");
      }

      return await pending.Task;
    }
    finally
    {
      lock (_pendingLock)
      {
        _pending.Remove(requestId);
      }
    }
  }

  private PublishPreparation PrepareGifSetNodes()
  {
    var set = _gifSets.GetActiveSet();
    if (set is null)
      return PublishPreparation.Failed("尚未导入任何符卡 GIF 集，请先导入文件夹或压缩包。");

    var entries = set.Manifest?.Entries;
    if (entries is null || entries.Count == 0)
      return PublishPreparation.Failed($"当前集（{set.SetName.Value}）没有任何符卡条目。");

    var nodes = new List<PublishNode>(entries.Count);
    var missing = new List<string>();

    foreach (var entry in entries)
    {
      if (entry is null)
        continue;

      var title = $"{entry.Index}. {entry.SpellCardName}";
      var path = _gifSets.GetEntryPath(set, entry);

      // 预检：导入后文件被移动/删除/清空属异常情形，此处在发送前拦下并指出具体条目
      if (!IsUsableFile(path))
      {
        missing.Add($"{title}（{entry.FileName}）");
        continue;
      }

      nodes.Add(new PublishNode(entry.Index, title, path));
    }

    if (missing.Count > 0)
    {
      var preview = string.Join("、", missing.Take(5));
      var suffix = missing.Count > 5 ? $" 等共 {missing.Count} 条" : string.Empty;
      return PublishPreparation.Failed($"预检失败：{preview}{suffix} 的 GIF 缺失或为空，未发送。");
    }

    return PublishPreparation.Ok(nodes);
  }

  private async Task<PublishPreparation> PrepareTableNodesAsync(
    PublishItemKind kind,
    Node? renderHost
  )
  {
    var snapshot = _dataService.BuildSnapshot();
    if (!snapshot.HasBoss)
      return PublishPreparation.Failed("当前没有可用的 Boss 数据，无法出图。");

    var model =
      kind == PublishItemKind.GuessingTable
        ? TableModelBuilder.BuildGuessingTable(snapshot)
        : TableModelBuilder.BuildCreatorRemainingTable(snapshot);

    if (!model.HasContent)
      return PublishPreparation.Failed($"「{model.Title}」当前没有可发布的数据行。");

    if (
      renderHost is null
      || !GodotObject.IsInstanceValid(renderHost)
      || !renderHost.IsInsideTree()
    )
      return PublishPreparation.Failed("缺少可用的出图宿主节点，未发送。");

    var result = await _tableImages.RenderAsync(model, renderHost);
    if (!result.IsSuccess)
      return PublishPreparation.Failed($"出图失败：{result.ErrorMessage}");

    if (!IsUsableFile(result.Path))
      return PublishPreparation.Failed($"出图产物为空：{result.Path}");

    return PublishPreparation.Ok(new[] { new PublishNode(0, model.Title, result.Path) });
  }

  private PublishPreparation PrepareActivityRuleNodes()
  {
    var text = _dataManager.InfoConfig.ActivityRule.Value ?? string.Empty;
    if (string.IsNullOrWhiteSpace(text))
      return PublishPreparation.Failed("活动规则为空，未发送。");

    return PublishPreparation.Ok(new[] { new PublishNode(0, text.Trim(), null) });
  }

  private static bool IsUsableFile(string? path) =>
    !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;

  private static string? ToFileUrl(string? path) =>
    string.IsNullOrWhiteSpace(path) ? null : new Uri(Path.GetFullPath(path)).AbsoluteUri;

  private static JsonElement? UnwrapData(JsonElement payload)
  {
    if (payload.ValueKind != JsonValueKind.Object)
      return null;

    if (!payload.TryGetProperty("data", out var data))
      return payload;

    return data.ValueKind == JsonValueKind.Object ? data : null;
  }

  private static string ReadString(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
      ? value.GetString() ?? string.Empty
      : string.Empty;

  /// <summary>预检结果：要么给出可发送节点，要么给出未发送原因（二者不会同时发生）。</summary>
  /// <param name="Nodes">可发送节点。</param>
  /// <param name="ErrorMessage">未发送原因；为空表示预检通过。</param>
  private sealed record PublishPreparation(IReadOnlyList<PublishNode> Nodes, string ErrorMessage)
  {
    public static PublishPreparation Ok(IReadOnlyList<PublishNode> nodes) =>
      new(nodes, string.Empty);

    public static PublishPreparation Failed(string errorMessage) =>
      new(Array.Empty<PublishNode>(), errorMessage);
  }
}

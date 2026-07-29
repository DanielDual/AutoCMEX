namespace AutoCMEX.UI.Settings;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCMEX;
using AutoCMEX.Core.Ai;
using AutoCMEX.Core.Logging;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using AutoCMEX.Services;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Chickensoft.Log;
using Godot;

/// <summary>
/// 设置板块脚本
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class SettingsPanel : Control
{
  #region AutoConnect Nodes

  [Node]
  public LineEdit SearchBar { get; set; } = default!;

  [Node]
  public ItemList CategoryList { get; set; } = default!;

  [Node]
  public Control ConfigArea { get; set; } = default!;

  #endregion

  #region Dependencies

  [Dependency]
  public DataManager DataManager => this.DependOn<DataManager>(() => null!);

  #endregion

  // 数据
  private AppSettings _settings = new();

  private readonly string[] _categories =
  {
    "AI模型",
    "群聊",
    "猜测",
    "整合",
    "信息",
    "帮助",
    "通用",
  };
  private int _currentCategory = -1;

  public override void _Notification(int what) => this.Notify(what);

  public void OnReady()
  {
    // 初始化类别列表
    foreach (var cat in _categories)
    {
      CategoryList.AddItem(cat);
    }

    CategoryList.ItemSelected += OnCategorySelected;
    SearchBar.TextChanged += OnSearchChanged;
  }

  public void OnResolved()
  {
    // 依赖已解析，同步设置数据。依赖提供者可能尚未注册（如测试环境），
    // 此时 DependOn 会抛出 NullReferenceException，需忽略。
    try
    {
      var dm = DataManager;
      if (dm != null)
      {
        _settings = dm.Settings;
      }
    }
    catch (NullReferenceException)
    {
      // Provider not available — leave default settings.
    }
  }

  /// <summary>
  /// 类别选择变更
  /// </summary>
  private void OnCategorySelected(long index)
  {
    _currentCategory = (int)index;
    RefreshConfigArea();
  }

  /// <summary>
  /// 搜索文本变更
  /// </summary>
  private void OnSearchChanged(string newText)
  {
    // 过滤类别列表
    for (int i = 0; i < _categories.Length; i++)
    {
      var visible =
        string.IsNullOrEmpty(newText)
        || _categories[i].Contains(newText, System.StringComparison.OrdinalIgnoreCase);
      CategoryList.SetItemDisabled(i, !visible);
    }
  }

  /// <summary>
  /// 刷新配置区域
  /// </summary>
  private void RefreshConfigArea()
  {
    // 清除旧内容
    foreach (var child in ConfigArea.GetChildren())
    {
      child.QueueFree();
    }

    switch (_currentCategory)
    {
      case 0:
        BuildAiModelConfig();
        break;
      case 1:
        BuildChatConfig();
        break;
      default:
        BuildPlaceholder(_categories[_currentCategory]);
        break;
    }
  }

  /// <summary>
  /// 构建 AI 模型配置 UI
  /// </summary>
  private void BuildAiModelConfig()
  {
    var container = new VBoxContainer();
    container.SetAnchorsPreset(LayoutPreset.FullRect);
    ConfigArea.AddChild(container);

    // 当前使用模型选择
    var activeRow = new HBoxContainer();
    var activeLabel = new Label();
    activeLabel.Text = "当前使用:";
    activeLabel.CustomMinimumSize = new Vector2(100, 0);
    activeRow.AddChild(activeLabel);

    var modelSelect = new OptionButton();
    modelSelect.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
    modelSelect.AddItem("(未选择)");
    modelSelect.SetItemDisabled(0, true);

    int selectedIdx = 0;
    for (int i = 0; i < _settings.AiModels.Count; i++)
    {
      var model = _settings.AiModels[i];
      var label = $"{model.ModelId} ({model.ApiFormat})";
      if (string.IsNullOrEmpty(model.ModelId))
        label = $"(未命名) ({model.ApiFormat})";
      modelSelect.AddItem(label);
      var itemIdx = i + 1;

      if (!AiServiceFactory.IsModelValid(model))
        modelSelect.SetItemDisabled(itemIdx, true);

      if (model.Id == _settings.ActiveAiModelId)
        selectedIdx = itemIdx;
    }
    modelSelect.Select(selectedIdx);

    modelSelect.ItemSelected += (idx) =>
    {
      if (idx == 0)
      {
        _settings.ActiveAiModelId = null;
      }
      else
      {
        var modelIdx = (int)idx - 1;
        if (modelIdx >= 0 && modelIdx < _settings.AiModels.Count)
          _settings.ActiveAiModelId = _settings.AiModels[modelIdx].Id;
      }
      DataManager?.TriggerAutoSave();
    };
    activeRow.AddChild(modelSelect);
    container.AddChild(activeRow);

    // 超时时间
    var timeoutRow = new HBoxContainer();
    var timeoutLabel = new Label();
    timeoutLabel.Text = "超时(秒):";
    timeoutLabel.CustomMinimumSize = new Vector2(100, 0);
    timeoutRow.AddChild(timeoutLabel);

    var timeoutInput = new SpinBox();
    timeoutInput.MinValue = 1;
    timeoutInput.MaxValue = 600;
    timeoutInput.Value = _settings.AiTimeoutSeconds;
    timeoutInput.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
    timeoutInput.ValueChanged += (value) =>
    {
      _settings.AiTimeoutSeconds = (int)value;
      DataManager?.TriggerAutoSave();
    };
    timeoutRow.AddChild(timeoutInput);
    container.AddChild(timeoutRow);
    container.AddChild(new HSeparator());

    // 模型列表
    var scrollContainer = new ScrollContainer();
    scrollContainer.SizeFlagsVertical = SizeFlags.Expand | SizeFlags.Fill;
    container.AddChild(scrollContainer);

    var modelList = new VBoxContainer();
    scrollContainer.AddChild(modelList);

    foreach (var model in _settings.AiModels)
    {
      var modelEntry = CreateModelEntry(model);
      modelList.AddChild(modelEntry);
    }

    // 添加按钮
    var addBtn = new Button();
    addBtn.Text = "添加模型";
    addBtn.Pressed += () =>
    {
      var _log = AppLogs.GetOrCreate().GetLogger(nameof(SettingsPanel));
      _log.Print("SettingsPanel: user added new AI model.");
      var newModel = new AiModelConfig
      {
        Id = System.Guid.NewGuid().ToString("N")[..8],
        ApiFormat = "OpenAI",
      };
      _settings.AiModels.Add(newModel);
      DataManager?.TriggerAutoSave();
      RefreshConfigArea();
    };
    container.AddChild(addBtn);
  }

  /// <summary>
  /// 创建单个模型配置条目
  /// </summary>
  private VBoxContainer CreateModelEntry(AiModelConfig model)
  {
    var entry = new VBoxContainer();

    // API 格式
    var formatRow = new HBoxContainer();
    var formatLabel = new Label();
    formatLabel.Text = "API 格式:";
    formatLabel.CustomMinimumSize = new Vector2(100, 0);
    formatRow.AddChild(formatLabel);

    var formatOption = new OptionButton();
    formatOption.AddItem("OpenAI");
    formatOption.AddItem("Anthropic");
    formatOption.Select(model.ApiFormat == "Anthropic" ? 1 : 0);
    formatOption.ItemSelected += (idx) =>
    {
      model.ApiFormat = idx == 1 ? "Anthropic" : "OpenAI";
      DataManager?.TriggerAutoSave();
    };
    formatRow.AddChild(formatOption);
    entry.AddChild(formatRow);

    // 请求地址
    var urlRow = new HBoxContainer();
    var urlLabel = new Label();
    urlLabel.Text = "请求地址:";
    urlLabel.CustomMinimumSize = new Vector2(100, 0);
    urlRow.AddChild(urlLabel);

    var urlInput = new LineEdit();
    urlInput.Text = model.EndpointUrl;
    urlInput.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
    urlInput.TextChanged += (text) =>
    {
      model.EndpointUrl = text;
      DataManager?.TriggerAutoSave();
    };
    urlRow.AddChild(urlInput);
    entry.AddChild(urlRow);

    // 模型 ID
    var modelIdRow = new HBoxContainer();
    var modelIdLabel = new Label();
    modelIdLabel.Text = "模型 ID:";
    modelIdLabel.CustomMinimumSize = new Vector2(100, 0);
    modelIdRow.AddChild(modelIdLabel);

    var modelIdInput = new LineEdit();
    modelIdInput.Text = model.ModelId;
    modelIdInput.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
    modelIdInput.TextChanged += (text) =>
    {
      model.ModelId = text;
      DataManager?.TriggerAutoSave();
    };
    modelIdRow.AddChild(modelIdInput);
    entry.AddChild(modelIdRow);

    // API 密钥
    var keyRow = new HBoxContainer();
    var keyLabel = new Label();
    keyLabel.Text = "API 密钥:";
    keyLabel.CustomMinimumSize = new Vector2(100, 0);
    keyRow.AddChild(keyLabel);

    var keyInput = new LineEdit();
    keyInput.Secret = true;
    keyInput.PlaceholderText = "****";
    keyInput.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
    keyInput.TextChanged += (text) =>
    {
      model.EncryptedApiKey = text;
      DataManager?.TriggerAutoSave();
    };
    keyRow.AddChild(keyInput);

    var toggleBtn = new Button();
    toggleBtn.Text = "显示";
    toggleBtn.Toggled += (on) =>
    {
      keyInput.Secret = !on;
      toggleBtn.Text = on ? "隐藏" : "显示";
    };
    keyRow.AddChild(toggleBtn);
    entry.AddChild(keyRow);

    // 操作按钮
    var actionRow = new HBoxContainer();

    var testBtn = new Button();
    testBtn.Text = "测试连接";
    testBtn.Pressed += async () =>
    {
      var _log = AppLogs.GetOrCreate().GetLogger(nameof(SettingsPanel));
      _log.Print(
        $"SettingsPanel: user requested connection test for model {model.Id} ({model.ModelId})."
      );
      testBtn.Disabled = true;
      testBtn.Text = "测试中...";

      var success = await TestModelConnection(model);
      _log.Print($"SettingsPanel: connection test for model {model.Id} result={success}");

      testBtn.Disabled = false;
      testBtn.Text = success ? "连接成功" : "连接失败";

      // 2 秒后恢复
      await ToSignal(GetTree().CreateTimer(2), "timeout");
      testBtn.Text = "测试连接";
    };
    actionRow.AddChild(testBtn);

    var deleteBtn = new Button();
    deleteBtn.Text = "删除";
    deleteBtn.Pressed += () =>
    {
      _settings.AiModels.Remove(model);
      DataManager?.TriggerAutoSave();
      RefreshConfigArea();
    };
    actionRow.AddChild(deleteBtn);
    entry.AddChild(actionRow);

    entry.AddChild(new HSeparator());

    return entry;
  }

  /// <summary>
  /// 测试模型连接
  /// </summary>
  private static async System.Threading.Tasks.Task<bool> TestModelConnection(AiModelConfig model)
  {
    try
    {
      var service = AiServiceFactory.CreateService(model);

      return await service.TestConnectionAsync();
    }
    catch
    {
      return false;
    }
  }

  /// <summary>
  /// 构建群聊配置 UI
  /// </summary>
  private void BuildChatConfig()
  {
    var container = new VBoxContainer();
    container.SetAnchorsPreset(LayoutPreset.FullRect);
    ConfigArea.AddChild(container);

    // WebSocket 端口
    var portRow = new HBoxContainer();
    var portLabel = new Label();
    portLabel.Text = "WebSocket 端口:";
    portLabel.CustomMinimumSize = new Vector2(120, 0);
    portRow.AddChild(portLabel);

    var portInput = new SpinBox();
    portInput.MinValue = 1;
    portInput.MaxValue = 65535;
    portInput.Value = _settings.WebSocketPort;
    portInput.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
    portInput.ValueChanged += (value) =>
    {
      _settings.WebSocketPort = (int)value;
      DataManager?.TriggerAutoSave();
    };
    portRow.AddChild(portInput);
    container.AddChild(portRow);

    // WebSocket 运行模式
    var modeRow = new HBoxContainer();
    var modeLabel = new Label();
    modeLabel.Text = "连接模式:";
    modeLabel.CustomMinimumSize = new Vector2(120, 0);
    modeRow.AddChild(modeLabel);

    var modeOption = new OptionButton();
    modeOption.AddItem("Server（等待连接）");
    modeOption.AddItem("Client（主动连接）");
    modeOption.Select(
      string.Equals(_settings.WebSocketMode, "Client", StringComparison.OrdinalIgnoreCase) ? 1 : 0
    );
    modeOption.ItemSelected += (idx) =>
    {
      _settings.WebSocketMode = idx == 1 ? "Client" : "Server";
      DataManager?.TriggerAutoSave();
      // 刷新 UI 以显示/隐藏 URL 输入
      RefreshConfigArea();
      // 通知 MainWindow 重启 WebSocket
    };
    modeRow.AddChild(modeOption);
    container.AddChild(modeRow);

    // Koishi WebSocket URL（仅 Client 模式显示）
    if (string.Equals(_settings.WebSocketMode, "Client", StringComparison.OrdinalIgnoreCase))
    {
      var urlRow = new HBoxContainer();
      var urlLabel = new Label();
      urlLabel.Text = "Koishi 地址:";
      urlLabel.CustomMinimumSize = new Vector2(120, 0);
      urlRow.AddChild(urlLabel);

      var urlInput = new LineEdit();
      urlInput.PlaceholderText = "ws://localhost:5140";
      urlInput.Text = _settings.KoishiWebSocketUrl;
      urlInput.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill;
      urlInput.TextChanged += (text) =>
      {
        _settings.KoishiWebSocketUrl = text;
        DataManager?.TriggerAutoSave();
      };
      urlRow.AddChild(urlInput);
      container.AddChild(urlRow);
    }

    // 消息筛选模式
    var filterRow = new HBoxContainer();
    var filterLabel = new Label();
    filterLabel.Text = "消息筛选:";
    filterLabel.CustomMinimumSize = new Vector2(120, 0);
    filterRow.AddChild(filterLabel);

    var filterOption = new OptionButton();
    filterOption.AddItem("仅严格格式匹配");
    filterOption.AddItem("仅 AI 智能匹配");
    filterOption.AddItem("先严格再 AI");
    filterOption.Select(
      _settings.MessageFilterMode switch
      {
        "ai" => 1,
        "strict_then_ai" => 2,
        _ => 0,
      }
    );
    filterOption.ItemSelected += (idx) =>
    {
      _settings.MessageFilterMode = idx switch
      {
        1 => "ai",
        2 => "strict_then_ai",
        _ => "strict",
      };
      DataManager?.TriggerAutoSave();
    };
    filterRow.AddChild(filterOption);
    container.AddChild(filterRow);

    // 一键安装 Koishi 插件
    var installRow = new HBoxContainer();
    var installBtn = new Button();
    installBtn.Text = "一键安装 Koishi 插件";
    installBtn.Pressed += () =>
    {
      var dialog = new FileDialog();
      dialog.FileMode = FileDialog.FileModeEnum.OpenDir;
      dialog.Access = FileDialog.AccessEnum.Filesystem;
      dialog.Title = "选择 Koishi plugins 目录";
      dialog.DirSelected += (dir) =>
      {
        // 复制插件文件夹
        var sourceDir = "res://src/plugin/koishi/";
        var destDir = System.IO.Path.Combine(dir, "auto-cmex");

        // 使用 DirAccess 复制
        PluginInstaller.CopyPluginDir(sourceDir, destDir);

        _settings.KoishiPluginPath = destDir;
        DataManager?.TriggerAutoSave();

        var okDialog = new AcceptDialog();
        okDialog.Title = "安装完成";
        okDialog.DialogText = $"插件已安装到 {destDir}";
        AddChild(okDialog);
        okDialog.PopupCentered();
      };
      AddChild(dialog);
      dialog.PopupCentered();
    };
    installRow.AddChild(installBtn);
    container.AddChild(installRow);
  }

  /// <summary>
  /// 构建占位配置
  /// </summary>
  private void BuildPlaceholder(string category)
  {
    var label = new Label();
    label.Text = $"{category} 配置暂未实现";
    label.HorizontalAlignment = HorizontalAlignment.Center;
    label.VerticalAlignment = VerticalAlignment.Center;
    label.SetAnchorsPreset(LayoutPreset.FullRect);
    ConfigArea.AddChild(label);
  }
}

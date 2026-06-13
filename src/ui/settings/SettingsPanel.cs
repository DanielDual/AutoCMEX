namespace AutoCMEX.UI.Settings;

using System.Collections.Generic;
using System.Linq;
using AutoCMEX.Core.Storage;
using AutoCMEX.Models;
using Godot;

/// <summary>
/// 设置板块脚本
/// </summary>
public partial class SettingsPanel : Control
{
  // 搜索栏
  private LineEdit _searchBar = default!;

  // 左栏：配置类别列表
  private ItemList _categoryList = default!;

  // 右栏：配置项区域
  private Control _configArea = default!;

  // 数据
  private DataManager? _dataManager;
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

  public override void _Ready()
  {
    _searchBar = GetNode<LineEdit>("%SearchBar");
    _categoryList = GetNode<ItemList>("%CategoryList");
    _configArea = GetNode<Control>("%ConfigArea");

    // 初始化类别列表
    foreach (var cat in _categories)
    {
      _categoryList.AddItem(cat);
    }

    _categoryList.ItemSelected += OnCategorySelected;
    _searchBar.TextChanged += OnSearchChanged;
  }

  /// <summary>
  /// 设置数据管理器引用
  /// </summary>
  public void SetDataManager(DataManager dataManager)
  {
    _dataManager = dataManager;
    _settings = dataManager.Settings;
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
      _categoryList.SetItemDisabled(i, !visible);
    }
  }

  /// <summary>
  /// 刷新配置区域
  /// </summary>
  private void RefreshConfigArea()
  {
    // 清除旧内容
    foreach (var child in _configArea.GetChildren())
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
    _configArea.AddChild(container);

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
      var newModel = new AiModelConfig
      {
        Id = System.Guid.NewGuid().ToString("N")[..8],
        ApiFormat = "OpenAI",
      };
      _settings.AiModels.Add(newModel);
      _dataManager?.TriggerAutoSave();
      RefreshConfigArea();
    };
    container.AddChild(addBtn);
  }

  /// <summary>
  /// 创建单个模型配置条目
  /// </summary>
  private Control CreateModelEntry(AiModelConfig model)
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
      _dataManager?.TriggerAutoSave();
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
      _dataManager?.TriggerAutoSave();
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
      _dataManager?.TriggerAutoSave();
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
      _dataManager?.TriggerAutoSave();
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
      testBtn.Disabled = true;
      testBtn.Text = "测试中...";

      var success = await TestModelConnection(model);

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
      _dataManager?.TriggerAutoSave();
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
  private async System.Threading.Tasks.Task<bool> TestModelConnection(AiModelConfig model)
  {
    try
    {
      Core.Ai.IAiService service =
        model.ApiFormat == "Anthropic"
          ? new Core.Ai.AnthropicService(model)
          : new Core.Ai.OpenAiService(model);

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
    _configArea.AddChild(container);

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
      _dataManager?.TriggerAutoSave();
    };
    portRow.AddChild(portInput);
    container.AddChild(portRow);

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
      _dataManager?.TriggerAutoSave();
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
        CopyPluginDir(sourceDir, destDir);

        _settings.KoishiPluginPath = destDir;
        _dataManager?.TriggerAutoSave();

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
  /// 复制插件目录
  /// </summary>
  private void CopyPluginDir(string sourceDir, string destDir)
  {
    var dir = DirAccess.Open(sourceDir);
    if (dir == null)
      return;

    DirAccess.MakeDirAbsolute(destDir);

    dir.ListDirBegin();
    var fileName = dir.GetNext();
    while (!string.IsNullOrEmpty(fileName))
    {
      if (fileName != "." && fileName != "..")
      {
        var srcPath = System.IO.Path.Combine(sourceDir, fileName);
        var dstPath = System.IO.Path.Combine(destDir, fileName);

        if (dir.CurrentIsDir())
          CopyPluginDir(srcPath, dstPath);
        else
          DirAccess.CopyAbsolute(srcPath, dstPath);
      }
      fileName = dir.GetNext();
    }
    dir.ListDirEnd();
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
    _configArea.AddChild(label);
  }
}

namespace AutoCMEX;

using System;
using AutoCMEX.UI.Guessing;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
using Godot;

/// <summary>
/// Test driver for <see cref="AliasTreeHandler"/> — provides a high-level
/// interface for interacting with the alias tree area in tests.
/// </summary>
public class AliasTreeHandlerDriver : ControlDriver<AliasTreeHandler>
{
  public ButtonDriver ImportAliasBtn { get; }
  public ButtonDriver ExportAliasBtn { get; }
  public ButtonDriver AddAliasBtn { get; }
  public ButtonDriver AddAliasToCreatorBtn { get; }
  public ButtonDriver DeleteAliasBtn { get; }

  public AliasTreeHandlerDriver(Func<AliasTreeHandler> producer)
    : base(producer)
  {
    ImportAliasBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("AliasButtons/ImportAliasBtn")!
    );
    ExportAliasBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("AliasButtons/ExportAliasBtn")!
    );
    AddAliasBtn = new ButtonDriver(() => Root?.GetNodeOrNull<Button>("AliasButtons/AddAliasBtn")!);
    AddAliasToCreatorBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("AliasButtons/AddAliasToCreatorBtn")!
    );
    DeleteAliasBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("AliasButtons/DeleteAliasBtn")!
    );
  }

  /// <summary>
  /// Gets the underlying Tree node.
  /// </summary>
  public Tree? Tree => Root?.GetNodeOrNull<Tree>("AliasTree");

  /// <summary>
  /// Adds a new creator via the AddAlias button.
  /// </summary>
  public void AddCreator() => AddAliasBtn.ClickCenter();

  /// <summary>
  /// Adds a new alias to the selected creator.
  /// </summary>
  public void AddAliasToCreator() => AddAliasToCreatorBtn.ClickCenter();

  /// <summary>
  /// Deletes the currently selected item in the tree.
  /// </summary>
  public void DeleteSelected() => DeleteAliasBtn.ClickCenter();

  /// <summary>
  /// Gets the number of root children in the tree.
  /// </summary>
  public int TreeRootChildCount
  {
    get
    {
      var root = Tree?.GetRoot();
      return root?.GetChildCount() ?? 0;
    }
  }

  /// <summary>
  /// Gets whether the import button is disabled.
  /// </summary>
  public bool ImportBtnDisabled => ImportAliasBtn.Disabled;

  /// <summary>
  /// Gets whether the export button is disabled.
  /// </summary>
  public bool ExportBtnDisabled => ExportAliasBtn.Disabled;
}

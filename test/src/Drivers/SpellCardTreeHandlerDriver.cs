namespace AutoCMEX;

using System;
using AutoCMEX.UI.Guessing;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
using Godot;

/// <summary>
/// Test driver for <see cref="SpellCardTreeHandler"/> — provides a high-level
/// interface for interacting with the spell card tree area in tests.
/// </summary>
public class SpellCardTreeHandlerDriver : ControlDriver<SpellCardTreeHandler>
{
  public OptionButtonDriver BossSelect { get; }
  public ButtonDriver ImportCardBtn { get; }
  public ButtonDriver ExportCardBtn { get; }
  public ButtonDriver AddBossBtn { get; }
  public ButtonDriver AddCardBtn { get; }
  public ButtonDriver DeleteBtn { get; }

  public SpellCardTreeHandlerDriver(Func<SpellCardTreeHandler> producer)
    : base(producer)
  {
    BossSelect = new OptionButtonDriver(() =>
      Root?.GetNodeOrNull<OptionButton>("../../../BossSelect")
    );
    ImportCardBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("SpellCardButtons/ImportCardBtn")
    );
    ExportCardBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("SpellCardButtons/ExportCardBtn")
    );
    AddBossBtn = new ButtonDriver(() => Root?.GetNodeOrNull<Button>("SpellCardButtons/AddBossBtn"));
    AddCardBtn = new ButtonDriver(() => Root?.GetNodeOrNull<Button>("SpellCardButtons/AddCardBtn"));
    DeleteBtn = new ButtonDriver(() => Root?.GetNodeOrNull<Button>("SpellCardButtons/DeleteBtn"));
  }

  /// <summary>
  /// Gets the underlying Tree node.
  /// </summary>
  public Tree? Tree => Root?.GetNodeOrNull<Tree>("SpellCardTree");

  /// <summary>
  /// Adds a new boss via the AddBoss button.
  /// </summary>
  public void AddBoss() => AddBossBtn.ClickCenter();

  /// <summary>
  /// Adds a new spell card to the currently selected boss.
  /// </summary>
  public void AddSpellCard() => AddCardBtn.ClickCenter();

  /// <summary>
  /// Deletes the currently selected item in the tree.
  /// </summary>
  public void DeleteSelected() => DeleteBtn.ClickCenter();
}

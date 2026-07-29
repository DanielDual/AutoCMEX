namespace AutoCMEX;

using System;
using AutoCMEX.UI.Guessing;
using Chickensoft.GodotTestDriver;
using Chickensoft.GodotTestDriver.Drivers;
using Godot;

/// <summary>
/// Test driver for <see cref="GuessingPanel"/> — provides a high-level
/// interface for interacting with the guessing panel UI in tests.
/// Composes sub-drivers for the spell card area and alias area.
/// </summary>
public class GuessingPanelDriver : ControlDriver<GuessingPanel>
{
  public TextEditDriver GuessInput { get; }
  public ButtonDriver FuzzifyBtn { get; }
  public ButtonDriver ProcessBtn { get; }
  public RichTextLabelDriver ResponseDisplay { get; }
  public ItemListDriver DroppedList { get; }
  public ButtonDriver RetryDroppedBtn { get; }
  public ButtonDriver ClearDroppedBtn { get; }

  public GuessingPanelDriver(Func<GuessingPanel> producer)
    : base(producer)
  {
    GuessInput = new TextEditDriver(() =>
      Root?.GetNodeOrNull<TextEdit>("MainContainer/ContentArea/RightPanel/GuessInput")!
    );
    FuzzifyBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("MainContainer/ContentArea/RightPanel/GuessButtons/FuzzifyBtn")!
    );
    ProcessBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>("MainContainer/ContentArea/RightPanel/GuessButtons/ProcessBtn")!
    );
    ResponseDisplay = new RichTextLabelDriver(() =>
      Root?.GetNodeOrNull<RichTextLabel>("MainContainer/ContentArea/RightPanel/ResponseDisplay")!
    );
    DroppedList = new ItemListDriver(() =>
      Root?.GetNodeOrNull<ItemList>(
        "MainContainer/ContentArea/RightPanel/DroppedSection/DroppedList"
      )!
    );
    RetryDroppedBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>(
        "MainContainer/ContentArea/RightPanel/DroppedSection/DroppedButtons/RetryDroppedBtn"
      )!
    );
    ClearDroppedBtn = new ButtonDriver(() =>
      Root?.GetNodeOrNull<Button>(
        "MainContainer/ContentArea/RightPanel/DroppedSection/DroppedButtons/ClearDroppedBtn"
      )!
    );
  }

  /// <summary>
  /// Gets the spell card tree handler driver.
  /// </summary>
  public SpellCardTreeHandlerDriver SpellCardHandler =>
    new(() =>
      Root?.GetNodeOrNull<SpellCardTreeHandler>(
        "MainContainer/ContentArea/LeftSplit/SpellCardArea"
      )!
    );

  /// <summary>
  /// Gets the alias tree handler driver.
  /// </summary>
  public AliasTreeHandlerDriver AliasHandler =>
    new(() =>
      Root?.GetNodeOrNull<AliasTreeHandler>("MainContainer/ContentArea/LeftSplit/AliasArea")!
    );

  /// <summary>
  /// Sets the guess input text and clicks the process button.
  /// </summary>
  public void SubmitGuess(string text)
  {
    GuessInput.Type(text);
    ProcessBtn.ClickCenter();
  }

  /// <summary>
  /// Clicks the fuzzify button to trigger AI fuzzification.
  /// </summary>
  public void Fuzzify() => FuzzifyBtn.ClickCenter();

  /// <summary>
  /// Retries all dropped guesses.
  /// </summary>
  public void RetryAllDropped() => RetryDroppedBtn.ClickCenter();

  /// <summary>
  /// Clears all dropped guesses.
  /// </summary>
  public void ClearAllDropped() => ClearDroppedBtn.ClickCenter();

  /// <summary>
  /// Gets the current response display text.
  /// </summary>
  public string ResponseText => ResponseDisplay.Text ?? string.Empty;

  /// <summary>
  /// Gets the number of items in the dropped list.
  /// </summary>
  public int DroppedCount
  {
    get
    {
      var list = Root?.GetNodeOrNull<ItemList>(
        "MainContainer/ContentArea/RightPanel/DroppedSection/DroppedList"
      );
      return list?.ItemCount ?? 0;
    }
  }

  /// <summary>
  /// Gets whether the fuzzify button is disabled.
  /// </summary>
  public bool FuzzifyBtnDisabled => FuzzifyBtn.Root?.Disabled ?? true;

  /// <summary>
  /// Gets the fuzzify button text.
  /// </summary>
  public string FuzzifyBtnText
  {
    get
    {
      var btn = Root?.GetNodeOrNull<Button>(
        "MainContainer/ContentArea/RightPanel/GuessButtons/FuzzifyBtn"
      );
      return btn?.Text ?? string.Empty;
    }
  }

  /// <summary>
  /// Gets the retry dropped button text.
  /// </summary>
  public string RetryDroppedBtnText
  {
    get
    {
      var btn = Root?.GetNodeOrNull<Button>(
        "MainContainer/ContentArea/RightPanel/DroppedSection/DroppedButtons/RetryDroppedBtn"
      );
      return btn?.Text ?? string.Empty;
    }
  }

  /// <summary>
  /// Gets whether the retry dropped button is disabled.
  /// </summary>
  public bool RetryDroppedBtnDisabled => RetryDroppedBtn.Root?.Disabled ?? true;

  /// <summary>
  /// Gets whether the process button is disabled.
  /// </summary>
  public bool ProcessBtnDisabled => ProcessBtn.Root?.Disabled ?? false;
}

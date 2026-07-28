namespace AutoCMEX;

using System;
using AutoCMEX.Core.Guessing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// Tests for <see cref="DroppedGuess"/> model and <see cref="DroppedGuessRepository"/>.
/// </summary>
public class DroppedGuessTest : TestClass
{
  public DroppedGuessTest(Node testScene)
    : base(testScene) { }

  // ==================== DroppedGuess Model Tests ====================

  [Test]
  public void DroppedGuess_CreatesWithGeneratedId()
  {
    var dropped = new DroppedGuess("test input", "error");
    dropped.Id.ShouldNotBeNullOrEmpty();
    dropped.Id.Length.ShouldBe(8);
  }

  [Test]
  public void DroppedGuess_StoresRawText()
  {
    var dropped = new DroppedGuess("my raw text", "error");
    dropped.RawText.ShouldBe("my raw text");
  }

  [Test]
  public void DroppedGuess_StoresLastError()
  {
    var dropped = new DroppedGuess("text", "timeout error");
    dropped.LastError.ShouldBe("timeout error");
  }

  [Test]
  public void DroppedGuess_TimestampIsRecent()
  {
    var before = DateTime.Now;
    var dropped = new DroppedGuess("text", "error");
    var after = DateTime.Now;

    dropped.Timestamp.ShouldBeGreaterThanOrEqualTo(before);
    dropped.Timestamp.ShouldBeLessThanOrEqualTo(after);
  }

  [Test]
  public void DroppedGuess_EachInstanceHasUniqueId()
  {
    var d1 = new DroppedGuess("text1", "error1");
    var d2 = new DroppedGuess("text2", "error2");
    d1.Id.ShouldNotBe(d2.Id);
  }

  // ==================== DroppedGuessRepository Tests ====================

  [Test]
  public void Repository_Add_IncreasesCount()
  {
    var repo = new DroppedGuessRepository();
    repo.Add(new DroppedGuess("text", "error"));
    repo.GetAll().Count.ShouldBe(1);
  }

  [Test]
  public void Repository_AddMultiple_ReturnsAll()
  {
    var repo = new DroppedGuessRepository();
    repo.Add(new DroppedGuess("text1", "error1"));
    repo.Add(new DroppedGuess("text2", "error2"));
    repo.GetAll().Count.ShouldBe(2);
  }

  [Test]
  public void Repository_FindById_ReturnsCorrectItem()
  {
    var repo = new DroppedGuessRepository();
    var dropped = new DroppedGuess("find me", "error");
    repo.Add(dropped);

    var found = repo.FindById(dropped.Id);
    found.ShouldNotBeNull();
    found.RawText.ShouldBe("find me");
  }

  [Test]
  public void Repository_FindById_NonExistent_ReturnsNull()
  {
    var repo = new DroppedGuessRepository();
    repo.FindById("nonexistent").ShouldBeNull();
  }

  [Test]
  public void Repository_Remove_DecreasesCount()
  {
    var repo = new DroppedGuessRepository();
    var dropped = new DroppedGuess("to remove", "error");
    repo.Add(dropped);

    repo.Remove(dropped.Id);
    repo.GetAll().Count.ShouldBe(0);
  }

  [Test]
  public void Repository_Remove_NonExistent_DoesNotThrow()
  {
    var repo = new DroppedGuessRepository();
    repo.Remove("nonexistent");
    repo.GetAll().Count.ShouldBe(0);
  }

  [Test]
  public void Repository_Clear_RemovesAll()
  {
    var repo = new DroppedGuessRepository();
    repo.Add(new DroppedGuess("text1", "error1"));
    repo.Add(new DroppedGuess("text2", "error2"));

    repo.Clear();
    repo.GetAll().Count.ShouldBe(0);
  }

  [Test]
  public void Repository_GetAll_ReturnsSnapshot()
  {
    var repo = new DroppedGuessRepository();
    repo.Add(new DroppedGuess("text", "error"));

    var snapshot = repo.GetAll();
    repo.Add(new DroppedGuess("text2", "error2"));

    // Snapshot should not be affected by subsequent adds
    snapshot.Count.ShouldBe(1);
    repo.GetAll().Count.ShouldBe(2);
  }
}

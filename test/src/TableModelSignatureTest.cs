namespace AutoCMEX;

using System;
using AutoCMEX.Core.Info;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

/// <summary>
/// <see cref="TableModelSignature"/> 单测：同内容同指纹、内容/高亮变化产生不同指纹。
/// </summary>
public class TableModelSignatureTest : TestClass
{
  public TableModelSignatureTest(Node testScene)
    : base(testScene) { }

  private static TableModel CreateModel(
    string title = "表",
    bool highlighted = false,
    string cell = "A"
  ) =>
    new()
    {
      Title = title,
      Columns = new[]
      {
        new TableColumn { Header = "列1", Align = TableColumnAlign.Left },
        new TableColumn { Header = "列2", Align = TableColumnAlign.Center },
      },
      Rows = new[]
      {
        new TableRow { Cells = new[] { cell, "1" }, IsHighlighted = highlighted },
      },
    };

  [Test]
  public void Build_SameContent_ProducesSameSignature()
  {
    TableModelSignature.Build(CreateModel()).ShouldBe(TableModelSignature.Build(CreateModel()));
  }

  [Test]
  public void Build_HighlightChange_ProducesDifferentSignature()
  {
    TableModelSignature
      .Build(CreateModel(highlighted: true))
      .ShouldNotBe(TableModelSignature.Build(CreateModel(highlighted: false)));
  }

  [Test]
  public void Build_CellTextChange_ProducesDifferentSignature()
  {
    TableModelSignature
      .Build(CreateModel(cell: "B"))
      .ShouldNotBe(TableModelSignature.Build(CreateModel(cell: "A")));
  }

  [Test]
  public void Build_TitleChange_ProducesDifferentSignature()
  {
    TableModelSignature
      .Build(CreateModel(title: "另一张表"))
      .ShouldNotBe(TableModelSignature.Build(CreateModel(title: "表")));
  }

  [Test]
  public void Build_RowCountChange_ProducesDifferentSignature()
  {
    var single = CreateModel();
    var twoRows = new TableModel
    {
      Title = single.Title,
      Columns = single.Columns,
      Rows = new[]
      {
        single.Rows[0],
        new TableRow { Cells = new[] { "B", "2" }, IsHighlighted = false },
      },
    };

    TableModelSignature.Build(twoRows).ShouldNotBe(TableModelSignature.Build(single));
  }

  [Test]
  public void Build_NullModel_ReturnsEmpty()
  {
    TableModelSignature.Build(null).ShouldBe(string.Empty);
  }
}

namespace AutoCMEX.Core.Storage;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chickensoft.Sync.Primitives;

/// <summary>
/// 自定义 JSON 转换器，用于序列化/反序列化 <see cref="AutoList{T}"/>。
/// </summary>
public class AutoListConverter<T> : JsonConverter<AutoList<T>>
{
  public override AutoList<T> Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options
  )
  {
    var list = JsonSerializer.Deserialize<List<T>>(ref reader, options);
    return new AutoList<T>(list ?? new List<T>());
  }

  public override void Write(
    Utf8JsonWriter writer,
    AutoList<T> value,
    JsonSerializerOptions options
  )
  {
    var list = new List<T>(value);
    JsonSerializer.Serialize(writer, list, options);
  }
}

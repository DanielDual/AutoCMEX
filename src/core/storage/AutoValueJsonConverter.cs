namespace AutoCMEX.Core.Storage;

using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chickensoft.Sync.Primitives;

/// <summary>
/// JSON 转换器工厂：为所有 AutoValue&lt;T&gt; 类型生成对应的转换器
/// </summary>
public class AutoValueJsonConverterFactory : JsonConverterFactory
{
  /// <summary>
  /// 判断是否可以转换指定类型
  /// </summary>
  public override bool CanConvert(Type typeToConvert)
  {
    if (!typeToConvert.IsGenericType)
      return false;
    return typeToConvert.GetGenericTypeDefinition() == typeof(AutoValue<>);
  }

  /// <summary>
  /// 为 AutoValue&lt;T&gt; 创建转换器实例
  /// </summary>
  public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
  {
    var valueType = typeToConvert.GetGenericArguments()[0];
    var converterType = typeof(AutoValueJsonConverter<>).MakeGenericType(valueType);
    return (JsonConverter?)Activator.CreateInstance(converterType);
  }
}

/// <summary>
/// JSON 转换器：将 AutoValue&lt;T&gt; 序列化为其 Value 属性，反序列化时构造新实例
/// </summary>
public class AutoValueJsonConverter<T> : JsonConverter<AutoValue<T>>
{
  /// <summary>
  /// 读取 JSON 并构造 AutoValue&lt;T&gt;
  /// </summary>
  public override AutoValue<T>? Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options
  )
  {
    var value = JsonSerializer.Deserialize<T>(ref reader, options);
    return value is null ? new AutoValue<T>(default!) : new AutoValue<T>(value);
  }

  /// <summary>
  /// 写入 AutoValue&lt;T&gt; 的 Value 属性
  /// </summary>
  public override void Write(
    Utf8JsonWriter writer,
    AutoValue<T> value,
    JsonSerializerOptions options
  )
  {
    JsonSerializer.Serialize(writer, value.Value, options);
  }
}

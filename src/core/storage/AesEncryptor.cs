namespace AutoCMEX.Core.Storage;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// AES-256-CBC 加密/解密工具
/// </summary>
public class AesEncryptor
{
  /// <summary>默认密钥文件名。</summary>
  public const string DefaultKeyFileName = "key.bin";

  /// <summary>
  /// 获取指定数据目录下的默认密钥文件路径。
  /// </summary>
  /// <param name="dataDir">数据目录路径。</param>
  /// <returns>完整的密钥文件路径。</returns>
  public static string GetDefaultKeyPath(string dataDir) =>
    Path.Combine(dataDir, DefaultKeyFileName);

  private readonly byte[] _key;
  private readonly byte[] _iv;

  /// <summary>
  /// 从密钥文件初始化加密器
  /// </summary>
  /// <param name="keyFilePath">密钥文件路径</param>
  public AesEncryptor(string keyFilePath)
  {
    if (File.Exists(keyFilePath))
    {
      var lines = File.ReadAllLines(keyFilePath);
      if (lines.Length >= 2)
      {
        _key = Convert.FromBase64String(lines[0]);
        _iv = Convert.FromBase64String(lines[1]);
        return;
      }
    }

    // 生成新密钥
    using var aes = Aes.Create();
    aes.KeySize = 256;
    aes.GenerateKey();
    aes.GenerateIV();
    _key = aes.Key;
    _iv = aes.IV;

    // 保存密钥文件
    var dir = Path.GetDirectoryName(keyFilePath);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
      Directory.CreateDirectory(dir);

    File.WriteAllLines(
      keyFilePath,
      new[] { Convert.ToBase64String(_key), Convert.ToBase64String(_iv) }
    );
  }

  /// <summary>
  /// 加密明文
  /// </summary>
  public string Encrypt(string plainText)
  {
    if (string.IsNullOrEmpty(plainText))
      return string.Empty;

    using var aes = Aes.Create();
    aes.Key = _key;
    aes.IV = _iv;

    using var encryptor = aes.CreateEncryptor();
    var plainBytes = Encoding.UTF8.GetBytes(plainText);
    var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
    return Convert.ToBase64String(cipherBytes);
  }

  /// <summary>
  /// 解密密文
  /// </summary>
  public string Decrypt(string cipherText)
  {
    if (string.IsNullOrEmpty(cipherText))
      return string.Empty;

    try
    {
      using var aes = Aes.Create();
      aes.Key = _key;
      aes.IV = _iv;

      using var decryptor = aes.CreateDecryptor();
      var cipherBytes = Convert.FromBase64String(cipherText);
      var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
      return Encoding.UTF8.GetString(plainBytes);
    }
    catch
    {
      return string.Empty;
    }
  }
}

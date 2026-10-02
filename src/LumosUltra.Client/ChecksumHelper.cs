using System.Security.Cryptography;
using System.Text;

namespace LumosUltra.Client;

/// <summary>
/// The device's upload/command endpoints take an <c>md5</c> query parameter. Two
/// independent community sources (marcoreps/lumos_RE on the Lumos Ultra, and 2ktsch/weburn
/// on the earlier Vision 20W) confirm this is an UPPERCASE hex MD5 of the payload, used
/// as a transmission-integrity check - not an authentication credential. See PROVENANCE.md.
/// </summary>
public static class ChecksumHelper
{
    /// <summary>Uppercase hex MD5 of UTF-8 text, as the device's API expects it.</summary>
    public static string UppercaseMd5(string content) => UppercaseMd5(Encoding.UTF8.GetBytes(content));

    /// <summary>Uppercase hex MD5 of raw bytes, as the device's API expects it.</summary>
    public static string UppercaseMd5(byte[] content) =>
        Convert.ToHexString(MD5.HashData(content)); // Convert.ToHexString is already uppercase.
}

using System.Text.Json.Serialization;

namespace LumosUltra.Client.Models;

/// <summary>
/// <c>GET /device/info</c>. Fields here are CONFIRMED by direct observation of the JSON
/// this project's authors' own Lumos Ultra actually returned, logged by MakeIt itself -
/// not a vendor-published schema. Treat any field as possibly incomplete: unrecognised
/// JSON is preserved in <see cref="Extra"/> rather than dropped, since no one has
/// published a complete schema for this endpoint. See PROVENANCE.md.
/// </summary>
public sealed record DeviceInfo
{
    [JsonPropertyName("app_version")]
    public string? AppVersion { get; init; }

    /// <summary>Observed value: "LU25xx".</summary>
    [JsonPropertyName("dev_type")]
    public string? DeviceType { get; init; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("laser_id")]
    public string? LaserId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("sn")]
    public string? SerialNumber { get; init; }

    /// <summary>Allwinner "Tina" SDK build timestamp, e.g. "tina.dev.20251225.084241".</summary>
    [JsonPropertyName("soc_img_buildtime")]
    public string? SocImageBuildTime { get; init; }

    [JsonPropertyName("ver_mcu_1")]
    public string? McuVersion1 { get; init; }

    [JsonPropertyName("ver_mcu_2")]
    public string? McuVersion2 { get; init; }

    [JsonPropertyName("wlan0_ip")]
    public string? Wlan0Ip { get; init; }

    [JsonPropertyName("wlan0_mac")]
    public string? Wlan0Mac { get; init; }

    /// <summary>Observed values: "USB", "WIFI".</summary>
    [JsonPropertyName("connectType")]
    public string? ConnectType { get; init; }

    [JsonPropertyName("connectIP")]
    public string? ConnectIp { get; init; }

    [JsonPropertyName("mac")]
    public string? Mac { get; init; }

    /// <summary>Observed values include "5w", "60w", "100w", "mopa" - inconsistent
    /// formatting in what the device itself reports, not normalised here.</summary>
    [JsonPropertyName("power")]
    public string? Power { get; init; }

    /// <summary>Anything this type doesn't have a named property for yet.</summary>
    [JsonExtensionData]
    public Dictionary<string, object?> Extra { get; init; } = new();
}

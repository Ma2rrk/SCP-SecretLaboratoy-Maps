using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SLMapsOverlay;

public sealed class MapApiResponse
{
    [JsonPropertyName("seed")]
    public long Seed { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("zones")]
    public Dictionary<string, List<RoomData>> Zones { get; set; } = new();
}

public sealed class RoomData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("zone")]
    public string Zone { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("short")]
    public string Short { get; set; } = string.Empty;

    [JsonPropertyName("glyph")]
    public string Glyph { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("shape")]
    public string Shape { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    [JsonPropertyName("rotY")]
    public float RotY { get; set; }

    [JsonPropertyName("cx")]
    public float Cx { get; set; }

    [JsonPropertyName("cy")]
    public float Cy { get; set; }

    [JsonPropertyName("conn")]
    public List<RoomConnection> Conn { get; set; } = new();
}

public sealed class RoomConnection
{
    [JsonPropertyName("dx")]
    public int Dx { get; set; }

    [JsonPropertyName("dz")]
    public int Dz { get; set; }
}

using System.Collections.Generic;
using System.Text.Json;
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

    [JsonPropertyName("variant")]
    public string Variant { get; set; } = string.Empty;

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

    [JsonIgnore]
    public float? TargetX { get; set; }

    [JsonIgnore]
    public float? TargetZ { get; set; }
}

internal static class MapResponseAdapter
{
    public static MapApiResponse Parse(string json, long requestedSeed, bool backup)
    {
        if (!backup)
        {
            var primaryResponse = JsonSerializer.Deserialize<MapApiResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (primaryResponse is not null && primaryResponse.Zones.Count > 0) return primaryResponse;

            // 共享缓存可能来自备用 API，继续使用兼容解析器处理 rooms/zones 结构。
        }

        using var document = JsonDocument.Parse(json);
        var node = FindMapNode(document.RootElement);
        var response = new MapApiResponse
        {
            Seed = ReadLong(node, requestedSeed, "seed", "mapSeed"),
            Version = (int)ReadLong(node, 0, "version", "mapVersion"),
        };

        if (node.ValueKind == JsonValueKind.Array)
        {
            ReadRoomArray(node, response.Zones, string.Empty);
        }
        else if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("zones", out var zones))
        {
            ReadZones(zones, response.Zones);
        }
        else if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("rooms", out var rooms))
        {
            ReadRoomArray(rooms, response.Zones, string.Empty);
        }

        return response;
    }

    private static JsonElement FindMapNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return element;
        if (element.TryGetProperty("zones", out _) || element.TryGetProperty("rooms", out _)) return element;
        foreach (var name in new[] { "data", "map", "result", "payload" })
        {
            if (element.TryGetProperty(name, out var child))
            {
                var found = FindMapNode(child);
                if (found.ValueKind != JsonValueKind.Undefined) return found;
            }
        }
        return element;
    }

    private static void ReadZones(JsonElement zones, Dictionary<string, List<RoomData>> target)
    {
        if (zones.ValueKind == JsonValueKind.Object)
        {
            foreach (var zone in zones.EnumerateObject())
            {
                if (zone.Value.ValueKind == JsonValueKind.Array)
                {
                    ReadRoomArray(zone.Value, target, zone.Name);
                }
            }
        }
        else if (zones.ValueKind == JsonValueKind.Array)
        {
            foreach (var zone in zones.EnumerateArray())
            {
                var name = ReadString(zone, "", "name", "id", "zone", "key");
                if (zone.TryGetProperty("rooms", out var rooms)) ReadRoomArray(rooms, target, name);
            }
        }
    }

    private static void ReadRoomArray(JsonElement rooms, Dictionary<string, List<RoomData>> target, string fallbackZone)
    {
        if (rooms.ValueKind != JsonValueKind.Array) return;
        foreach (var item in rooms.EnumerateArray())
        {
            var room = ReadRoom(item, fallbackZone);
            var zone = string.IsNullOrWhiteSpace(room.Zone) ? fallbackZone : room.Zone;
            if (string.IsNullOrWhiteSpace(zone)) zone = "HeavyContainment";
            if (!target.TryGetValue(zone, out var list)) target[zone] = list = new List<RoomData>();
            list.Add(room);
        }
    }

    private static RoomData ReadRoom(JsonElement item, string fallbackZone)
    {
        var position = Child(item, "position", "coordinates", "pos");
        var room = new RoomData
        {
            Id = (int)ReadLong(item, 0, "id", "index"),
            Name = ReadString(item, "", "name", "room", "type"),
            Variant = ReadString(item, "", "variant", "prefab", "template"),
            Zone = ReadString(item, fallbackZone, "zone", "area", "region"),
            Group = ReadString(item, "", "group", "layer"),
            Label = ReadString(item, "", "label", "displayName"),
            Category = ReadString(item, "", "category", "kind"),
            Short = ReadString(item, "", "short", "shortName"),
            Glyph = ReadString(item, "", "glyph", "icon"),
            Code = ReadString(item, "", "code", "roomCode"),
            Shape = ReadString(item, "", "shape", "form"),
            X = (float)ReadDouble(position, ReadDouble(item, 0, "x", "posX"), "x"),
            Y = (float)ReadDouble(position, ReadDouble(item, 0, "y", "posY"), "y"),
            Z = (float)ReadDouble(position, ReadDouble(item, 0, "z", "posZ"), "z"),
            RotY = (float)ReadDouble(item, 0, "rotY", "rotation"),
            Cx = (float)ReadDouble(item, 0, "cx", "width"),
            Cy = (float)ReadDouble(item, 0, "cy", "height"),
        };
        var connections = Child(item, "conn", "connections", "links");
        if (connections.ValueKind == JsonValueKind.Array)
        {
            foreach (var connection in connections.EnumerateArray())
            {
                var targetX = 0d;
                var targetZ = 0d;
                var hasTargetCoordinates = TryReadDouble(connection, out targetX, "x")
                    && TryReadDouble(connection, out targetZ, "z");
                var dx = hasTargetCoordinates
                    ? (int)Math.Round((targetX - room.X) / 15d)
                    : (int)ReadDouble(connection, 0, "dx", "x");
                var dz = hasTargetCoordinates
                    ? (int)Math.Round((targetZ - room.Z) / 15d)
                    : (int)ReadDouble(connection, 0, "dz", "z");
                room.Conn.Add(new RoomConnection
                {
                    Dx = dx,
                    Dz = dz,
                    TargetX = hasTargetCoordinates ? (float)targetX : null,
                    TargetZ = hasTargetCoordinates ? (float)targetZ : null,
                });
            }
        }
        return room;
    }

    private static JsonElement Child(JsonElement element, params string[] names)
    {
        foreach (var name in names) if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child)) return child;
        return default;
    }

    private static string ReadString(JsonElement element, string fallback, params string[] names)
    {
        var child = Child(element, names);
        return child.ValueKind == JsonValueKind.String ? child.GetString() ?? fallback : fallback;
    }

    private static bool TryReadDouble(JsonElement element, out double value, params string[] names)
    {
        var child = Child(element, names);
        if (child.ValueKind == JsonValueKind.Number && child.TryGetDouble(out value)) return true;
        value = 0;
        return false;
    }

    private static long ReadLong(JsonElement element, long fallback, params string[] names) => (long)ReadDouble(element, fallback, names);

    private static double ReadDouble(JsonElement element, double fallback, params string[] names)
    {
        var child = Child(element, names);
        return child.ValueKind == JsonValueKind.Number && child.TryGetDouble(out var value) ? value : fallback;
    }
}

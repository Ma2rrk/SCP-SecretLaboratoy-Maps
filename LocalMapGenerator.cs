using System.Text.Json;

namespace SLMapsOverlay;

internal static class LocalMapGenerator
{
    private static readonly string[] ZoneOrder = ["LightContainment", "HeavyContainment", "Entrance"];
    private static readonly Lazy<GenerationData> Data = new(ReadData);

    public static MapApiResponse Generate(long seed)
    {
        var random = new LegacyRandom(unchecked((int)seed));
        var map = new MapApiResponse { Seed = seed, Version = 15 };
        foreach (var zoneName in ZoneOrder)
        {
            var zone = Data.Value.Zones[zoneName];
            var atlasIndex = random.Next(zone.Atlases.Count);
            map.AtlasIndex[zoneName] = atlasIndex;
            var cells = zone.Atlases[atlasIndex].Select(cell => cell.Copy()).ToList();
            if (zoneName == "Entrance") AlignEntrance(cells, map.Zones["HeavyContainment"]);
            foreach (var cell in cells)
            {
                cell.Rotation = cell.Rotations[random.Next(cell.Rotations.Length)];
                if (zoneName == "Entrance") cell.Rotation = (cell.Rotation + 270) % 360;
            }
            for (var i = cells.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (cells[i], cells[j]) = (cells[j], cells[i]);
            }
            map.Zones[zoneName] = SelectRooms(zoneName, cells, zone.Templates, random);
        }
        Validate(map);
        return map;
    }

    private static GenerationData ReadData()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "map-generation-data.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<GenerationData>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("地图生成数据为空");
    }

    private static void AlignEntrance(List<AtlasCell> cells, List<RoomData> heavyRooms)
    {
        var entrance = cells.Where(cell => cell.SpecificRooms.Contains(13)).ToList();
        var heavy = heavyRooms.Where(room => room.Variant == "HCZ_EZ_Checkpoint Part").ToList();
        if (entrance.Count != 2 || heavy.Count != 2)
            throw new InvalidDataException("HCZ/EZ 检查点数量异常");
        var offsetX = (int)heavy.Average(room => room.X) + 15 - (int)entrance.Average(cell => cell.X);
        var offsetZ = (int)heavy.Average(room => room.Z) - (int)entrance.Average(cell => cell.Z);
        foreach (var cell in cells)
        {
            cell.X += offsetX;
            cell.Z += offsetZ;
        }
    }

    private static List<RoomData> SelectRooms(string zoneName, List<AtlasCell> cells,
        List<RoomTemplate> templates, LegacyRandom random)
    {
        var result = new List<RoomData>();
        var amounts = new int[templates.Count];
        var templateIndices = templates.Select((template, index) => (template, index))
            .ToDictionary(item => item.template, item => item.index);
        var reserved = cells.SelectMany(cell => cell.SpecificRooms).ToHashSet();
        var adjacentCounts = new Dictionary<(string Name, int X, int Z), int>();
        var upperCheckpointZ = cells.Where(cell => cell.SpecificRooms.Contains(13))
            .Select(cell => cell.Z).DefaultIfEmpty().Min();
        foreach (var cell in cells)
        {
            var candidates = new List<RoomTemplate>();
            for (var index = 0; index < templates.Count; index++)
            {
                var template = templates[index];
                if (cell.SpecificRooms.Length > 0
                    ? cell.SpecificRooms.Contains(template.RoomNameId)
                    : template.ShapeId == cell.ShapeId && !reserved.Contains(template.RoomNameId)
                        && amounts[index] < template.MaxAmount)
                    candidates.Add(template);
            }
            if (candidates.Count == 0)
                throw new InvalidDataException($"地图格子没有可用房间: {zoneName} ({cell.X}, {cell.Z})");
            RoomTemplate? chosen = null;
            foreach (var candidate in candidates)
            {
                if (amounts[templateIndices[candidate]] < candidate.MinAmount)
                {
                    chosen = candidate;
                    break;
                }
            }
            if (chosen is null)
            {
                var weights = candidates.Select(t =>
                {
                    var adjacent = adjacentCounts.GetValueOrDefault((t.Name, cell.X, cell.Z));
                    return t.ChanceMultiplier * Math.Pow(t.AdjacentChanceMultiplier, adjacent);
                }).ToArray();
                var value = random.NextDouble() * weights.Sum();
                chosen = candidates[^1];
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (value < weights[i])
                    {
                        chosen = candidates[i];
                        break;
                    }
                    value -= weights[i];
                }
            }
            amounts[templateIndices[chosen]]++;
            adjacentCounts[(chosen.Name, cell.X - 15, cell.Z)] =
                adjacentCounts.GetValueOrDefault((chosen.Name, cell.X - 15, cell.Z)) + 1;
            adjacentCounts[(chosen.Name, cell.X + 15, cell.Z)] =
                adjacentCounts.GetValueOrDefault((chosen.Name, cell.X + 15, cell.Z)) + 1;
            adjacentCounts[(chosen.Name, cell.X, cell.Z - 15)] =
                adjacentCounts.GetValueOrDefault((chosen.Name, cell.X, cell.Z - 15)) + 1;
            adjacentCounts[(chosen.Name, cell.X, cell.Z + 15)] =
                adjacentCounts.GetValueOrDefault((chosen.Name, cell.X, cell.Z + 15)) + 1;
            var display = chosen.Display;
            result.Add(new RoomData
            {
                Id = result.Count,
                Name = display.Name,
                Variant = chosen.Name,
                Zone = zoneName,
                Group = display.Group,
                Label = chosen.Name is "HCZ_EZ_Checkpoint Part" or "EZ_HCZ_Checkpoint Part"
                    ? (cell.Z == upperCheckpointZ ? "上检查点（通往办公区）" : "下检查点（通往办公区）")
                    : display.Label,
                Category = display.Category,
                Short = chosen.Name is "HCZ_EZ_Checkpoint Part" or "EZ_HCZ_Checkpoint Part"
                    ? (cell.Z == upperCheckpointZ ? "上检" : "下检")
                    : display.Short,
                Glyph = display.Glyph,
                Code = cell.Code,
                Shape = cell.ShapeId switch
                {
                    1 => "Endroom", 2 => "Straight", 3 => "Curve", 4 => "TShape", 5 => "XShape",
                    _ => throw new InvalidDataException($"未知房间形状 {cell.ShapeId}"),
                },
                X = cell.X,
                Y = zoneName == "LightContainment" ? 100 : -100,
                Z = cell.Z,
                RotY = cell.Rotation,
                Cx = cell.Cx,
                Cy = cell.Cy,
                Conn = cell.Connections[cell.Rotation.ToString()]
                    .Select(pair => new RoomConnection { Dx = pair[0], Dz = pair[1] }).ToList(),
            });
        }
        return result;
    }

    private static void Validate(MapApiResponse map)
    {
        if (map.Zones.Count != 3 || map.Zones.Values.Any(rooms => rooms.Count < 20))
            throw new InvalidDataException("地图区域或房间数量异常");
        var positions = new HashSet<(float X, float Y, float Z)>();
        foreach (var room in map.Zones.Values.SelectMany(rooms => rooms))
            if (!positions.Add((room.X, room.Y, room.Z)))
                throw new InvalidDataException("地图包含重复房间坐标");
    }

    private sealed class GenerationData
    {
        public Dictionary<string, GenerationZone> Zones { get; set; } = new();
    }

    private sealed class GenerationZone
    {
        public List<List<AtlasCell>> Atlases { get; set; } = new();
        public List<RoomTemplate> Templates { get; set; } = new();
    }

    private sealed class AtlasCell
    {
        public int X { get; set; }
        public int Z { get; set; }
        public int Cx { get; set; }
        public int Cy { get; set; }
        public int ShapeId { get; set; }
        public int[] Rotations { get; set; } = [];
        public int[] SpecificRooms { get; set; } = [];
        public Dictionary<string, int[][]> Connections { get; set; } = new();
        public string Code { get; set; } = string.Empty;
        public int Rotation { get; set; }

        public AtlasCell Copy() => new()
        {
            X = X, Z = Z, Cx = Cx, Cy = Cy, ShapeId = ShapeId,
            Rotations = Rotations, SpecificRooms = SpecificRooms, Connections = Connections, Code = Code,
        };
    }

    private sealed class RoomTemplate
    {
        public string Name { get; set; } = string.Empty;
        public int ShapeId { get; set; }
        public int RoomNameId { get; set; }
        public int MinAmount { get; set; }
        public int MaxAmount { get; set; }
        public double ChanceMultiplier { get; set; }
        public double AdjacentChanceMultiplier { get; set; }
        public RoomDisplay Display { get; set; } = new();
    }

    private sealed class RoomDisplay
    {
        public string Name { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Short { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
    }

    private sealed class LegacyRandom
    {
        private const int Mbig = int.MaxValue;
        private readonly int[] _values = new int[56];
        private int _next;
        private int _nextPrime;

        public LegacyRandom(int seed)
        {
            var subtraction = seed == int.MinValue ? Mbig : Math.Abs(seed);
            var previous = 161803398 - subtraction;
            _values[55] = previous;
            var current = 1;
            for (var i = 1; i < 55; i++)
            {
                var slot = 21 * i % 55;
                _values[slot] = current;
                current = previous - current;
                if (current < 0) current += Mbig;
                previous = _values[slot];
            }
            for (var pass = 0; pass < 4; pass++)
                for (var i = 1; i < 56; i++)
                {
                    var value = _values[i] - _values[1 + (i + 30) % 55];
                    _values[i] = value < 0 ? value + Mbig : value;
                }
            _nextPrime = 21;
        }

        public double NextDouble()
        {
            _next = _next + 1 >= 56 ? 1 : _next + 1;
            _nextPrime = _nextPrime + 1 >= 56 ? 1 : _nextPrime + 1;
            var value = _values[_next] - _values[_nextPrime];
            if (value == Mbig) value--;
            if (value < 0) value += Mbig;
            _values[_next] = value;
            return value * (1.0 / Mbig);
        }

        public int Next(int maxValue) => (int)(NextDouble() * maxValue);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SLMapsOverlay;

internal static class LocalMapGenerator
{
    private const long TemplateSeed = 224593721;
    private const string TemplateFileName = "generated-map-224593721.json";
    private static readonly Lazy<string> TemplateJson = new(ReadTemplate);

    public static MapApiResponse Generate(long seed)
    {
        var template = MapResponseAdapter.Parse(TemplateJson.Value, TemplateSeed, backup: false);
        template.Seed = seed;
        template.Version = 1;

        if (seed != TemplateSeed)
        {
            // The template supplies the offline room pool and validated topology.
            // The seeded pass below varies compatible room identities and orientation.
            MainForm.LogMessage($"本地地图使用已验证拓扑模板，当前 seed={seed}，精确游戏布局仅支持模板 seed={TemplateSeed}");
        }

        if (seed != TemplateSeed) ApplySeededLayout(template, seed);
        ValidateGeneratedMap(template);
        return template;
    }

    private static string ReadTemplate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, TemplateFileName);
        if (!File.Exists(path)) throw new FileNotFoundException("本地地图模板不存在", path);
        return File.ReadAllText(path);
    }

    private static void ValidateGeneratedMap(MapApiResponse map)
    {
        var rooms = map.Zones.Values.SelectMany(rooms => rooms).ToList();
        if (map.Zones.Count < 3 || rooms.Count < 100)
        {
            throw new InvalidDataException("本地地图模板区域或房间数量不足");
        }

        var coordinates = new HashSet<(float X, float Y, float Z)>();
        foreach (var room in rooms)
        {
            if (!coordinates.Add((room.X, room.Y, room.Z)))
            {
                throw new InvalidDataException("本地地图模板包含重复房间坐标");
            }
        }
    }

    private static void ApplySeededLayout(MapApiResponse map, long seed)
    {
        var random = new LegacyRandom(unchecked((int)seed));

        foreach (var rooms in map.Zones.Values)
        {
            var groups = rooms
                .GroupBy(room => string.Concat(room.Zone, "|", room.Shape, "|", room.Conn.Count, "|", room.Cx.ToString("0.###", CultureInfo.InvariantCulture), "|", room.Cy.ToString("0.###", CultureInfo.InvariantCulture)))
                .ToList();

            foreach (var group in groups)
            {
                var slots = group.ToList();
                var descriptors = slots.Select(RoomDescriptor.From).ToList();
                Shuffle(descriptors, random);
                for (var i = 0; i < slots.Count; i++) descriptors[i].ApplyTo(slots[i]);
            }
        }
        var turns = random.Next(4);
        var offsetX = random.Next(-8, 9) * 15f;
        var offsetZ = random.Next(-8, 9) * 15f;

        foreach (var room in map.Zones.Values.SelectMany(rooms => rooms))
        {
            var x = room.X;
            var z = room.Z;
            (room.X, room.Z) = turns switch
            {
                1 => (-z + offsetX, x + offsetZ),
                2 => (-x + offsetX, -z + offsetZ),
                3 => (z + offsetX, -x + offsetZ),
                _ => (x + offsetX, z + offsetZ),
            };
            room.RotY = (room.RotY + turns * 90f) % 360f;

            foreach (var connection in room.Conn)
            {
                (connection.Dx, connection.Dz) = turns switch
                {
                    1 => (-connection.Dz, connection.Dx),
                    2 => (-connection.Dx, -connection.Dz),
                    3 => (connection.Dz, -connection.Dx),
                    _ => (connection.Dx, connection.Dz),
                };
            }
        }
    }

    private static void Shuffle<T>(IList<T> values, LegacyRandom random)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private sealed class RoomDescriptor
    {
        public string Name { get; private init; } = string.Empty;
        public string Variant { get; private init; } = string.Empty;
        public string Label { get; private init; } = string.Empty;
        public string Category { get; private init; } = string.Empty;
        public string Short { get; private init; } = string.Empty;
        public string Glyph { get; private init; } = string.Empty;
        public string Code { get; private init; } = string.Empty;

        public static RoomDescriptor From(RoomData room) => new()
        {
            Name = room.Name,
            Variant = room.Variant,
            Label = room.Label,
            Category = room.Category,
            Short = room.Short,
            Glyph = room.Glyph,
            Code = room.Code,
        };

        public void ApplyTo(RoomData room)
        {
            room.Name = Name;
            room.Variant = Variant;
            room.Label = Label;
            room.Category = Category;
            room.Short = Short;
            room.Glyph = Glyph;
            room.Code = Code;
        }
    }

    private sealed class LegacyRandom
    {
        private const int MBIG = int.MaxValue;
        private const int MSEED = 161803398;
        private readonly int[] _seedArray = new int[56];
        private int _inext;
        private int _inextp;

        public LegacyRandom(int seed)
        {
            var subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
            var mj = MSEED - subtraction;
            _seedArray[55] = mj;
            var mk = 1;
            for (var i = 1; i < 55; i++)
            {
                var ii = 21 * i % 55;
                _seedArray[ii] = mk;
                mk = mj - mk;
                if (mk < 0) mk += MBIG;
                mj = _seedArray[ii];
            }

            for (var k = 1; k < 5; k++)
            {
                for (var i = 1; i < 56; i++)
                {
                    var n = _seedArray[i] - _seedArray[1 + (i + 30) % 55];
                    _seedArray[i] = n < 0 ? n + MBIG : n;
                }
            }

            _inext = 0;
            _inextp = 21;
        }

        private int InternalSample()
        {
            var locInext = _inext + 1;
            if (locInext >= 56) locInext = 1;
            var locInextp = _inextp + 1;
            if (locInextp >= 56) locInextp = 1;
            var retVal = _seedArray[locInext] - _seedArray[locInextp];
            if (retVal == MBIG) retVal--;
            if (retVal < 0) retVal += MBIG;
            _seedArray[locInext] = retVal;
            _inext = locInext;
            _inextp = locInextp;
            return retVal;
        }

        public int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
            return (int)(InternalSample() * (1.0 / MBIG) * maxValue);
        }

        public int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            var range = (long)maxValue - minValue;
            return (int)(InternalSample() * (1.0 / MBIG) * range) + minValue;
        }
    }
}

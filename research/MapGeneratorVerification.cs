using System.Text.Json;
using SLMapsOverlay;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var legacyData = Path.Combine(root, ".tools", "legacy-baseline", "map-generation-data.json");
var packagedLegacyData = Path.Combine(root, "map-generation-data-14.2.9.json");
Check(File.Exists(packagedLegacyData), 0, "Version switch", "packaged 14.2.9 data");
var hasLegacyFixtures = File.Exists(legacyData)
    && Enumerable.Range(1, 50).All(seed => File.Exists(Path.Combine(root, ".tools", "samples", $"{seed}.json")))
    && File.Exists(Path.Combine(root, "backup-api-224593721.json"))
    && File.Exists(Path.Combine(root, "backup-api-7654321.json"));
var checkedRooms = 0;
if (hasLegacyFixtures)
{
    for (var seed = 1; seed <= 50; seed++)
    {
        var path = Path.Combine(root, ".tools", "samples", $"{seed}.json");
        var expected = JsonSerializer.Deserialize<MapApiResponse>(File.ReadAllText(path))!;
        var generated = LocalMapGenerator.Generate(seed, legacyData);
        foreach (var (zone, index) in expected.AtlasIndex)
            Check(generated.AtlasIndex[zone] == index, seed, zone, "atlas index");
        foreach (var (zone, expectedRooms) in expected.Zones)
        {
            var rooms = generated.Zones[zone];
            Check(rooms.Count == expectedRooms.Count, seed, zone, "count");
            for (var index = 0; index < rooms.Count; index++)
            {
                var actual = rooms[index];
                var reference = expectedRooms[index];
                Check(actual.X == reference.X && actual.Y == reference.Y && actual.Z == reference.Z,
                    seed, zone, $"{index} position");
                Check(actual.RotY == reference.RotY && actual.Shape == reference.Shape,
                    seed, zone, $"{index} rotation/shape");
                var expectedLabel = actual.Variant == "HCZ_MicroHID_New" ? "HID" : reference.Label;
                Check(actual.Name == reference.Name && actual.Label == expectedLabel,
                    seed, zone, $"{index} name/label: {actual.Variant} {actual.Name}/{actual.Label} vs {reference.Name}/{reference.Label}");
                Check(actual.Group == reference.Group && actual.Category == reference.Category
                    && actual.Short == reference.Short && actual.Glyph == reference.Glyph,
                    seed, zone, $"{index} display metadata: {actual.Group}/{actual.Category}/{actual.Short}/{actual.Glyph} vs {reference.Group}/{reference.Category}/{reference.Short}/{reference.Glyph}");
                Check(actual.Cx == reference.Cx && actual.Cy == reference.Cy,
                    seed, zone, $"{index} atlas coordinates");
                Check(actual.Conn.Select(connection => (connection.Dx, connection.Dz)).Order().SequenceEqual(
                    reference.Conn.Select(connection => (connection.Dx, connection.Dz)).Order()),
                    seed, zone, $"{index} connections");
                checkedRooms++;
            }
        }
    }
}

for (var seed = 1; seed <= 10; seed++)
{
    var generated = LocalMapGenerator.Generate(seed, packagedLegacyData);
    Check(generated.Version == 14 && generated.Zones.Values.All(rooms => rooms.Count > 0),
        seed, "Version switch", "14.2.9 generation");
}

using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "map-generation-data.json"))))
{
    var zones = document.RootElement.GetProperty("zones");
    for (var seed = 1; seed <= 100; seed++)
    {
        var generated = LocalMapGenerator.Generate(seed);
        var repeated = LocalMapGenerator.Generate(seed);
        foreach (var (zone, rooms) in generated.Zones)
        {
            var atlasCount = zones.GetProperty(zone).GetProperty("atlases")
                .GetArrayLength();
            Check(generated.AtlasIndex[zone] >= 0 && generated.AtlasIndex[zone] < atlasCount,
                seed, zone, "beta atlas index");
            Check(rooms.Count == zones.GetProperty(zone).GetProperty("atlases")
                    .EnumerateArray().ElementAt(generated.AtlasIndex[zone]).GetArrayLength(),
                seed, zone, "beta room count");
            for (var index = 0; index < rooms.Count; index++)
            {
                var room = rooms[index];
                var samePosition = repeated.Zones[zone][index];
                Check(room.X == samePosition.X && room.Z == samePosition.Z
                    && room.Variant == samePosition.Variant && room.RotY == samePosition.RotY,
                    seed, zone, "beta determinism");
                if (room.Variant == "EZ_GateA")
                {
                    Check(room.ConnectorPorts.GetValueOrDefault("SimpleRoomConnectorSpawnpoint") == 1
                        && room.ConnectorPorts.GetValueOrDefault("WallableSmallNodeRoomConnector") == 7,
                        seed, zone, "Gate A connector metadata");
                }
                else if (room.Variant == "EZ_GateB")
                {
                    Check(room.ConnectorPorts.GetValueOrDefault("SimpleRoomConnectorSpawnpoint") == 1
                        && room.ConnectorPorts.GetValueOrDefault("SpawnableRoomConnector") == 3,
                        seed, zone, "Gate B connector metadata");
                }
            }
        }
        var allRooms = generated.Zones.Values.SelectMany(rooms => rooms).ToList();
        foreach (var room in allRooms)
            foreach (var connection in room.Conn)
            {
                var target = allRooms.SingleOrDefault(candidate => candidate.X == room.X + connection.Dx * 15
                    && candidate.Y == room.Y && candidate.Z == room.Z + connection.Dz * 15);
                Check(target is not null && target.Conn.Any(reverse => reverse.Dx == -connection.Dx
                    && reverse.Dz == -connection.Dz), seed, room.Zone,
                    $"beta reciprocal connection {room.X},{room.Z}");
            }
    }
}

if (hasLegacyFixtures)
foreach (var seed in new long[] { 224593721, 7654321 })
{
    var path = Path.Combine(root, $"backup-api-{seed}.json");
    var backup = MapResponseAdapter.Parse(File.ReadAllText(path), seed, backup: true);
    var expected = backup.Zones.Values.SelectMany(rooms => rooms)
        .ToDictionary(room => (room.Zone, room.X, room.Y, room.Z));
    var generated = LocalMapGenerator.Generate(seed, legacyData);
    foreach (var room in generated.Zones.Values.SelectMany(rooms => rooms))
    {
        Check(expected.TryGetValue((room.Zone, room.X, room.Y, room.Z), out var reference),
            seed, room.Zone, "backup position");
        Check(reference!.Variant == room.Variant + "(Clone)" && reference.RotY == room.RotY,
            seed, room.Zone, $"backup variant {room.X}, {room.Z}");
        checkedRooms++;
    }
}
var logPath = Path.Combine(Path.GetTempPath(), $"slmaps-seed-monitor-{Guid.NewGuid():N}.log");
try
{
    File.WriteAllText(logPath, "Map seed is: 12345\n");
    Check(SeedMonitor.TryGetLatestSeed(logPath, out var firstSeed) && firstSeed == 12345,
        12345, "SeedMonitor", "initial read");
    File.AppendAllText(logPath, "Map seed is: 67890\n");
    Check(SeedMonitor.TryGetLatestSeed(logPath, out var appendedSeed) && appendedSeed == 67890,
        67890, "SeedMonitor", "append read");
    File.WriteAllText(logPath, "Map seed is: 24680\n");
    Check(SeedMonitor.TryGetLatestSeed(logPath, out var rotatedSeed) && rotatedSeed == 24680,
        24680, "SeedMonitor", "truncation reset");
}
finally
{
    File.Delete(logPath);
}
Console.WriteLine(hasLegacyFixtures
    ? $"Verified {checkedRooms} historical rooms across 52 seeds and Beta generation across 100 seeds."
    : "Legacy fixtures absent; verified Beta generation across 100 seeds.");

static void Check(bool condition, long seed, string zone, string detail)
{
    if (!condition) throw new Exception($"Seed {seed}, {zone}: {detail}");
}

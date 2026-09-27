using System.Text.Json;
using SLMapsOverlay;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var checkedRooms = 0;
for (var seed = 1; seed <= 50; seed++)
{
    var path = Path.Combine(root, ".tools", "samples", $"{seed}.json");
    var expected = JsonSerializer.Deserialize<MapApiResponse>(File.ReadAllText(path))!;
    var generated = LocalMapGenerator.Generate(seed);
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

foreach (var seed in new long[] { 224593721, 7654321 })
{
    var path = Path.Combine(root, $"backup-api-{seed}.json");
    var backup = MapResponseAdapter.Parse(File.ReadAllText(path), seed, backup: true);
    var expected = backup.Zones.Values.SelectMany(rooms => rooms)
        .ToDictionary(room => (room.Zone, room.X, room.Y, room.Z));
    var generated = LocalMapGenerator.Generate(seed);
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
Console.WriteLine($"Verified {checkedRooms} rooms across 52 seeds.");

static void Check(bool condition, long seed, string zone, string detail)
{
    if (!condition) throw new Exception($"Seed {seed}, {zone}: {detail}");
}

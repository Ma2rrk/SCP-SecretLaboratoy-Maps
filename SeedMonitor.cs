using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SLMapsOverlay;

public static partial class SeedMonitor
{
    private sealed class LogReadState
    {
        public long Position { get; set; }
        public DateTime CreationTimeUtc { get; set; }
        public string PendingLine { get; set; } = string.Empty;
        public long LatestSeed { get; set; }
    }

    private static readonly object StateLock = new();
    private static readonly Dictionary<string, LogReadState> ReadStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex[] SeedRegexes =
    {
        new("Map\\s+seed\\s+is\\s*:\\s*(-?\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new("seed\\s+is\\s*[:=]\\s*(-?\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new("Map\\s+seed\\s*[:=]\\s*(-?\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:^|\s)(-?\d{1,10})\s*(?:\|\||$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    public static bool TryGetLatestSeed(string? logPath, out long seed)
    {
        seed = 0;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            candidates.Add(logPath);
        }

        foreach (var candidate in GetCandidateLogPaths())
        {
            if (candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        foreach (var candidate in candidates)
        {
            if (TryReadLatestSeed(candidate, out seed))
            {
                return true;
            }
        }

        return false;
    }

    public static string? GetLatestLogPath()
    {
        return GetCandidateLogPaths().FirstOrDefault(File.Exists);
    }

    private static bool TryReadLatestSeed(string path, out long seed)
    {
        seed = 0;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            const int tailBytes = 256 * 1024;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var creationTimeUtc = File.GetCreationTimeUtc(path);
            lock (StateLock)
            {
                if (!ReadStates.TryGetValue(path, out var state)
                    || stream.Length < state.Position || state.CreationTimeUtc != creationTimeUtc)
                {
                    state = new LogReadState
                    {
                        Position = Math.Max(0, stream.Length - tailBytes),
                        CreationTimeUtc = creationTimeUtc,
                    };
                    ReadStates[path] = state;
                }

                if (state.Position == stream.Length)
                {
                    seed = state.LatestSeed;
                    return seed > 0;
                }

                stream.Position = state.Position;
                using var reader = new StreamReader(stream);
                var appendedText = reader.ReadToEnd();
                state.Position = stream.Position;
                var lines = (state.PendingLine + appendedText).Split('\n');
                state.PendingLine = lines[^1];
                foreach (var line in lines.Skip(Math.Max(0, lines.Length - 512)))
                    ReadSeedFromLine(line, state);

                seed = state.LatestSeed;
                return seed > 0;
            }
        }
        catch
        {
            return false;
        }

    }

    private static void ReadSeedFromLine(string line, LogReadState state)
    {
        line = line.TrimEnd('\r');
        foreach (var regex in SeedRegexes)
        {
            var match = regex.Match(line);
            if (match.Success && long.TryParse(match.Groups[1].Value, out var value)
                && value >= 1 && value <= int.MaxValue)
            {
                state.LatestSeed = value;
                return;
            }
        }
    }

    public static IEnumerable<string> GetCandidateLogPaths()
    {
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ??
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.Combine(userProfile, "AppData", "LocalLow", "Northwood", "SCPSL"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            roots.Add(Path.Combine(localAppData, "..", "LocalLow", "Northwood", "SCPSL"));
        }

        if (!string.IsNullOrWhiteSpace(appData))
        {
            roots.Add(Path.Combine(appData, "..", "LocalLow", "Northwood", "SCPSL"));
        }

        var list = new List<string>();
        foreach (var root in roots)
        {
            var fullRoot = Path.GetFullPath(root);
            list.Add(Path.Combine(fullRoot, "Player.log"));
            list.Add(Path.Combine(fullRoot, "Player-prev.log"));
            list.Add(Path.Combine(fullRoot, "Player.log.1"));
            list.Add(Path.Combine(fullRoot, "Player-prev.log.1"));
        }

        return list
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

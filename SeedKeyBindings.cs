using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SLMapsOverlay;

// Read the embedded PowerShell data as text. Never execute the detector script.
internal static class SeedKeyBindings
{
    public const string FileName = "scpsl-seed-detector.bat";

    public static IReadOnlyDictionary<string, int> Read(string path, out string command)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var keyMap = Regex.Match(text, @"\$KEYMAP\s*=\s*@\{(?<entries>.*?)^\}",
            RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!keyMap.Success) throw new InvalidDataException("BAT 中未找到 KEYMAP");

        var bindings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match entry in Regex.Matches(keyMap.Groups["entries"].Value,
            @"(?<key>\w+)\s*=\s*@\{[^}]*\bkc\s*=\s*(?<code>0x[\da-fA-F]+|\d+)\s*[^}]*\}"))
        {
            var value = entry.Groups["code"].Value;
            var keyCode = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : int.Parse(value, CultureInfo.InvariantCulture);
            if (keyCode <= 0 || !Enum.TryParse<System.Windows.Forms.Keys>(entry.Groups["key"].Value, true, out var key)
                || key == System.Windows.Forms.Keys.None)
                throw new InvalidDataException("BAT 中存在无效按键映射");
            bindings.Add(entry.Groups["key"].Value, keyCode);
        }
        if (bindings.Count == 0) throw new InvalidDataException("BAT 中没有可用键码");

        var sync = Regex.Match(text, @"function\s+Sync-Cmdbinding\b.*?\$aliases\s*=\s*@\((?<aliases>[^\r\n]*)\)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var binding = Regex.Match(sync.Groups["aliases"].Value,
            "\"\\$(?:keyCode|\\{keyCode\\}):(?<command>[a-zA-Z][a-zA-Z0-9_-]*)\"");
        if (!binding.Success) throw new InvalidDataException("BAT 中未找到种子绑定命令");
        command = binding.Groups["command"].Value;
        return bindings;
    }

    public static string BuildCommand(string path, string key)
    {
        var bindings = Read(path, out var command);
        if (!bindings.TryGetValue(key, out var code))
            throw new InvalidDataException($"BAT 中没有 {key} 的键码");
        return $"cmdbind {code.ToString(CultureInfo.InvariantCulture)} {command}";
    }
}

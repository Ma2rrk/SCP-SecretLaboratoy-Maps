using System.Drawing;
using System.Drawing.Text;

namespace SLMapsOverlay;

internal sealed class MapFontManager : IDisposable
{
    public const string DefaultFamilyName = "Microsoft YaHei";
    private PrivateFontCollection? _privateFonts;
    private FontFamily? _family;
    private Font? _coreFont;
    private Font? _lightFont;
    public string FamilyName => _family?.Name ?? DefaultFamilyName;
    public bool Bold { get; set; } = true;

    public bool Load(string familyName, string filePath, out string message)
    {
        PrivateFontCollection? privateFonts = null;
        FontFamily? family = null;
        var success = true;
        message = "字体已切换";
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                privateFonts = new PrivateFontCollection();
                privateFonts.AddFontFile(filePath);
                var families = privateFonts.Families;
                if (families.Length == 0) throw new ArgumentException("字体文件没有可用字体");
                family = families[0];
                foreach (var unused in families.Skip(1)) unused.Dispose();
            }
            else
            {
                family = new FontFamily(string.IsNullOrWhiteSpace(familyName) ? DefaultFamilyName : familyName);
            }
            if (!family.IsStyleAvailable(FontStyle.Bold) && !family.IsStyleAvailable(FontStyle.Regular))
                throw new ArgumentException("字体不支持常规或粗体样式");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
            or System.Runtime.InteropServices.ExternalException)
        {
            family?.Dispose();
            privateFonts?.Dispose();
            privateFonts = null;
            family = FontFamily.GenericSansSerif;
            success = false;
            message = "字体不可用，已回退系统字体";
        }

        ClearCache();
        _family?.Dispose();
        _privateFonts?.Dispose();
        _family = family;
        _privateFonts = privateFonts;
        return success;
    }

    public Font GetFont(bool light, float size)
    {
        var font = light ? _lightFont : _coreFont;
        var family = _family ?? throw new InvalidOperationException("地图字体尚未加载");
        var style = Bold ? FontStyle.Bold : FontStyle.Regular;
        if (!family.IsStyleAvailable(style)) style = Bold ? FontStyle.Regular : FontStyle.Bold;
        if (font is not null && font.Size == size && font.Style == style) return font;
        var replacement = new Font(family, size, style);
        font?.Dispose();
        if (light) _lightFont = replacement;
        else _coreFont = replacement;
        return replacement;
    }

    private void ClearCache()
    {
        _coreFont?.Dispose();
        _lightFont?.Dispose();
        _coreFont = _lightFont = null;
    }

    public void Dispose()
    {
        ClearCache();
        _family?.Dispose();
        _privateFonts?.Dispose();
        _family = null;
        _privateFonts = null;
    }
}

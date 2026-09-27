using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SLMapsOverlay;

internal sealed class OverlaySettings
{
  public int? X { get; set; }
  public int? Y { get; set; }
  public int Width { get; set; }
  public int Height { get; set; }
  public float Scale { get; set; } = 1f;
  public float MapOffsetX { get; set; }
  public float MapOffsetY { get; set; }
  public float CoreScale { get; set; } = 1f;
  public float CoreOffsetX { get; set; }
  public float CoreOffsetY { get; set; }
  public float LczScale { get; set; } = 1f;
  public float LczOffsetX { get; set; }
  public float LczOffsetY { get; set; }
  public double Opacity { get; set; } = 1.0;
  public float MapOpacity { get; set; } = 1.0f;
  public int RoomColorArgb { get; set; } = Color.FromArgb(235, 25, 26, 29).ToArgb();
  public int ConnectionColorArgb { get; set; } = Color.FromArgb(235, 25, 26, 29).ToArgb();
  public string Hotkey { get; set; } = "F8";
  public string SeedHotkey { get; set; } = "PageDown";
  public string ApiSource { get; set; } = "Primary";

  private static string GetUserConfigPath()
  {
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return string.IsNullOrWhiteSpace(localAppData)
      ? Path.Combine(AppContext.BaseDirectory, "overlay.json")
      : Path.Combine(localAppData, "SLMapsOverlay", "overlay.json");
  }

  public static OverlaySettings Load()
  {
    var paths = new[]
    {
      GetUserConfigPath(),
      Path.Combine(AppContext.BaseDirectory, "overlay.json")
    }.Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var path in paths)
    {
      try
      {
        if (File.Exists(path))
        {
          return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path), new JsonSerializerOptions
          {
            PropertyNameCaseInsensitive = true
          }) ?? new OverlaySettings();
        }
      }
      catch (IOException)
      {
      }
      catch (JsonException)
      {
      }
    }

    return new OverlaySettings();
  }

  public static string GetSavePath() => GetUserConfigPath();
}

internal sealed class TransparentPanel : Panel
{
  public TransparentPanel()
  {
    DoubleBuffered = true;
    SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    BackColor = Color.Transparent;
  }

  protected override void OnPaintBackground(PaintEventArgs e)
  {
    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    using var path = new GraphicsPath();
    var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    const int radius = 14;
    path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
    path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
    path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
    path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
    path.CloseFigure();

    using var brush = new SolidBrush(Color.FromArgb(224, 20, 22, 27));
    e.Graphics.FillPath(brush, path);
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    base.OnPaint(e);
    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    using var border = new Pen(Color.FromArgb(150, 88, 94, 104), 1f);
    using var accent = new SolidBrush(Color.FromArgb(232, 89, 12));
    var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    e.Graphics.DrawRoundedRectangle(border, bounds, 14);
    e.Graphics.FillRectangle(accent, 16, 0, Math.Max(24, Width - 32), 2);
  }
}

internal static class GraphicsExtensions
{
  public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
  {
    using var path = new GraphicsPath();
    path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
    path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
    path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
    path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
    path.CloseFigure();
    graphics.DrawPath(pen, path);
  }
}

internal sealed class TransparentTrackBar : TrackBar
{
  public TransparentTrackBar()
  {
    SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    DoubleBuffered = true;
    BackColor = Color.FromArgb(32, 24, 25, 29);
    ForeColor = Color.White;
  }
}

public sealed class MainForm : Form
{
  private static readonly object LogLock = new();
  private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "overlay.log");
  private const uint KeyEventKeyUp = 0x0002;

  [DllImport("user32.dll")]
  private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);

  private static readonly IntPtr HwndTopmost = new(-1);
  private const uint SwpNoMove = 0x0002;
  private const uint SwpNoSize = 0x0001;
  private const uint SwpNoActivate = 0x0010;
  private const int ScaleSliderMin = 100;
  private const int ScaleSliderMax = 1500;
  private const int OffsetXSliderMin = -20000;
  private const int OffsetXSliderMax = 20000;
  private const int OffsetYSliderMin = -10000;
  private const int OffsetYSliderMax = 10000;

    private readonly HttpClient _httpClient = new()
    {
      Timeout = TimeSpan.FromSeconds(15)
    };
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _settingsSaveTimer = new() { Interval = 400 };
    private readonly OverlaySettings _settings = OverlaySettings.Load();
    private readonly TransparentTrackBar _scaleInput = new();
    private readonly TransparentTrackBar _offsetXInput = new();
    private readonly TransparentTrackBar _offsetYInput = new();
    private readonly TransparentTrackBar _lczScaleInput = new();
    private readonly TransparentTrackBar _lczOffsetXInput = new();
    private readonly TransparentTrackBar _lczOffsetYInput = new();
    private readonly TransparentTrackBar _mapOpacityInput = new();
    private readonly Button _roomColorInput = new();
    private readonly Button _connectionColorInput = new();
    private readonly ComboBox _overlayHotkeyInput = new();
    private readonly ComboBox _seedHotkeyInput = new();
    private readonly ComboBox _apiInput = new();
    private RectangleF _coreMapBounds;
    private RectangleF _lczMapBounds;
    private Panel? _controlPanel;
    private bool _dragging;
    private bool _draggingLcz;
    private RoomData? _hoveredRoom;
    private readonly Dictionary<RoomData, RectangleF> _roomHitBoxes = new();
    private Point _dragStart;
    private float _dragStartX;
    private float _dragStartY;
    private DateTime _lastZoomLog = DateTime.MinValue;

    private long _currentSeed;
    private MapApiResponse? _map;
    private bool _isBackupMap;
    private IReadOnlyDictionary<string, string> _roomTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool _isPolling;
    private bool _pollTickRunning;
    private Label? _cacheInfoLabel;
    private int _mapLoadVersion;
    private CancellationTokenSource? _mapLoadCancellation;
    private MapLayout? _mapLayout;

    private sealed class MapLayout
    {
      public MapApiResponse Source { get; init; } = null!;
      public List<RoomData> Rooms { get; init; } = new();
      public List<RoomData> EzRooms { get; init; } = new();
      public List<RoomData> HczRooms { get; init; } = new();
      public List<RoomData> LightRooms { get; init; } = new();
      public List<RoomData> CoreRooms { get; init; } = new();
      public List<RoomData> ConnectedCoreRooms { get; init; } = new();
      public Dictionary<(int X, int Y, int Z), List<(RoomData Room, int Order)>> RoomIndex { get; init; } = new();
      public Dictionary<RoomData, (float X, float Z, List<RoomConnection> Connections)> AlignedPositions { get; init; } = new();
      public float ConnectedMinX { get; init; }
      public float ConnectedMaxX { get; init; }
      public float ConnectedMinZ { get; init; }
      public float ConnectedMaxZ { get; init; }
      public float CoreMinX { get; init; }
      public float CoreMaxX { get; init; }
      public float CoreMinZ { get; init; }
      public float CoreMaxZ { get; init; }
      public float LightMinX { get; init; }
      public float LightMaxX { get; init; }
      public float LightMinZ { get; init; }
      public float LightMaxZ { get; init; }
    }
    private const int HotkeyId = 1001;
    private const int WmHotkey = 0x0312;
    private const int WmMouseWheel = 0x020A;
    private const int WmLButtonDown = 0x0201;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonUp = 0x0202;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainForm()
    {
        Text = "SLMaps Overlay";
        StartPosition = FormStartPosition.Manual;
        var screenBounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var width = _settings.Width > 0 ? _settings.Width : screenBounds.Width;
        var height = _settings.Height > 0 ? _settings.Height : screenBounds.Height;
        var x = _settings.X ?? screenBounds.X;
        var y = _settings.Y ?? screenBounds.Y;
        Bounds = new Rectangle(x, y, width, height);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = Math.Clamp(_settings.Opacity, 0.1, 1.0);
        DoubleBuffered = true;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _controlPanel = BuildControlPanel();
        _controlPanel.Visible = false;
        Controls.Add(_controlPanel);
        MouseClick += HandleMapMouseClick;
        MouseDown += HandleMapMouseDown;
        MouseMove += HandleMapMouseMove;
        MouseUp += HandleMapMouseUp;
        MouseWheel += HandleMapMouseWheel;
        _settingsSaveTimer.Tick += (_, _) =>
        {
          _settingsSaveTimer.Stop();
          SaveControlSettings();
        };
        ClearLog();
        Log("程序启动，覆盖层已创建");

        _timer.Interval = 3000;
        _timer.Tick += async (_, _) =>
        {
          if (_pollTickRunning)
          {
            return;
          }

          _pollTickRunning = true;
          try
          {
            if (IsGameFocused())
            {
              Log("SCP:SL 获得焦点，发送 PageDown");
              SimulateSeedKey();
            }
            else
            {
              Log("SCP:SL 未获得焦点，跳过 PageDown");
            }
            await Task.Delay(250);
            await PollForSeedAsync();
          }
          catch (IOException)
          {
            // The game may be rotating or writing Player.log.
          }
          catch (TaskCanceledException)
          {
            Log("地图请求超时");
          }
          finally
          {
            _pollTickRunning = false;
          }
        };

        Shown += (_, _) => _timer.Start();
        Shown += (_, _) =>
        {
          SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
          RegisterOverlayHotkey();
        };
        FormClosed += (_, _) =>
        {
          _timer.Stop();
          _settingsSaveTimer.Stop();
          Interlocked.Exchange(ref _mapLoadCancellation, null)?.Cancel();
          SaveControlSettings();
          _settingsSaveTimer.Dispose();
          if (IsHandleCreated) UnregisterHotKey(Handle, HotkeyId);
        };
    }

    private async Task PollForSeedAsync()
    {
        if (_isPolling || !SeedMonitor.TryGetLatestSeed(null, out var latestSeed))
        {
            return;
        }

        if (_currentSeed == latestSeed)
        {
            return;
        }

        _isPolling = true;
        try
        {
          await LoadSeedAsync(latestSeed);
        }
        finally
        {
          _isPolling = false;
        }
    }

    private void SimulateSeedKey()
    {
      if (!Enum.TryParse<Keys>(_settings.SeedHotkey, true, out var key) || key == Keys.None)
      {
        key = Keys.PageDown;
      }

      var virtualKey = (byte)((int)key & 0xFF);
      keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
      keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
      Log("PageDown 已发送");
    }

    private static bool IsGameFocused()
    {
      var foregroundWindow = GetForegroundWindow();
      if (foregroundWindow == IntPtr.Zero) return false;

      GetWindowThreadProcessId(foregroundWindow, out var processId);
      try
      {
        using var process = Process.GetProcessById((int)processId);
        var focused = string.Equals(process.ProcessName, "SCPSL", StringComparison.OrdinalIgnoreCase);
        Log($"前台进程={process.ProcessName}, 游戏焦点={focused}");
        return focused;
      }
      catch (ArgumentException)
      {
        return false;
      }
    }

    private async Task LoadSeedAsync(long seed, bool forceRefresh = false)
    {
      var loadVersion = Interlocked.Increment(ref _mapLoadVersion);
      var requestCancellation = new CancellationTokenSource();
      var previousCancellation = Interlocked.Exchange(ref _mapLoadCancellation, requestCancellation);
      previousCancellation?.Cancel();
      var cancellationToken = requestCancellation.Token;
      Log($"发现新 seed={seed}");
        try
        {
            var useBackup = string.Equals(_settings.ApiSource, "Backup", StringComparison.OrdinalIgnoreCase);
            var useLocal = string.Equals(_settings.ApiSource, "Local", StringComparison.OrdinalIgnoreCase);
            if (useLocal)
            {
              var localData = LocalMapGenerator.Generate(seed);
              if (loadVersion != Volatile.Read(ref _mapLoadVersion)) return;
              _map = localData;
              _mapLayout = null;
              _isBackupMap = false;
              _roomTranslations = LoadRoomTranslations();
              _currentSeed = seed;
              Log($"本地地图已生成，seed={seed}，区域数={localData.Zones.Count}");
              Invalidate();
              return;
            }
            var url = useBackup ? $"https://slmaps.com/api/maps/{seed}" : $"https://scpslmaps.fxdyj.com/api.php?seed={seed}";
            string json = string.Empty;
            var cachePath = GetMapCachePath(seed);
            var loadedFromCache = false;
            if (!forceRefresh && File.Exists(cachePath))
            {
              try
              {
                json = await File.ReadAllTextAsync(cachePath, cancellationToken);
                loadedFromCache = IsUsableMapJson(json, seed);
                if (loadedFromCache) Log($"已读取地图缓存，seed={seed}");
                else Log($"地图缓存无效，将重新请求，seed={seed}");
              }
              catch (IOException)
              {
                loadedFromCache = false;
              }
            }

            if (!loadedFromCache)
            {
            for (var attempt = 1; attempt <= (useBackup ? 30 : 1); attempt++)
            {
              json = await _httpClient.GetStringAsync(url, cancellationToken);
              if (!useBackup || !IsPendingMapJob(json)) break;
              Log($"备用 API 仍在生成地图，等待重试 ({attempt}/30)");
              await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            }
            if (useBackup && IsPendingMapJob(json))
            {
              Log("备用 API 等待超时");
              return;
            }
        Log($"API 请求成功，响应长度={json.Length}");
          Log("JSON 原文开始");
          const int maxLoggedJsonLength = 4096;
          Log(json.Length <= maxLoggedJsonLength
            ? $"JSON response: {json}"
            : $"JSON response truncated to {maxLoggedJsonLength} characters (full length {json.Length}): {json[..maxLoggedJsonLength]}");
          Log("JSON 原文结束");
            var responseUsesBackupFormat = useBackup || IsBackupMapResponse(json);
            var data = MapResponseAdapter.Parse(json, seed, responseUsesBackupFormat);

            if (data is null || data.Zones is null || !data.Zones.Values.Any(rooms => rooms is { Count: > 0 }))
            {
              Log("JSON 解析失败：结果为空");
                return;
            }

            Log($"JSON 解析成功：seed={data.Seed}，区域数={data.Zones.Count}");
            if (loadVersion != Volatile.Read(ref _mapLoadVersion)) return;
            if (!loadedFromCache)
            {
              try
              {
                Directory.CreateDirectory(GetMapCacheDirectory());
                await WriteCacheAtomicallyAsync(cachePath, json, cancellationToken);
                UpdateCacheInfo();
                Log($"地图 JSON 已保存到缓存: seed={seed}");
              }
              catch (IOException exception)
              {
                Log($"地图缓存写入失败: {exception.Message}");
              }
            }
            if (loadVersion != Volatile.Read(ref _mapLoadVersion)) return;
            _map = data;
            _mapLayout = null;
            _isBackupMap = responseUsesBackupFormat;
            _roomTranslations = LoadRoomTranslations();
            _currentSeed = seed;
            Log($"地图解析成功，区域数={data.Zones.Count}，开始重绘");
            Invalidate();
        }
          catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          return;
        }
          catch (JsonException exception)
        {
          Log($"JSON 解析失败: {exception.Message}");
        }
          catch (Exception exception)
        {
            Log($"地图请求或解析失败: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
          if (ReferenceEquals(_mapLoadCancellation, requestCancellation))
          {
            Interlocked.CompareExchange(ref _mapLoadCancellation, null, requestCancellation);
          }
          requestCancellation.Dispose();
        }
    }

    private static bool IsUsableMapJson(string json, long seed)
    {
      if (string.IsNullOrWhiteSpace(json) || IsPendingMapJob(json)) return false;

      try
      {
        var data = MapResponseAdapter.Parse(json, seed, IsBackupMapResponse(json));
        return data.Zones is not null && data.Zones.Values.Any(rooms => rooms is { Count: > 0 });
      }
      catch (Exception exception) when (exception is JsonException or InvalidOperationException or NullReferenceException)
      {
        return false;
      }
    }

    private static async Task WriteCacheAtomicallyAsync(string cachePath, string json, CancellationToken cancellationToken)
    {
      var directory = Path.GetDirectoryName(cachePath) ?? AppContext.BaseDirectory;
      var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");
      try
      {
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, cachePath, overwrite: true);
      }
      finally
      {
        try
        {
          if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
      }
    }

    private static bool IsPendingMapJob(string json)
    {
      try
      {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var job = root.TryGetProperty("job", out var jobProperty) ? jobProperty.GetString() : null;
        var status = root.TryGetProperty("status", out var statusProperty) ? statusProperty.GetString() : null;
        return string.Equals(job, "running", StringComparison.OrdinalIgnoreCase)
          && string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase);
      }
      catch (JsonException)
      {
        return false;
      }
    }

    private static bool IsBackupMapResponse(string json)
    {
      try
      {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rooms", out _);
      }
      catch (JsonException)
      {
        return false;
      }
    }

    private static string GetMapCacheDirectory()
    {
      var configDirectory = Path.GetDirectoryName(OverlaySettings.GetSavePath()) ?? AppContext.BaseDirectory;
      return Path.Combine(configDirectory, "map-cache");
    }

    private static string GetMapCachePath(long seed) => Path.Combine(GetMapCacheDirectory(), $"{seed}.json");

    private void ClearDisplayedMap()
    {
      _map = null;
      _mapLayout = null;
      _roomHitBoxes.Clear();
      _coreMapBounds = RectangleF.Empty;
      _lczMapBounds = RectangleF.Empty;
      _hoveredRoom = null;
      Invalidate();
    }


    private void UpdateCacheInfo()
    {
      if (_cacheInfoLabel is null) return;
      try
      {
        var files = Directory.Exists(GetMapCacheDirectory())
          ? Directory.EnumerateFiles(GetMapCacheDirectory(), "*.json")
          : Enumerable.Empty<string>();
        var bytes = files.Sum(path => new FileInfo(path).Length);
        _cacheInfoLabel.Text = $"已缓存 {FormatBytes(bytes)}";
      }
      catch (IOException)
      {
        _cacheInfoLabel.Text = "缓存大小不可用";
      }
    }

    private void ClearMapCache()
    {
      try
      {
        if (Directory.Exists(GetMapCacheDirectory()))
        {
          foreach (var file in Directory.EnumerateFiles(GetMapCacheDirectory(), "*.json")) File.Delete(file);
        }
        UpdateCacheInfo();
        Log("地图缓存已删除");
      }
      catch (IOException exception)
      {
        Log($"地图缓存删除失败: {exception.Message}");
      }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
      >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.##} MB",
      >= 1024 => $"{bytes / 1024d:0.##} KB",
      _ => $"{bytes} B"
    };

      private Panel BuildControlPanel()
      {
        var panel = new TransparentPanel
        {
          Location = new Point(16, 16),
          Size = new Size(370, 780),
          ForeColor = Color.White,
          Padding = new Padding(18),
        };

        var title = new Label
        {
          Text = "设置面板",
          Location = new Point(18, 16),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.White,
          Font = new Font(FontFamily.GenericSansSerif, 13f, FontStyle.Bold),
        };
        panel.Controls.Add(title);

        var subtitle = new Label
        {
          Text = "地图定位与缩放",
          Location = new Point(19, 43),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(170, 190, 196, 204),
          Font = new Font(FontFamily.GenericSansSerif, 8.5f),
        };
        panel.Controls.Add(subtitle);

        _settings.CoreScale = Math.Clamp(_settings.CoreScale, 0.1f, 1.5f);
        _settings.LczScale = Math.Clamp(_settings.LczScale, 0.1f, 1.5f);
        _settings.CoreOffsetX = Math.Clamp(_settings.CoreOffsetX, -2000f, 2000f);
        _settings.CoreOffsetY = Math.Clamp(_settings.CoreOffsetY, -1000f, 1000f);
        _settings.LczOffsetX = Math.Clamp(_settings.LczOffsetX, -2000f, 2000f);
        _settings.LczOffsetY = Math.Clamp(_settings.LczOffsetY, -1000f, 1000f);
        _settings.MapOpacity = Math.Clamp(_settings.MapOpacity, 0.1f, 1f);

        AddSectionLabel(panel, "核心区 / EZ + HCZ", 20, 68);
        AddSectionLabel(panel, "LCZ / 轻收容区", 20, 218);

        AddSliderControl(panel, "核心缩放", _scaleInput, 0, 150, (int)Math.Clamp(_settings.CoreScale * 100, 0, 150), 42);
        AddSliderControl(panel, "核心 X", _offsetXInput, -2500, 2000, (int)Math.Clamp(_settings.CoreOffsetX, -2500, 2000), 76);
        AddSliderControl(panel, "核心 Y", _offsetYInput, -2500, 2000, (int)Math.Clamp(_settings.CoreOffsetY, -2500, 2000), 110);
        AddSliderControl(panel, "LCZ 缩放", _lczScaleInput, 0, 150, (int)Math.Clamp(_settings.LczScale * 100, 0, 150), 144);
        AddSliderControl(panel, "LCZ X", _lczOffsetXInput, -2500, 2000, (int)Math.Clamp(_settings.LczOffsetX, -2500, 2000), 178);
        AddSliderControl(panel, "LCZ Y", _lczOffsetYInput, -2500, 2000, (int)Math.Clamp(_settings.LczOffsetY, -2500, 2000), 212);

        AddSectionLabel(panel, "地图样式", 20, 364);
        AddSliderControl(panel, "地图透明度", _mapOpacityInput, 10, 100,
          (int)Math.Clamp(_settings.MapOpacity * 100, 10, 100), 382);
        AddColorControl(panel, "房间颜色", _roomColorInput, _settings.RoomColorArgb, 428);
        AddColorControl(panel, "连接颜色", _connectionColorInput, _settings.ConnectionColorArgb, 462);

        AddKeyControl(panel, "面板快捷键", _overlayHotkeyInput, _settings.Hotkey, 500, "F8");
        AddKeyControl(panel, "获取种子键", _seedHotkeyInput, _settings.SeedHotkey, 536, "PageDown");
        AddApiControlFixed(panel, _apiInput, _settings.ApiSource, 572);
        _apiInput.SelectedIndexChanged += async (_, _) =>
        {
          if (_currentSeed <= 0) return;

          var apiSource = _apiInput.SelectedIndex == 1 ? "Backup"
            : _apiInput.SelectedIndex == 2 ? "Local" : "Primary";
          if (string.Equals(_settings.ApiSource, apiSource, StringComparison.OrdinalIgnoreCase)) return;

          _settings.ApiSource = apiSource;
          ClearDisplayedMap();
          await LoadSeedAsync(_currentSeed, forceRefresh: true);
        };

        _cacheInfoLabel = new Label
        {
          Location = new Point(20, 730),
          Size = new Size(170, 24),
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(190, 220, 220, 220),
          Font = new Font(FontFamily.GenericSansSerif, 8.5f),
        };
        panel.Controls.Add(_cacheInfoLabel);
        var clearCacheButton = new Button
        {
          Text = "删除缓存 JSON",
          Location = new Point(210, 730),
          Size = new Size(145, 30),
          FlatStyle = FlatStyle.Flat,
          BackColor = Color.FromArgb(70, 74, 82),
          ForeColor = Color.White,
          Cursor = Cursors.Hand,
        };
        clearCacheButton.FlatAppearance.BorderSize = 0;
        clearCacheButton.Click += (_, _) => ClearMapCache();
        panel.Controls.Add(clearCacheButton);
        UpdateCacheInfo();

        var saveButton = new Button
        {
          Text = "保存设置",
          Location = new Point(18, 682),
          Size = new Size(136, 32),
          FlatStyle = FlatStyle.Flat,
          BackColor = Color.FromArgb(232, 89, 12),
          ForeColor = Color.White,
          Cursor = Cursors.Hand,
          Font = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold),
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += (_, _) => SaveControlSettings(closePanel: true, reloadMap: true);
        panel.Controls.Add(saveButton);

        var hint = new Label
        {
          Text = "缩放 0.1-1.5x | 偏移 X±2000 / Y±1000",
          Location = new Point(166, 691),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(190, 220, 220, 220),
          Font = new Font(FontFamily.GenericSansSerif, 7.5f),
        };
        panel.Controls.Add(hint);

        _scaleInput.ValueChanged += (_, _) => ApplyControlSettings();
        _offsetXInput.ValueChanged += (_, _) => ApplyControlSettings();
        _offsetYInput.ValueChanged += (_, _) => ApplyControlSettings();
        _lczScaleInput.ValueChanged += (_, _) => ApplyControlSettings();
        _lczOffsetXInput.ValueChanged += (_, _) => ApplyControlSettings();
        _lczOffsetYInput.ValueChanged += (_, _) => ApplyControlSettings();
        _mapOpacityInput.ValueChanged += (_, _) => ApplyControlSettings();
        return panel;
      }

      private void AddSliderControl(Control parent, string labelText, TrackBar input, int minimum, int maximum, int value, int y)
      {
        var isScale = input == _scaleInput || input == _lczScaleInput;
        var isOpacity = input == _mapOpacityInput;
        y = input == _scaleInput ? 82
          : input == _offsetXInput ? 124
          : input == _offsetYInput ? 166
          : input == _lczScaleInput ? 232
          : input == _lczOffsetXInput ? 274
          : input == _lczOffsetYInput ? 316
          : 382;
        minimum = isScale ? ScaleSliderMin
          : isOpacity ? 100
          : input == _offsetYInput || input == _lczOffsetYInput ? OffsetYSliderMin : OffsetXSliderMin;
        maximum = isScale ? ScaleSliderMax
          : isOpacity ? 1000
          : input == _offsetYInput || input == _lczOffsetYInput ? OffsetYSliderMax : OffsetXSliderMax;
        value *= 10;
        var label = new Label
        {
          Text = labelText,
          Location = new Point(20, y + 7),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(220, 226, 230, 236),
          Font = new Font(FontFamily.GenericSansSerif, 8.5f),
        };

        input.Location = new Point(106, y);
        input.Size = new Size(188, 38);
        input.Minimum = minimum;
        input.Maximum = maximum;
        input.Value = value;
        input.TickStyle = TickStyle.None;
        input.LargeChange = 1;
        input.SmallChange = 1;
        input.BackColor = Color.Transparent;
        input.ForeColor = Color.White;
        var valueLabel = new Label
        {
          Location = new Point(302, y + 7),
          Size = new Size(52, 20),
          TextAlign = ContentAlignment.MiddleRight,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(255, 180, 133),
          Font = new Font(FontFamily.GenericMonospace, 8f, FontStyle.Bold),
        };
        valueLabel.Text = FormatSliderValue(input.Value, isScale, isOpacity);
        input.ValueChanged += (_, _) => valueLabel.Text = FormatSliderValue(input.Value, isScale, isOpacity);
        parent.Controls.Add(label);
        parent.Controls.Add(input);
        parent.Controls.Add(valueLabel);
      }

      private void AddColorControl(Control parent, string labelText, Button input, int argb, int y)
      {
        var label = new Label
        {
          Text = labelText,
          Location = new Point(20, y + 5),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(220, 226, 230, 236),
          Font = new Font(FontFamily.GenericSansSerif, 8.5f),
        };
        input.Location = new Point(146, y);
        input.Size = new Size(190, 26);
        input.FlatStyle = FlatStyle.Flat;
        input.FlatAppearance.BorderColor = Color.FromArgb(150, 190, 196, 204);
        input.FlatAppearance.BorderSize = 1;
        input.BackColor = Color.FromArgb(argb);
        input.ForeColor = Color.White;
        input.Cursor = Cursors.Hand;
        input.Text = string.Empty;
        input.AccessibleName = labelText;
        input.Click += (_, _) =>
        {
          using var dialog = new ColorDialog
          {
            Color = input.BackColor,
            FullOpen = true,
            AnyColor = true,
          };
          if (dialog.ShowDialog(this) != DialogResult.OK) return;
          input.BackColor = dialog.Color;
          ApplyControlSettings();
          _settingsSaveTimer.Stop();
          _settingsSaveTimer.Start();
        };
        parent.Controls.Add(label);
        parent.Controls.Add(input);
      }

      private static void AddKeyControl(Control parent, string labelText, ComboBox input, string selectedKey, int y, string fallbackKey)
      {
        var label = new Label
        {
          Text = labelText,
          Location = new Point(20, y + 5),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(220, 226, 230, 236),
          Font = new Font(FontFamily.GenericSansSerif, 8.5f),
        };

        input.DropDownStyle = ComboBoxStyle.DropDownList;
        input.Location = new Point(146, y);
        input.Size = new Size(150, 26);
        input.BackColor = Color.FromArgb(38, 42, 49);
        input.ForeColor = Color.White;
        input.FlatStyle = FlatStyle.Flat;
        input.Items.Clear();
        input.Items.AddRange(new object[]
        {
          "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
          "Home", "End", "Insert", "Delete", "PageUp", "PageDown"
        });
        var index = input.Items.IndexOf(selectedKey);
        input.SelectedIndex = index >= 0 ? index : input.Items.IndexOf(fallbackKey);

        parent.Controls.Add(label);
        parent.Controls.Add(input);
      }

      /* Legacy API control removed; AddApiControlFixed is the single implementation. */
      /*
      private static void AddApiControl(Control parent, ComboBox input, string selectedApi, int y)
      {
        var label = new Label
        {
          Text = "地图 API",
          Location = new Point(20, y + 5),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(220, 226, 230, 236),
          Font = new Font(FontFamily.GenericSansSerif, 8.5f),
        };
        input.DropDownStyle = ComboBoxStyle.DropDownList;
        input.Location = new Point(146, y);
        input.Size = new Size(190, 26);
        input.BackColor = Color.FromArgb(38, 42, 49);
        input.ForeColor = Color.White;
        input.FlatStyle = FlatStyle.Flat;
         input.Items.Clear();
         input.Items.AddRange(new object[] { "Primary API", "Backup API (slmaps.com)", "Local generation" });
         input.SelectedIndex = string.Equals(selectedApi, "Backup", StringComparison.OrdinalIgnoreCase) ? 1
           : string.Equals(selectedApi, "Local", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
         parent.Controls.Add(label);
         parent.Controls.Add(input);
         return;
        input.Items.AddRange(new object[] { "主 API", "备用 API (slmaps.com)" });
         input.Items.Clear();
         input.Items.AddRange(new object[] { "Primary API", "Backup API (slmaps.com)", "Local generation" });
         input.SelectedIndex = string.Equals(selectedApi, "Backup", StringComparison.OrdinalIgnoreCase) ? 1
           : string.Equals(selectedApi, "Local", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
         parent.Controls.Add(label);
         parent.Controls.Add(input);
      }
      */

      private static void AddApiControlFixed(Control parent, ComboBox input, string selectedApi, int y)
      {
        var label = new Label
        {
          Text = "地图来源",
          Location = new Point(20, y + 5),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(220, 226, 230, 236),
          Font = new Font(FontFamily.GenericSansSerif, 8.5f),
        };

        input.DropDownStyle = ComboBoxStyle.DropDownList;
        input.Location = new Point(146, y);
        input.Size = new Size(190, 26);
        input.BackColor = Color.FromArgb(38, 42, 49);
        input.ForeColor = Color.White;
        input.FlatStyle = FlatStyle.Flat;
        input.Items.Clear();
        input.Items.AddRange(new object[] { "Primary API", "Backup API (slmaps.com)", "Local generation" });
        input.SelectedIndex = string.Equals(selectedApi, "Backup", StringComparison.OrdinalIgnoreCase) ? 1
          : string.Equals(selectedApi, "Local", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        parent.Controls.Add(label);
        parent.Controls.Add(input);
      }

      private static void AddSectionLabel(Control parent, string text, int x, int y)
      {
        var label = new Label
        {
          Text = text,
          Location = new Point(x, y),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(232, 89, 12),
          Font = new Font(FontFamily.GenericSansSerif, 8f, FontStyle.Bold),
        };
        parent.Controls.Add(label);
      }

      private static string FormatSliderValue(int value, bool isScale, bool isOpacity)
      {
        return isScale ? $"{value / 1000f:0.000}x"
          : isOpacity ? $"{value / 10f:0}%"
          : $"{value / 10f:0.0}";
      }

      private void ApplyControlSettings()
      {
        _settings.CoreScale = _scaleInput.Value / 1000f;
        _settings.CoreOffsetX = _offsetXInput.Value / 10f;
        _settings.CoreOffsetY = _offsetYInput.Value / 10f;
        _settings.LczScale = _lczScaleInput.Value / 1000f;
        _settings.LczOffsetX = _lczOffsetXInput.Value / 10f;
        _settings.LczOffsetY = _lczOffsetYInput.Value / 10f;
        _settings.MapOpacity = _mapOpacityInput.Value / 1000f;
        _settings.RoomColorArgb = _roomColorInput.BackColor.ToArgb();
        _settings.ConnectionColorArgb = _connectionColorInput.BackColor.ToArgb();
        Invalidate();
      }

      private void SaveControlSettings(bool closePanel = false, bool reloadMap = false)
      {
        _settingsSaveTimer.Stop();
        ApplyControlSettings();
        _settings.Hotkey = _overlayHotkeyInput.SelectedItem?.ToString() ?? "F8";
        _settings.SeedHotkey = _seedHotkeyInput.SelectedItem?.ToString() ?? "PageDown";
        _settings.ApiSource = _apiInput.SelectedIndex == 1 ? "Backup"
          : _apiInput.SelectedIndex == 2 ? "Local" : "Primary";
        try
        {
          var path = OverlaySettings.GetSavePath();
          var directory = Path.GetDirectoryName(path);
          if (!string.IsNullOrWhiteSpace(directory))
          {
            Directory.CreateDirectory(directory);
          }
          File.WriteAllText(path, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
          if (IsHandleCreated && !IsDisposed)
          {
            UnregisterHotKey(Handle, HotkeyId);
            RegisterOverlayHotkey();
          }
          Log("配置已保存");
          if (closePanel && _controlPanel is not null)
          {
            _controlPanel.Visible = false;
          }
          if (reloadMap && _currentSeed > 0)
          {
            _ = LoadSeedAsync(_currentSeed, forceRefresh: true);
          }
        }
        catch (IOException exception)
        {
          Log($"配置保存失败: {exception.Message}");
        }
      }

      private void HandleMapMouseDown(object? sender, MouseEventArgs e)
      {
        if (e.Button != MouseButtons.Left || _controlPanel?.Bounds.Contains(e.Location) == true) return;
        var region = GetMapRegionAt(e.Location);
        if (region < 0) return;
        _dragging = true;
        _dragStart = e.Location;
        _draggingLcz = region == 1;
        _dragStartX = _draggingLcz ? _settings.LczOffsetX : _settings.CoreOffsetX;
        _dragStartY = _draggingLcz ? _settings.LczOffsetY : _settings.CoreOffsetY;
        Log($"开始拖动 {(_draggingLcz ? "LCZ" : "EZ/HCZ")}");
        Capture = true;
      }

      private void HandleMapMouseMove(object? sender, MouseEventArgs e)
      {
        if (!_dragging)
        {
          _hoveredRoom = FindRoomAt(e.Location);
          Cursor = _hoveredRoom is null ? Cursors.Default : Cursors.Hand;
          return;
        }

        var dx = e.X - _dragStart.X;
        var dy = e.Y - _dragStart.Y;
        if (_draggingLcz)
        {
          _settings.LczOffsetX = Math.Clamp(_dragStartX + dx, -2000f, 2000f);
          _settings.LczOffsetY = Math.Clamp(_dragStartY + dy, -1000f, 1000f);
        }
        else
        {
          _settings.CoreOffsetX = Math.Clamp(_dragStartX + dx, -2000f, 2000f);
          _settings.CoreOffsetY = Math.Clamp(_dragStartY + dy, -1000f, 1000f);
        }
        UpdateControlInputs();
        Invalidate();
      }

      private RoomData? FindRoomAt(Point point)
      {
        foreach (var entry in _roomHitBoxes)
        {
          if (entry.Value.Contains(point))
          {
            return entry.Key;
          }
        }
        return null;
      }

      private void HandleMapMouseUp(object? sender, MouseEventArgs e)
      {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
        SaveControlSettings();
        Log($"结束拖动 {(_draggingLcz ? "LCZ" : "EZ/HCZ")}");
      }

      private void HandleMapMouseClick(object? sender, MouseEventArgs e)
      {
        var room = FindRoomAt(e.Location);
        if (room is not null)
        {
          Log($"点击房间: {room.Short ?? room.Code ?? room.Name}");
        }
      }

      private void HandleMapMouseWheel(object? sender, MouseEventArgs e)
      {
        var region = GetMapRegionAt(e.Location);
        if (region < 0) return;
        var isLcz = region == 1;
        var factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
        if (isLcz)
        {
          _settings.LczScale = Math.Clamp(_settings.LczScale * factor, 0.1f, 1.5f);
          _lczScaleInput.Value = (int)Math.Clamp(_settings.LczScale * 1000, ScaleSliderMin, ScaleSliderMax);
        }
        else
        {
          _settings.CoreScale = Math.Clamp(_settings.CoreScale * factor, 0.1f, 1.5f);
          _scaleInput.Value = (int)Math.Clamp(_settings.CoreScale * 1000, ScaleSliderMin, ScaleSliderMax);
        }
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
        Invalidate();
        if (DateTime.UtcNow - _lastZoomLog >= TimeSpan.FromSeconds(1))
        {
          _lastZoomLog = DateTime.UtcNow;
          Log($"滚轮缩放 {(isLcz ? "LCZ" : "EZ/HCZ")}，方向={e.Delta}，比例={(isLcz ? _settings.LczScale : _settings.CoreScale):F2}");
        }
      }

      private int GetMapRegionAt(Point point)
      {
        foreach (var entry in _roomHitBoxes)
        {
          if (entry.Value.Contains(point))
          {
            return IsLightContainmentRoom(entry.Key) ? 1 : 0;
          }
        }

        if (_lczMapBounds.Contains(point)) return 1;
        if (_coreMapBounds.Contains(point)) return 0;
        return -1;
      }

      private void UpdateControlInputs()
      {
        _scaleInput.Value = (int)Math.Clamp(_settings.CoreScale * 1000, ScaleSliderMin, ScaleSliderMax);
        _offsetXInput.Value = (int)Math.Clamp(_settings.CoreOffsetX * 10, OffsetXSliderMin, OffsetXSliderMax);
        _offsetYInput.Value = (int)Math.Clamp(_settings.CoreOffsetY * 10, OffsetYSliderMin, OffsetYSliderMax);
        _lczScaleInput.Value = (int)Math.Clamp(_settings.LczScale * 1000, ScaleSliderMin, ScaleSliderMax);
        _lczOffsetXInput.Value = (int)Math.Clamp(_settings.LczOffsetX * 10, OffsetXSliderMin, OffsetXSliderMax);
        _lczOffsetYInput.Value = (int)Math.Clamp(_settings.LczOffsetY * 10, OffsetYSliderMin, OffsetYSliderMax);
      }

    internal static void LogMessage(string message) => Log(message);

    private static void Log(string message)
      {
        try
        {
          lock (LogLock)
          {
            File.AppendAllText(LogPath, message + Environment.NewLine);
          }
        }
        catch (IOException)
        {
        }
      }

      private static void ClearLog()
      {
        try
        {
          lock (LogLock)
          {
            File.WriteAllText(LogPath, string.Empty);
          }
        }
        catch (IOException)
        {
        }
      }

      private void RegisterOverlayHotkey()
      {
        if (!Enum.TryParse<Keys>(_settings.Hotkey, true, out var key) || key == Keys.None)
        {
          key = Keys.F8;
        }

        RegisterHotKey(Handle, HotkeyId, 0, (uint)key);
      }

      protected override void WndProc(ref Message message)
      {
        if (message.Msg == WmLButtonDown || message.Msg == WmMouseMove || message.Msg == WmLButtonUp)
        {
          var x = (short)(long)message.LParam;
          var y = (short)((long)message.LParam >> 16);
          var buttons = message.Msg == WmLButtonDown || message.Msg == WmMouseMove
            ? MouseButtons.Left
            : MouseButtons.None;
          var mouseEvent = new MouseEventArgs(buttons, 0, x, y, 0);
          if (message.Msg == WmLButtonDown) HandleMapMouseDown(this, mouseEvent);
          else if (message.Msg == WmMouseMove) HandleMapMouseMove(this, mouseEvent);
          else HandleMapMouseUp(this, new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
        }

        if (message.Msg == WmMouseWheel)
        {
          var point = PointToClient(Cursor.Position);
          var delta = (short)((long)message.WParam >> 16);
          HandleMapMouseWheel(this, new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, delta));
        }

        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId && _controlPanel is not null)
        {
          _controlPanel.Visible = !_controlPanel.Visible;
        }

        base.WndProc(ref message);
      }

      protected override void OnPaint(PaintEventArgs e)
      {
        base.OnPaint(e);
        if (_map is null)
        {
          return;
        }

        if (_mapLayout is null || !ReferenceEquals(_mapLayout.Source, _map))
        {
          _mapLayout = BuildMapLayout(_map);
        }

        var layout = _mapLayout;
        var rooms = layout.Rooms;
        if (rooms.Count == 0)
        {
          return;
        }

        var ezRooms = layout.EzRooms;
        var hczRooms = layout.HczRooms;
        var lightRooms = layout.LightRooms;
        var coreRooms = layout.CoreRooms;
        var connectedCoreRooms = layout.ConnectedCoreRooms;
        var alignedPositions = layout.AlignedPositions;
        var padding = 60f;
        var coreMinX = layout.CoreMinX;
        var coreMaxX = layout.CoreMaxX;
        var coreMinZ = layout.CoreMinZ;
        var coreMaxZ = layout.CoreMaxZ;
        var connectedMinX = layout.ConnectedMinX;
        var connectedMaxX = layout.ConnectedMaxX;
        var connectedMinZ = layout.ConnectedMinZ;
        var connectedMaxZ = layout.ConnectedMaxZ;
        var lightMinX = layout.LightMinX;
        var lightMaxX = layout.LightMaxX;
        var lightMinZ = layout.LightMinZ;
        var lightMaxZ = layout.LightMaxZ;
        var coreSpan = Math.Max(1f, Math.Max(coreMaxX - coreMinX, coreMaxZ - coreMinZ));
        var lightSpan = Math.Max(1f, Math.Max(lightMaxX - lightMinX, lightMaxZ - lightMinZ));
        var coreScale = Math.Clamp(Math.Min((ClientSize.Width / 2f - padding * 2) / coreSpan, (ClientSize.Height - padding * 2) / coreSpan) * _settings.CoreScale, 1f, 100f);
        var lczScale = Math.Clamp(Math.Min((ClientSize.Width / 2f - padding * 2) / lightSpan, (ClientSize.Height - padding * 2) / lightSpan) * _settings.LczScale, 1f, 100f);
        const float verticalCompression = 0.78f;

        PointF ToPoint(RoomData room)
        {
          var position = alignedPositions[room];
          var isLcz = IsLightContainmentRoom(room);
          var isEz = IsEntranceRoom(room);
          var isHcz = IsHeavyContainmentRoom(room);
          var activeScale = isLcz ? lczScale : coreScale;
          var offsetX = isLcz ? _settings.LczOffsetX : _settings.CoreOffsetX;
          var offsetY = isLcz ? _settings.LczOffsetY : _settings.CoreOffsetY;
          var baseX = isLcz ? ClientSize.Width * .74f : ClientSize.Width * .34f;
          var baseY = padding + (ClientSize.Height - padding * 2) / 2f;
          var localX = isLcz ? position.X - lightMinX : (isEz || isHcz ? position.X - connectedMinX : position.X - coreMinX);
          var localZ = isLcz ? position.Z - lightMinZ : (isEz || isHcz ? position.Z - connectedMinZ : position.Z - coreMinZ);
          var regionWidth = isLcz ? lightMaxX - lightMinX : (isEz || isHcz ? connectedMaxX - connectedMinX : coreMaxX - coreMinX);
          var regionHeight = isLcz ? lightMaxZ - lightMinZ : (isEz || isHcz ? connectedMaxZ - connectedMinZ : coreMaxZ - coreMinZ);
          return new PointF(
            baseX + (localX - regionWidth / 2f) * activeScale + offsetX,
            baseY + (regionHeight / 2f - localZ) * activeScale * verticalCompression + offsetY);
        }

        var mapOpacity = Math.Clamp(_settings.MapOpacity, 0.1f, 1f);
        var roomColor = ApplyMapOpacity(GetConfiguredColor(_settings.RoomColorArgb, Color.FromArgb(235, 25, 26, 29)), mapOpacity);
        var connectionColor = ApplyMapOpacity(GetConfiguredColor(_settings.ConnectionColorArgb, Color.FromArgb(235, 25, 26, 29)), mapOpacity);
        using var borderPen = new Pen(ApplyMapOpacity(Color.FromArgb(210, 42, 42, 42), mapOpacity), 1.2f);
        using var corridorBrush = new SolidBrush(roomColor);
        using var labelBrush = new SolidBrush(ApplyMapOpacity(Color.White, mapOpacity));
        using var coreConnectorPen = CreateConnectorPen(connectionColor, 0.44f * 15f * coreScale);
        using var lightConnectorPen = CreateConnectorPen(connectionColor, 0.44f * 15f * lczScale);
        using var labelFormat = new StringFormat
        {
          Alignment = StringAlignment.Center,
          LineAlignment = StringAlignment.Center,
          Trimming = StringTrimming.EllipsisCharacter,
        };
        using var coreLabelFont = new Font(FontFamily.GenericSansSerif, Math.Clamp(coreScale * 1.7f, 7f, 14f), FontStyle.Bold);
        using var lightLabelFont = new Font(FontFamily.GenericSansSerif, Math.Clamp(lczScale * 1.7f, 7f, 14f), FontStyle.Bold);
        var roomOrder = rooms
          .Select((room, index) => (room, index))
          .ToDictionary(item => item.room, item => item.index);

        // Draw the connectors first so each room body hides the line ends and the map reads as one continuous corridor network.
        foreach (var room in rooms)
        {
          var point = ToPoint(room);
          var connectorPen = IsLightContainmentRoom(room) ? lightConnectorPen : coreConnectorPen;
          var connections = alignedPositions[room].Connections;
          foreach (var connection in connections)
          {
            var target = FindConnectedRoom(room, connection, layout.RoomIndex);
            if (target is null || roomOrder[room] >= roomOrder[target])
            {
              continue;
            }

            var targetPoint = ToPoint(target);
            e.Graphics.DrawLine(connectorPen, point, targetPoint);
          }
        }

        _roomHitBoxes.Clear();
        _coreMapBounds = RectangleF.Empty;
        _lczMapBounds = RectangleF.Empty;
        foreach (var room in rooms)
        {
          var point = ToPoint(room);
          var activeScale = IsLightContainmentRoom(room) ? lczScale : coreScale;
          var cell = 15f * activeScale;

          var widthRoom = 0.88f * cell;
          var heightRoom = 0.62f * cell;
          var hitBox = new RectangleF(point.X - widthRoom / 2, point.Y - heightRoom / 2, widthRoom, heightRoom);
          _roomHitBoxes[room] = hitBox;
          var regionBounds = hitBox;
          regionBounds.Inflate(cell * 0.55f, cell * 0.55f);
          if (IsLightContainmentRoom(room))
          {
            _lczMapBounds = _lczMapBounds.IsEmpty ? regionBounds : RectangleF.Union(_lczMapBounds, regionBounds);
          }
          else
          {
            _coreMapBounds = _coreMapBounds.IsEmpty ? regionBounds : RectangleF.Union(_coreMapBounds, regionBounds);
          }
          DrawRoomBody(e.Graphics, room, point, widthRoom, heightRoom, corridorBrush, borderPen);

          var label = GetRoomDisplayName(room);
          if (!string.IsNullOrWhiteSpace(label) && !label.Contains("Unnamed", StringComparison.OrdinalIgnoreCase))
          {
            var roomLabelFont = IsLightContainmentRoom(room) ? lightLabelFont : coreLabelFont;
            var labelRectangle = new RectangleF(point.X - widthRoom / 2, point.Y - heightRoom / 2, widthRoom, heightRoom);
            e.Graphics.DrawString(label, roomLabelFont, labelBrush, labelRectangle, labelFormat);
          }
        }

      }

      private static MapLayout BuildMapLayout(MapApiResponse map)
      {
        var rooms = map.Zones
          .SelectMany(zone => zone.Value.Select(room =>
          {
            room.Zone = ResolveZone(room, zone.Key);
            return room;
          }))
          .ToList();
        var ezRooms = rooms.Where(IsEntranceRoom).ToList();
        var hczRooms = rooms.Where(IsHeavyContainmentRoom).ToList();
        var lightRooms = rooms.Where(IsLightContainmentRoom).ToList();
        var coreRooms = rooms.Where(room => !IsLightContainmentRoom(room)).ToList();
        var connectedCoreRooms = coreRooms.Where(room => IsEntranceRoom(room) || IsHeavyContainmentRoom(room)).ToList();
        var positions = new Dictionary<RoomData, (float X, float Z, List<RoomConnection> Connections)>();

        foreach (var room in coreRooms)
        {
          positions[room] = (-room.X, -room.Z, room.Conn.Select(connection => new RoomConnection
          {
            Dx = -connection.Dx,
            Dz = -connection.Dz,
            TargetX = connection.TargetX ?? room.X + connection.Dx * 15f,
            TargetZ = connection.TargetZ ?? room.Z + connection.Dz * 15f,
          }).ToList());
        }

        foreach (var room in lightRooms)
        {
          positions[room] = (room.X, room.Z, room.Conn.Select(connection => new RoomConnection
          {
            Dx = connection.Dx,
            Dz = connection.Dz,
            TargetX = connection.TargetX ?? room.X + connection.Dx * 15f,
            TargetZ = connection.TargetZ ?? room.Z + connection.Dz * 15f,
          }).ToList());
        }

        var aligned = AlignConnectedRooms(rooms, positions);
        var roomIndex = new Dictionary<(int X, int Y, int Z), List<(RoomData Room, int Order)>>();
        for (var index = 0; index < rooms.Count; index++)
        {
          var room = rooms[index];
          var key = (BucketCoordinate(room.X), BucketCoordinate(room.Y), BucketCoordinate(room.Z));
          if (!roomIndex.TryGetValue(key, out var bucket)) roomIndex[key] = bucket = new();
          bucket.Add((room, index));
        }
        static float MinOrZero(IReadOnlyList<RoomData> source, Dictionary<RoomData, (float X, float Z, List<RoomConnection> Connections)> values, bool x)
          => source.Count == 0 ? 0f : (x ? source.Min(room => values[room].X) : source.Min(room => values[room].Z));
        static float MaxOrZero(IReadOnlyList<RoomData> source, Dictionary<RoomData, (float X, float Z, List<RoomConnection> Connections)> values, bool x)
          => source.Count == 0 ? 0f : (x ? source.Max(room => values[room].X) : source.Max(room => values[room].Z));

        return new MapLayout
        {
          Source = map,
          Rooms = rooms,
          EzRooms = ezRooms,
          HczRooms = hczRooms,
          LightRooms = lightRooms,
          CoreRooms = coreRooms,
          ConnectedCoreRooms = connectedCoreRooms,
          RoomIndex = roomIndex,
          AlignedPositions = aligned,
          CoreMinX = MinOrZero(coreRooms, aligned, true),
          CoreMaxX = MaxOrZero(coreRooms, aligned, true),
          CoreMinZ = MinOrZero(coreRooms, aligned, false),
          CoreMaxZ = MaxOrZero(coreRooms, aligned, false),
          ConnectedMinX = MinOrZero(connectedCoreRooms, aligned, true),
          ConnectedMaxX = MaxOrZero(connectedCoreRooms, aligned, true),
          ConnectedMinZ = MinOrZero(connectedCoreRooms, aligned, false),
          ConnectedMaxZ = MaxOrZero(connectedCoreRooms, aligned, false),
          LightMinX = MinOrZero(lightRooms, aligned, true),
          LightMaxX = MaxOrZero(lightRooms, aligned, true),
          LightMinZ = MinOrZero(lightRooms, aligned, false),
          LightMaxZ = MaxOrZero(lightRooms, aligned, false),
        };
      }

      private static int BucketCoordinate(float coordinate) => (int)Math.Floor(coordinate * 100f);

      private static RoomData? FindConnectedRoom(RoomData room, RoomConnection connection,
        Dictionary<(int X, int Y, int Z), List<(RoomData Room, int Order)>> roomIndex)
      {
        var targetX = connection.TargetX ?? (room.X + connection.Dx * 15f);
        var targetZ = connection.TargetZ ?? (room.Z + connection.Dz * 15f);
        var bucketX = BucketCoordinate(targetX);
        var bucketY = BucketCoordinate(room.Y);
        var bucketZ = BucketCoordinate(targetZ);
        RoomData? match = null;
        var matchOrder = int.MaxValue;
        for (var x = bucketX - 1; x <= bucketX + 1; x++)
          for (var y = bucketY - 1; y <= bucketY + 1; y++)
            for (var z = bucketZ - 1; z <= bucketZ + 1; z++)
            {
              if (!roomIndex.TryGetValue((x, y, z), out var bucket)) continue;
              foreach (var (candidate, order) in bucket)
              {
                if (order >= matchOrder || ReferenceEquals(candidate, room)
                    || Math.Abs(candidate.X - targetX) >= 0.01f
                    || Math.Abs(candidate.Y - room.Y) >= 0.01f
                    || Math.Abs(candidate.Z - targetZ) >= 0.01f) continue;
                match = candidate;
                matchOrder = order;
              }
            }
        return match;
      }

      private static Pen CreateConnectorPen(Color color, float width) => new(color, width)
      {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
      };

      private static Color GetConfiguredColor(int argb, Color fallback)
      {
        return argb == 0 ? fallback : Color.FromArgb(argb);
      }

      private static Color ApplyMapOpacity(Color color, float opacity)
      {
        var alpha = (int)Math.Round(color.A * Math.Clamp(opacity, 0.1f, 1f));
        return Color.FromArgb(Math.Clamp(alpha, 1, 255), color.R, color.G, color.B);
      }

      private static void DrawRoomBody(Graphics graphics, RoomData room, PointF center, float width, float height, Brush fill, Pen border)
      {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var left = center.X - width / 2f;
        var top = center.Y - height / 2f;
        switch (room.Glyph?.ToLowerInvariant())
        {
          case "circle":
            graphics.FillEllipse(fill, left, top, width, height);
            graphics.DrawEllipse(border, left, top, width, height);
            break;
          case "octagon":
            AddPolygon(path, center, width, height, 8);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            break;
          case "hex":
            AddPolygon(path, center, width, height, 6);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            break;
          case "dead":
            path.AddPolygon(new[]
            {
              new PointF(left + width * .16f, top), new PointF(left + width * .84f, top),
              new PointF(left + width, top + height), new PointF(left, top + height)
            });
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            break;
          default:
            graphics.FillRectangle(fill, left, top, width, height);
            graphics.DrawRectangle(border, left, top, width, height);
            break;
        }
      }

      private static void AddPolygon(System.Drawing.Drawing2D.GraphicsPath path, PointF center, float width, float height, int sides)
      {
        var points = new PointF[sides];
        for (var index = 0; index < sides; index++)
        {
          var angle = -Math.PI / 2 + index * Math.PI * 2 / sides;
          points[index] = new PointF(
            center.X + (float)Math.Cos(angle) * width / 2f,
            center.Y + (float)Math.Sin(angle) * height / 2f);
        }
        path.AddPolygon(points);
      }

      private static Dictionary<RoomData, (float X, float Z, List<RoomConnection> Connections)> AlignConnectedRooms(
        IEnumerable<RoomData> rooms,
        Dictionary<RoomData, (float X, float Z, List<RoomConnection> Connections)> positions)
      {
        var aligned = new Dictionary<RoomData, (float X, float Z, List<RoomConnection> Connections)>();
        foreach (var room in rooms)
        {
          if (!positions.TryGetValue(room, out var value))
          {
            continue;
          }

          var connections = value.Connections;
          if (connections.Count == 0)
          {
            aligned[room] = value;
            continue;
          }

          var avgDx = (float)connections.Average(connection => connection.Dx);
          var avgDz = (float)connections.Average(connection => connection.Dz);
          aligned[room] = (value.X + avgDx * 0.08f, value.Z + avgDz * 0.08f, connections);
        }
        return aligned;
      }

      private static string ResolveZone(RoomData room, string fallbackZone)
      {
        if (!string.IsNullOrWhiteSpace(room.Zone))
        {
          return room.Zone;
        }

        if (!string.IsNullOrWhiteSpace(fallbackZone))
        {
          return fallbackZone;
        }

        if (string.Equals(room.Group, "light", StringComparison.OrdinalIgnoreCase)
          || room.Name.Contains("Lcz", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("轻收容", StringComparison.OrdinalIgnoreCase))
        {
          return "LightContainment";
        }

        if (string.Equals(room.Group, "core", StringComparison.OrdinalIgnoreCase)
          || room.Name.Contains("Hcz", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("重收容", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("SCP-", StringComparison.OrdinalIgnoreCase))
        {
          return "HeavyContainment";
        }

        if (string.Equals(room.Group, "entrance", StringComparison.OrdinalIgnoreCase)
          || room.Name.Contains("Ez", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("办公", StringComparison.OrdinalIgnoreCase))
        {
          return "Entrance";
        }

        return string.IsNullOrWhiteSpace(room.Zone) ? "HeavyContainment" : room.Zone;
      }

      private static bool IsLightContainmentRoom(RoomData room)
      {
        return string.Equals(room.Zone, "LightContainment", StringComparison.OrdinalIgnoreCase)
          || string.Equals(room.Group, "light", StringComparison.OrdinalIgnoreCase)
          || room.Zone.Contains("Light", StringComparison.OrdinalIgnoreCase)
          || room.Name.Contains("Lcz", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("轻收容", StringComparison.OrdinalIgnoreCase);
      }

      private static bool IsEntranceRoom(RoomData room)
      {
        return string.Equals(room.Zone, "Entrance", StringComparison.OrdinalIgnoreCase)
          || string.Equals(room.Group, "entrance", StringComparison.OrdinalIgnoreCase)
          || room.Zone.Contains("Entrance", StringComparison.OrdinalIgnoreCase)
          || room.Name.Contains("Ez", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("办公", StringComparison.OrdinalIgnoreCase);
      }

      private static bool IsHeavyContainmentRoom(RoomData room)
      {
        return string.Equals(room.Zone, "HeavyContainment", StringComparison.OrdinalIgnoreCase)
          || string.Equals(room.Group, "heavy", StringComparison.OrdinalIgnoreCase)
          || string.Equals(room.Group, "core", StringComparison.OrdinalIgnoreCase)
          || room.Zone.Contains("Heavy", StringComparison.OrdinalIgnoreCase)
          || room.Name.Contains("Hcz", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("重收容", StringComparison.OrdinalIgnoreCase)
          || room.Label.Contains("SCP-", StringComparison.OrdinalIgnoreCase);
      }

      private static bool IsUnnamedRoomName(string? name)
      {
        if (string.IsNullOrWhiteSpace(name))
        {
          return true;
        }

        var value = name.Trim();
        return value.Equals("Unnamed", StringComparison.OrdinalIgnoreCase)
          || value.StartsWith("Unnamed", StringComparison.OrdinalIgnoreCase)
          || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
          || value.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);
      }

      private static bool IsHiddenRoomName(string? name)
      {
        if (string.IsNullOrWhiteSpace(name))
        {
          return false;
        }

        var trimmed = name.Trim();
        return trimmed.Contains("T形路口", StringComparison.OrdinalIgnoreCase)
          || trimmed.Contains("转角走廊", StringComparison.OrdinalIgnoreCase)
          || trimmed.Contains("直线走廊", StringComparison.OrdinalIgnoreCase)
          || trimmed.Contains("十字路口", StringComparison.OrdinalIgnoreCase)
          || trimmed.Contains("T 形路口", StringComparison.OrdinalIgnoreCase)
          || trimmed.Contains("转 角 走 廊", StringComparison.OrdinalIgnoreCase)
          || trimmed.Contains("直 线 走 廊", StringComparison.OrdinalIgnoreCase);
      }

      private static bool IsZoneCodeLabel(string? value)
      {
        if (string.IsNullOrWhiteSpace(value))
        {
          return false;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("EZ", StringComparison.OrdinalIgnoreCase)
          || trimmed.StartsWith("HCZ", StringComparison.OrdinalIgnoreCase)
          || trimmed.StartsWith("LCZ", StringComparison.OrdinalIgnoreCase);
      }

      private string GetRoomDisplayName(RoomData room)
      {
        var candidate = room.Short;
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate) && !IsHiddenRoomName(candidate) && (_isBackupMap || !IsZoneCodeLabel(candidate)))
        {
          var translated = TranslateRoomName(candidate);
          if (!string.IsNullOrWhiteSpace(translated)) return translated;
        }
        candidate = room.Code;
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate) && !IsHiddenRoomName(candidate) && (_isBackupMap || !IsZoneCodeLabel(candidate)))
        {
          var translated = TranslateRoomName(candidate);
          if (!string.IsNullOrWhiteSpace(translated)) return translated;
        }
        candidate = room.Label;
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate) && !IsHiddenRoomName(candidate) && (_isBackupMap || !IsZoneCodeLabel(candidate)))
        {
          var translated = TranslateRoomName(candidate);
          if (!string.IsNullOrWhiteSpace(translated)) return translated;
        }
        candidate = room.Name;
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate) && !IsHiddenRoomName(candidate))
        {
          var translated = TranslateRoomName(candidate);
          if (!string.IsNullOrWhiteSpace(translated)) return translated;
        }

        var variant = room.Variant?.Trim();
        if (!string.IsNullOrWhiteSpace(variant))
        {
          variant = variant.Replace("(Clone)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
          if (!string.IsNullOrWhiteSpace(variant) && !IsUnnamedRoomName(variant))
            return TranslateRoomName(variant);
        }

        return string.Empty;
      }

      private string TranslateRoomName(string name)
      {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var trimmed = name.Trim().Replace("(Clone)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (trimmed.Any(character => character is >= '\u4e00' and <= '\u9fff')
            || !trimmed.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
          return trimmed;

        foreach (var candidate in BuildRoomNameCandidates(trimmed))
        {
          if (_roomTranslations.TryGetValue(candidate, out var translated)) return translated;
        }

        // Keep room identifiers without a Chinese translation off the map.
        return string.Empty;
      }

      private static IEnumerable<string> BuildRoomNameCandidates(string value)
      {
        var trimmed = value.Trim();
        yield return trimmed;

        var prefix = string.Empty;
        var remainder = trimmed;
        foreach (var zonePrefix in new[] { "EZ", "HCZ", "LCZ" })
        {
          if (trimmed.StartsWith(zonePrefix + "_", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith(zonePrefix + " ", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith(zonePrefix + "-", StringComparison.OrdinalIgnoreCase))
          {
            prefix = zonePrefix;
            remainder = trimmed[zonePrefix.Length..].TrimStart('_', ' ', '-');
            break;
          }
        }

        if (prefix.Length > 0)
        {
          yield return prefix + remainder;
          yield return char.ToUpperInvariant(prefix[0]) + prefix[1..].ToLowerInvariant()
            + remainder.Replace("_", string.Empty, StringComparison.Ordinal);
          yield return char.ToUpperInvariant(prefix[0]) + prefix[1..].ToLowerInvariant()
            + remainder.Replace('_', ' ');
        }

        var compact = trimmed.Replace("_", string.Empty, StringComparison.Ordinal);
        if (!string.Equals(compact, trimmed, StringComparison.OrdinalIgnoreCase)) yield return compact;
      }

      private static IReadOnlyDictionary<string, string> LoadRoomTranslations()
      {
        var paths = new List<string>
        {
          Path.Combine(Environment.CurrentDirectory, "room.txt"),
          Path.Combine(AppContext.BaseDirectory, "room.txt")
        };

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 4 && directory.Parent is not null; level++)
        {
          directory = directory.Parent;
          paths.Add(Path.Combine(directory.FullName, "room.txt"));
        }

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
          if (!File.Exists(path)) continue;

          try
          {
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
              var separator = line.IndexOf('=');
              if (separator <= 0 || separator >= line.Length - 1) continue;

              var englishName = line[..separator].Trim();
              var chineseName = line[(separator + 1)..].Trim();
              if (englishName.Length > 0 && chineseName.Length > 0)
              {
                translations[englishName] = chineseName;
              }
            }

            return translations;
          }
          catch (IOException exception)
          {
            Log($"房间翻译文件读取失败: {exception.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
          }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      }

      private static Color GetCategoryColor(string category) => category switch
      {
        "scp" => Color.FromArgb(220, 255, 107, 107),
        "core" => Color.FromArgb(220, 255, 178, 56),
        "security" => Color.FromArgb(220, 106, 168, 255),
        "checkpoint" => Color.FromArgb(220, 43, 214, 150),
        "personnel" => Color.FromArgb(220, 195, 155, 255),
        "utility" => Color.FromArgb(220, 104, 214, 232),
        "deadend" => Color.FromArgb(220, 181, 121, 78),
        _ => Color.FromArgb(220, 107, 114, 128)
      };

    private static string BuildHtml()
    {
        return """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8" />
<style>
  html, body {
    margin: 0;
    width: 100%;
    height: 100%;
    background: transparent;
    overflow: hidden;
    font-family: system-ui, -apple-system, Segoe UI, sans-serif;
  }
  canvas {
    display: block;
    width: 100vw;
    height: 100vh;
    background: transparent;
  }
</style>
</head>
<body>
<canvas id="mapCanvas"></canvas>
<script>
const PAPER = '#f4f4f0';
const INK = '#191a1d';
const PATH_COLOR = '#e8590c';
const CAT_COLOR = {
  scp: '#ff6b6b', core: '#ffb238', security: '#6aa8ff',
  checkpoint: '#2bd696', personnel: '#c39bff', utility: '#68d6e8',
  deadend: '#b5794e', corridor: '#6b7280'
};

function clamp(v, min, max) {
  return Math.min(Math.max(v, min), max);
}

function zoneLabelOf(z) {
  const labels = {
    LightContainment: '轻收容区',
    HeavyContainment: '重收容区',
    Entrance: '办公区'
  };
  return labels[z] || z || '未知';
}

function drawRoundedRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + w - r, y);
  ctx.quadraticCurveTo(x + w, y, x + w, y + r);
  ctx.lineTo(x + w, y + h - r);
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  ctx.lineTo(x + r, y + h);
  ctx.quadraticCurveTo(x, y + h, x, y + h - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y, x + r, y);
  ctx.closePath();
}

function normalizeRoom(room) {
  const cx = Number(room.cx) || 3;
  const cy = Number(room.cy) || 3;
  return {
    ...room,
    width: Math.max(18, cx * 14),
    height: Math.max(18, cy * 14)
  };
}

function renderMap(map) {
  const canvas = document.getElementById('mapCanvas');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  const rooms = Object.values(map.zones || {}).flat().map(normalizeRoom);

  if (!rooms.length) {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    return;
  }

  const widthPx = window.innerWidth || document.documentElement.clientWidth || 1280;
  const heightPx = window.innerHeight || document.documentElement.clientHeight || 720;
  canvas.width = widthPx * (window.devicePixelRatio || 1);
  canvas.height = heightPx * (window.devicePixelRatio || 1);
  ctx.setTransform(window.devicePixelRatio || 1, 0, 0, window.devicePixelRatio || 1, 0, 0);

  const minX = Math.min(...rooms.map(r => r.x - r.width * 0.7));
  const maxX = Math.max(...rooms.map(r => r.x + r.width * 0.7));
  const minZ = Math.min(...rooms.map(r => r.z - r.height * 0.7));
  const maxZ = Math.max(...rooms.map(r => r.z + r.height * 0.7));

  const width = Math.max(600, widthPx);
  const height = Math.max(500, heightPx);

  const pad = 60;
  const spanX = (maxX - minX) || 1;
  const spanZ = (maxZ - minZ) || 1;
  const scale = clamp(Math.min((width - pad * 2) / spanX, (height - pad * 2) / spanZ), 8, 35);

  function toPx(room) {
    return {
      x: pad + (room.x - minX) * scale,
      y: pad + (maxZ - room.z) * scale,
      w: Math.max(12, room.width * 0.75),
      h: Math.max(12, room.height * 0.75)
    };
  }

  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = PAPER;
  ctx.fillRect(0, 0, width, height);

  rooms.forEach(room => {
    const p = toPx(room);
    const color = CAT_COLOR[room.category] || CAT_COLOR.corridor || '#777';
    const border = '#2a2a2a';

    ctx.strokeStyle = 'rgba(20,20,20,0.10)';
    ctx.lineWidth = 1.2;
    ctx.fillStyle = color;
    ctx.globalAlpha = 0.82;
    drawRoundedRect(ctx, p.x - p.w / 2, p.y - p.h / 2, p.w, p.h, 10);
    ctx.fill();
    ctx.globalAlpha = 1;
    ctx.strokeStyle = border;
    ctx.stroke();

    if (room.shape === 'Straight' || room.shape === 'TShape' || room.shape === 'XShape') {
      ctx.strokeStyle = 'rgba(0,0,0,0.25)';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(p.x - p.w * 0.2, p.y);
      ctx.lineTo(p.x + p.w * 0.2, p.y);
      ctx.moveTo(p.x, p.y - p.h * 0.2);
      ctx.lineTo(p.x, p.y + p.h * 0.2);
      ctx.stroke();
    }

    private void RegisterOverlayHotkey()
    {
      if (!Enum.TryParse<Keys>(_settings.Hotkey, true, out var key) || key == Keys.None)
      {
        key = Keys.F8;
      }

      RegisterHotKey(Handle, HotkeyId, 0, (uint)key);
    }

    protected override void WndProc(ref Message message)
    {
      if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId && _controlPanel is not null)
      {
        _controlPanel.Visible = !_controlPanel.Visible;
      }

      base.WndProc(ref message);
    }

    const label = room.code || room.short || room.label || room.name;
    if (label) {
      ctx.fillStyle = '#ffffff';
      ctx.font = '600 12px sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText(label, p.x, p.y + 5);
    }
  });

  rooms.forEach(room => {
    const p = toPx(room);
    const pts = room.conn || [];
    for (const link of pts) {
      const target = rooms.find(r => r.id !== room.id && Math.abs(r.x - (room.x + link.dx * 15)) < 1 && Math.abs(r.z - (room.z + link.dz * 15)) < 1);
      if (!target) continue;
      const q = toPx(target);
      ctx.strokeStyle = PATH_COLOR;
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.moveTo(p.x, p.y);
      ctx.lineTo(q.x, q.y);
      ctx.stroke();
    }
  });

  ctx.fillStyle = INK;
  ctx.font = '700 14px sans-serif';
  ctx.textAlign = 'left';
  const zoneList = Object.keys(map.zones || {});
  let y = 18;
  zoneList.forEach(zone => {
    ctx.fillStyle = INK;
    ctx.fillText(zoneLabelOf(zone), 18, y);
    y += 18;
  });
}

window.renderMap = function (data) {
  try {
    if (!data) return;
    window.__lastMapData = data;
    renderMap(data);
  } catch (e) {
    console.error(e);
  }
};
window.addEventListener('resize', function () {
  if (window.__lastMapData) {
    window.renderMap(window.__lastMapData);
  }
});
</script>
</body>
</html>
""";
    }
}

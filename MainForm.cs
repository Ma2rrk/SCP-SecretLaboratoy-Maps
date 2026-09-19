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
  public string Hotkey { get; set; } = "F8";
  public string SeedHotkey { get; set; } = "PageDown";
  public string ApiSource { get; set; } = "Primary";

  public static OverlaySettings Load()
  {
    var path = Path.Combine(AppContext.BaseDirectory, "overlay.json");
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

    return new OverlaySettings();
  }
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

    private readonly HttpClient _httpClient = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly OverlaySettings _settings = OverlaySettings.Load();
    private readonly TransparentTrackBar _scaleInput = new();
    private readonly TransparentTrackBar _offsetXInput = new();
    private readonly TransparentTrackBar _offsetYInput = new();
    private readonly TransparentTrackBar _lczScaleInput = new();
    private readonly TransparentTrackBar _lczOffsetXInput = new();
    private readonly TransparentTrackBar _lczOffsetYInput = new();
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
        ClearLog();
        Log("程序启动，覆盖层已创建");

        _timer.Interval = 3000;
        _timer.Tick += async (_, _) =>
        {
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
          SaveControlSettings();
          if (IsHandleCreated) UnregisterHotKey(Handle, HotkeyId);
        };
    }

    private static string GetPlayerLogPath()
    {
      return @"C:\Users\Administrator\AppData\LocalLow\Northwood\SCPSL\Player.log";
    }

    private async Task PollForSeedAsync()
    {
        if (_isPolling || !SeedMonitor.TryGetLatestSeed(GetPlayerLogPath(), out var latestSeed))
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

    private async Task LoadSeedAsync(long seed)
    {
      Log($"发现新 seed={seed}");
        try
        {
            var useBackup = string.Equals(_settings.ApiSource, "Backup", StringComparison.OrdinalIgnoreCase);
            var url = useBackup ? $"https://slmaps.com/api/maps/{seed}" : $"https://scpslmaps.fxdyj.com/api.php?seed={seed}";
            string json = string.Empty;
            for (var attempt = 1; attempt <= (useBackup ? 30 : 1); attempt++)
            {
              json = await _httpClient.GetStringAsync(url);
              if (!useBackup || !IsPendingMapJob(json)) break;
              Log($"备用 API 仍在生成地图，等待重试 ({attempt}/30)");
              await Task.Delay(TimeSpan.FromSeconds(2));
            }
            if (useBackup && IsPendingMapJob(json))
            {
              Log("备用 API 等待超时");
              return;
            }
        Log($"API 请求成功，响应长度={json.Length}");
          Log("JSON 原文开始");
          Log(json);
          Log("JSON 原文结束");
            var data = MapResponseAdapter.Parse(json, seed, useBackup);

            if (data is null || data.Zones.Count == 0)
            {
              Log("JSON 解析失败：结果为空");
                return;
            }

            Log($"JSON 解析成功：seed={data.Seed}，区域数={data.Zones.Count}");
            _map = data;
            _isBackupMap = useBackup;
            _roomTranslations = useBackup
              ? LoadRoomTranslations()
              : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _currentSeed = seed;
            Log($"地图解析成功，区域数={data.Zones.Count}，开始重绘");
            Invalidate();
        }
          catch (JsonException exception)
        {
          Log($"JSON 解析失败: {exception.Message}");
        }
          catch (Exception exception)
        {
            Log($"地图请求或解析失败: {exception.GetType().Name}: {exception.Message}");
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

      private Panel BuildControlPanel()
      {
        var panel = new TransparentPanel
        {
          Location = new Point(16, 16),
          Size = new Size(370, 610),
          ForeColor = Color.White,
          Padding = new Padding(18),
        };

        var title = new Label
        {
          Text = "地图控制",
          Location = new Point(18, 16),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.White,
          Font = new Font(FontFamily.GenericSansSerif, 13f, FontStyle.Bold),
        };
        panel.Controls.Add(title);

        var subtitle = new Label
        {
          Text = "Fine positioning and scale calibration",
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

        AddSectionLabel(panel, "CORE / EZ + HCZ", 20, 68);
        AddSectionLabel(panel, "LCZ / LIGHT CONTAINMENT", 20, 218);

        AddSliderControl(panel, "核心缩放", _scaleInput, 0, 150, (int)Math.Clamp(_settings.CoreScale * 100, 0, 150), 42);
        AddSliderControl(panel, "核心 X", _offsetXInput, -2500, 2000, (int)Math.Clamp(_settings.CoreOffsetX, -2500, 2000), 76);
        AddSliderControl(panel, "核心 Y", _offsetYInput, -2500, 2000, (int)Math.Clamp(_settings.CoreOffsetY, -2500, 2000), 110);
        AddSliderControl(panel, "LCZ 缩放", _lczScaleInput, 0, 150, (int)Math.Clamp(_settings.LczScale * 100, 0, 150), 144);
        AddSliderControl(panel, "LCZ X", _lczOffsetXInput, -2500, 2000, (int)Math.Clamp(_settings.LczOffsetX, -2500, 2000), 178);
        AddSliderControl(panel, "LCZ Y", _lczOffsetYInput, -2500, 2000, (int)Math.Clamp(_settings.LczOffsetY, -2500, 2000), 212);

        AddKeyControl(panel, "面板快捷键", _overlayHotkeyInput, _settings.Hotkey, 398, "F8");
        AddKeyControl(panel, "获取种子键", _seedHotkeyInput, _settings.SeedHotkey, 434, "PageDown");
        AddApiControl(panel, _apiInput, _settings.ApiSource, 470);

        var saveButton = new Button
        {
          Text = "保存设置",
          Location = new Point(18, 530),
          Size = new Size(136, 32),
          FlatStyle = FlatStyle.Flat,
          BackColor = Color.FromArgb(232, 89, 12),
          ForeColor = Color.White,
          Cursor = Cursors.Hand,
          Font = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold),
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += (_, _) => SaveControlSettings();
        panel.Controls.Add(saveButton);

        var hint = new Label
        {
          Text = "Scale 0.1–3.0x  |  Offset ±5000px",
          Location = new Point(166, 539),
          AutoSize = true,
          BackColor = Color.Transparent,
          ForeColor = Color.FromArgb(190, 220, 220, 220),
          Font = new Font(FontFamily.GenericSansSerif, 7.5f),
        };
        hint.Text = "Scale 0.1-1.5x  |  X +/-2000  |  Y +/-1000";
        panel.Controls.Add(hint);

        _scaleInput.ValueChanged += (_, _) => ApplyControlSettings();
        _offsetXInput.ValueChanged += (_, _) => ApplyControlSettings();
        _offsetYInput.ValueChanged += (_, _) => ApplyControlSettings();
        _lczScaleInput.ValueChanged += (_, _) => ApplyControlSettings();
        _lczOffsetXInput.ValueChanged += (_, _) => ApplyControlSettings();
        _lczOffsetYInput.ValueChanged += (_, _) => ApplyControlSettings();
        return panel;
      }

      private void AddSliderControl(Control parent, string labelText, TrackBar input, int minimum, int maximum, int value, int y)
      {
        var isScale = input == _scaleInput || input == _lczScaleInput;
        y = input == _scaleInput ? 82
          : input == _offsetXInput ? 124
          : input == _offsetYInput ? 166
          : input == _lczScaleInput ? 232
          : input == _lczOffsetXInput ? 274
          : 316;
        minimum = isScale ? ScaleSliderMin
          : input == _offsetYInput || input == _lczOffsetYInput ? OffsetYSliderMin : OffsetXSliderMin;
        maximum = isScale ? ScaleSliderMax
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
        valueLabel.Text = FormatSliderValue(input.Value, isScale);
        input.ValueChanged += (_, _) => valueLabel.Text = FormatSliderValue(input.Value, isScale);
        parent.Controls.Add(label);
        parent.Controls.Add(input);
        parent.Controls.Add(valueLabel);
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
        input.Items.AddRange(new object[] { "主 API", "备用 API (slmaps.com)" });
        input.SelectedIndex = string.Equals(selectedApi, "Backup", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
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

      private static string FormatSliderValue(int value, bool isScale)
      {
        return isScale ? $"{value / 1000f:0.000}x" : $"{value / 10f:0.0}";
      }

      private void ApplyControlSettings()
      {
        _settings.CoreScale = _scaleInput.Value / 1000f;
        _settings.CoreOffsetX = _offsetXInput.Value / 10f;
        _settings.CoreOffsetY = _offsetYInput.Value / 10f;
        _settings.LczScale = _lczScaleInput.Value / 1000f;
        _settings.LczOffsetX = _lczOffsetXInput.Value / 10f;
        _settings.LczOffsetY = _lczOffsetYInput.Value / 10f;
        Invalidate();
      }

      private void SaveControlSettings()
      {
        ApplyControlSettings();
        _settings.Hotkey = _overlayHotkeyInput.SelectedItem?.ToString() ?? "F8";
        _settings.SeedHotkey = _seedHotkeyInput.SelectedItem?.ToString() ?? "PageDown";
        _settings.ApiSource = _apiInput.SelectedIndex == 1 ? "Backup" : "Primary";
        if (_controlPanel is not null)
        {
          _controlPanel.Visible = false;
        }
        try
        {
          var path = Path.Combine(AppContext.BaseDirectory, "overlay.json");
          File.WriteAllText(path, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
          if (IsHandleCreated && !IsDisposed)
          {
            UnregisterHotKey(Handle, HotkeyId);
            RegisterOverlayHotkey();
          }
          if (_controlPanel is not null)
          {
            _controlPanel.Visible = false;
          }
          Log("配置已保存");
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
        SaveControlSettings();
        Invalidate();
        if (DateTime.UtcNow - _lastZoomLog >= TimeSpan.FromSeconds(1))
        {
          _lastZoomLog = DateTime.UtcNow;
          Log($"滚轮缩放 {(isLcz ? "LCZ" : "EZ/HCZ")}，方向={e.Delta}，比例={(isLcz ? _settings.LczScale : _settings.CoreScale):F2}");
        }
      }

      private int GetMapRegionAt(Point point)
      {
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

        var rooms = _map.Zones
          .SelectMany(zone => zone.Value.Select(room =>
          {
            room.Zone = ResolveZone(room, zone.Key);
            return room;
          }))
          .ToList();
        if (rooms.Count == 0)
        {
          return;
        }

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

        var alignedPositions = AlignConnectedRooms(rooms, positions);
        var padding = 60f;
        var coreMinX = coreRooms.Count == 0 ? 0f : coreRooms.Min(room => alignedPositions[room].X);
        var coreMaxX = coreRooms.Count == 0 ? 0f : coreRooms.Max(room => alignedPositions[room].X);
        var coreMinZ = coreRooms.Count == 0 ? 0f : coreRooms.Min(room => alignedPositions[room].Z);
        var coreMaxZ = coreRooms.Count == 0 ? 0f : coreRooms.Max(room => alignedPositions[room].Z);
        var ezMinX = ezRooms.Count == 0 ? 0f : ezRooms.Min(room => alignedPositions[room].X);
        var ezMaxX = ezRooms.Count == 0 ? 0f : ezRooms.Max(room => alignedPositions[room].X);
        var ezMinZ = ezRooms.Count == 0 ? 0f : ezRooms.Min(room => alignedPositions[room].Z);
        var ezMaxZ = ezRooms.Count == 0 ? 0f : ezRooms.Max(room => alignedPositions[room].Z);
        var hczMinX = hczRooms.Count == 0 ? 0f : hczRooms.Min(room => alignedPositions[room].X);
        var hczMaxX = hczRooms.Count == 0 ? 0f : hczRooms.Max(room => alignedPositions[room].X);
        var hczMinZ = hczRooms.Count == 0 ? 0f : hczRooms.Min(room => alignedPositions[room].Z);
        var hczMaxZ = hczRooms.Count == 0 ? 0f : hczRooms.Max(room => alignedPositions[room].Z);
        var connectedMinX = connectedCoreRooms.Count == 0 ? 0f : connectedCoreRooms.Min(room => alignedPositions[room].X);
        var connectedMaxX = connectedCoreRooms.Count == 0 ? 0f : connectedCoreRooms.Max(room => alignedPositions[room].X);
        var connectedMinZ = connectedCoreRooms.Count == 0 ? 0f : connectedCoreRooms.Min(room => alignedPositions[room].Z);
        var connectedMaxZ = connectedCoreRooms.Count == 0 ? 0f : connectedCoreRooms.Max(room => alignedPositions[room].Z);
        var lightMinX = lightRooms.Count == 0 ? 0f : lightRooms.Min(room => alignedPositions[room].X);
        var lightMaxX = lightRooms.Count == 0 ? 0f : lightRooms.Max(room => alignedPositions[room].X);
        var lightMinZ = lightRooms.Count == 0 ? 0f : lightRooms.Min(room => alignedPositions[room].Z);
        var lightMaxZ = lightRooms.Count == 0 ? 0f : lightRooms.Max(room => alignedPositions[room].Z);
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

        using var borderPen = new Pen(Color.FromArgb(210, 42, 42, 42), 1.2f);
        using var corridorBrush = new SolidBrush(Color.FromArgb(235, 25, 26, 29));
        using var labelBrush = new SolidBrush(Color.White);
        var roomOrder = rooms
          .Select((room, index) => (room, index))
          .ToDictionary(item => item.room, item => item.index);

        // Draw the connectors first so each room body hides the line ends and the map reads as one continuous corridor network.
        foreach (var room in rooms)
        {
          var point = ToPoint(room);
          var activeScale = IsLightContainmentRoom(room) ? lczScale : coreScale;
          var connectorWidth = 0.44f * 15f * activeScale;
          var connections = alignedPositions[room].Connections;
          foreach (var connection in connections)
          {
            var target = FindConnectedRoom(room, connection, rooms);
            if (target is null || roomOrder[room] >= roomOrder[target])
            {
              continue;
            }

            var targetPoint = ToPoint(target);
            using var connectorPen = new Pen(corridorBrush.Color, connectorWidth)
            {
              StartCap = LineCap.Round,
              EndCap = LineCap.Round,
              LineJoin = LineJoin.Round,
            };
            e.Graphics.DrawLine(connectorPen, point, targetPoint);
          }
        }

        _roomHitBoxes.Clear();
        _coreMapBounds = RectangleF.Empty;
        _lczMapBounds = RectangleF.Empty;
        foreach (var room in rooms)
        {
          var point = ToPoint(room);
          var activeScale = string.Equals(room.Zone, "LightContainment", StringComparison.OrdinalIgnoreCase) ? lczScale : coreScale;
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
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            var labelScale = string.Equals(room.Zone, "LightContainment", StringComparison.OrdinalIgnoreCase) ? lczScale : coreScale;
            using var roomLabelFont = new Font(FontFamily.GenericSansSerif, Math.Clamp(labelScale * 1.7f, 7f, 14f), FontStyle.Bold);
            var labelRectangle = new RectangleF(point.X - widthRoom / 2, point.Y - heightRoom / 2, widthRoom, heightRoom);
            e.Graphics.DrawString(label, roomLabelFont, labelBrush, labelRectangle, format);
          }
        }

      }

      private static RoomData? FindConnectedRoom(RoomData room, RoomConnection connection, IReadOnlyCollection<RoomData> rooms)
      {
        var targetX = connection.TargetX ?? (room.X + connection.Dx * 15f);
        var targetZ = connection.TargetZ ?? (room.Z + connection.Dz * 15f);

        return rooms.FirstOrDefault(candidate =>
          !ReferenceEquals(candidate, room)
          && Math.Abs(candidate.X - targetX) < 0.01f
          && Math.Abs(candidate.Y - room.Y) < 0.01f
          && Math.Abs(candidate.Z - targetZ) < 0.01f);
      }

      private static void DrawRoomBody(Graphics graphics, RoomData room, PointF center, float width, float height, Brush fill, Pen border)
      {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
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
        path.Dispose();
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
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate) && !IsHiddenRoomName(candidate) && !IsZoneCodeLabel(candidate)) return TranslateRoomName(candidate);
        candidate = room.Code;
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate) && !IsHiddenRoomName(candidate) && !IsZoneCodeLabel(candidate)) return TranslateRoomName(candidate);
        candidate = room.Label;
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate) && !IsHiddenRoomName(candidate) && !IsZoneCodeLabel(candidate)) return TranslateRoomName(candidate);
        candidate = room.Name;
        if (!string.IsNullOrWhiteSpace(candidate) && !IsUnnamedRoomName(candidate))
        {
          foreach (var prefix in new[]
          {
            "EZ_", "EZ ", "EZ-", "EZ:", "EZ/",
            "HCZ_", "HCZ ", "HCZ-", "HCZ:", "HCZ/",
            "LCZ_", "LCZ ", "LCZ-", "LCZ:", "LCZ/"
          })
          {
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
              var trimmed = candidate[prefix.Length..].Trim();
              if (!string.IsNullOrWhiteSpace(trimmed) && !IsUnnamedRoomName(trimmed) && !IsHiddenRoomName(trimmed))
              {
                return TranslateRoomName(trimmed);
              }
              break;
            }
          }

          var normalized = candidate.Trim();
          if (!IsZoneCodeLabel(normalized) && !IsHiddenRoomName(normalized))
          {
            return TranslateRoomName(normalized);
          }
        }

        var variant = room.Variant?.Trim();
        if (!string.IsNullOrWhiteSpace(variant))
        {
          variant = variant.Replace("(Clone)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
          foreach (var prefix in new[] { "EZ_", "HCZ_", "LCZ_" })
          {
            if (variant.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
              variant = variant[prefix.Length..].Trim();
              break;
            }
          }

          variant = variant.Replace('_', ' ');
          if (!string.IsNullOrWhiteSpace(variant) && !IsUnnamedRoomName(variant))
          {
            return variant;
          }
        }

        return string.Empty;
      }

      private string TranslateRoomName(string name)
      {
        if (!_isBackupMap || string.IsNullOrWhiteSpace(name)) return name;

        return _roomTranslations.TryGetValue(name.Trim(), out var translated) ? translated : name;
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

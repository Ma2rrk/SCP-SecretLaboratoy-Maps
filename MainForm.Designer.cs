using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SLMapsOverlay;

public sealed partial class MainForm
{
    private System.Windows.Forms.Timer _timer = null!;
    private System.Windows.Forms.Timer _settingsSaveTimer = null!;
    private TransparentTrackBar _scaleInput = null!;
    private TransparentTrackBar _offsetXInput = null!;
    private TransparentTrackBar _offsetYInput = null!;
    private TransparentTrackBar _lczScaleInput = null!;
    private TransparentTrackBar _lczOffsetXInput = null!;
    private TransparentTrackBar _lczOffsetYInput = null!;
    private TransparentTrackBar _mapOpacityInput = null!;
    private Button _roomColorInput = null!;
    private Button _connectionColorInput = null!;
    private ComboBox _overlayHotkeyInput = null!;
    private ComboBox _seedHotkeyInput = null!;
    private ComboBox _apiInput = null!;
    private CheckBox _betaVersionInput = null!;
    private ComboBox _fontInput = null!;
    private ComboBox _fontSizeInput = null!;
    private CheckBox _fontBoldInput = null!;
    private CheckBox _clearTypeInput = null!;

    private void InitializeComponent()
    {
        _timer = new System.Windows.Forms.Timer();
        _settingsSaveTimer = new System.Windows.Forms.Timer
        {
            Interval = 400,
        };
        _scaleInput = new TransparentTrackBar();
        _offsetXInput = new TransparentTrackBar();
        _offsetYInput = new TransparentTrackBar();
        _lczScaleInput = new TransparentTrackBar();
        _lczOffsetXInput = new TransparentTrackBar();
        _lczOffsetYInput = new TransparentTrackBar();
        _mapOpacityInput = new TransparentTrackBar();
        _roomColorInput = new Button();
        _connectionColorInput = new Button();
        _overlayHotkeyInput = new ComboBox();
        _seedHotkeyInput = new ComboBox();
        _apiInput = new ComboBox();
        _betaVersionInput = new CheckBox();
        _fontInput = new ComboBox();
        _fontSizeInput = new ComboBox();
        _fontBoldInput = new CheckBox();
        _clearTypeInput = new CheckBox();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.Magenta;
        ClientSize = new Size(1280, 720);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "MainForm";
        ShowInTaskbar = false;
        Text = "SLMaps Overlay";
        TopMost = true;
        TransparencyKey = Color.Magenta;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        // The existing UI is assembled by BuildControlPanel so its event wiring and
        // runtime behavior stay in one place. The designer host can still create it
        // safely because the constructor exits before polling and hotkey setup.
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            _controlPanel = BuildControlPanel();
            _controlPanel.Visible = true;
            Controls.Add(_controlPanel);
        }

        ResumeLayout(false);
    }
}

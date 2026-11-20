// @module: reEngine.Host.Automation.SdlAutomationProxyControl
// @purpose: Test-only STA WinForms proxy Control + IHostAutomationBridge + IRenderSurfaceSnapshotProvider queueing commands cleanly to SDL frame boundaries.
// @depends-on: System, System.Collections.Concurrent, System.Collections.Generic, System.Drawing, System.Linq, System.Threading, System.Windows.Forms, Foundation.Automation, reEngine.Host.Contracts
// @exports: SdlAutomationProxyControl

#if REENGINE_ENABLE_AUTOMATION
using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using Foundation.Automation;
using reEngine.Host.Contracts;

namespace reEngine.Host.Automation;

internal sealed class SdlAutomationProxyControl : IHostAutomationBridge, IRenderSurfaceSnapshotProvider
{
    private const int MaxQueueSize = 256;

    private sealed record QueuedAction(bool IsKey, string? KeyOrTarget, int X, int Y, IReadOnlyList<string>? Modifiers);

    private readonly ConcurrentQueue<QueuedAction> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly int _width;
    private readonly int _height;
    private IHostAutomationTarget? _target;
    private AutomationServerOptions? _serverOptions;
    private Thread? _staThread;
    private ProxyHostForm? _proxyForm;
    private AutomationLoopbackServer? _server;
    private string _lastWidgetSignature = string.Empty;
    private bool _started;
    private volatile bool _disposing;

    public int ListeningPort => _server?.AutomationPort ?? _serverOptions?.AutomationPort ?? 0;

    public SdlAutomationProxyControl(int width = 1280, int height = 720)
    {
        _width = Math.Max(320, width);
        _height = Math.Max(240, height);
    }

    public SdlAutomationProxyControl(IHostAutomationTarget target, AutomationServerOptions options, int width, int height)
        : this(width, height)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _serverOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Start(IHostAutomationTarget target, int port, string? token, string? allowedSnapshotRoot)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        string resolvedToken = !string.IsNullOrWhiteSpace(token)
            ? token!
            : (Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN") ?? "reengine-test-token");
        _serverOptions = new AutomationServerOptions(port, resolvedToken, "reengine-host", allowedSnapshotRoot);
        Start();
    }

    public void Start()
    {
        if (_disposing || _started) return;
        if (_target is null || _serverOptions is null)
        {
            throw new InvalidOperationException("Automation target and options must be configured before starting.");
        }

        _started = true;
        _staThread = new Thread(RunStaLoop) { IsBackground = true, Name = "reEngine.Host.AutomationProxy" };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void DrainPendingCommands()
    {
        if (_target is null) return;
        int budget = 32;
        while (budget-- > 0 && _queue.TryDequeue(out QueuedAction? cmd))
        {
            if (cmd.IsKey && !string.IsNullOrWhiteSpace(cmd.KeyOrTarget))
            {
                _target.EnqueueKeyPress(cmd.KeyOrTarget, cmd.Modifiers);
            }
            else if (!cmd.IsKey)
            {
                _target.EnqueuePointerClick(cmd.X, cmd.Y, cmd.KeyOrTarget);
            }
        }
    }

    public void SyncWidgets(IReadOnlyList<HostWidgetBounds> widgets)
    {
        if (_disposing || _proxyForm is null || _proxyForm.IsDisposed || !_proxyForm.IsHandleCreated) return;
        string sig = string.Join('|', widgets.Select(w => $"{w.ControlId}:{w.Bounds.X},{w.Bounds.Y},{w.Bounds.Width},{w.Bounds.Height}:{w.Label}"));
        if (string.Equals(sig, _lastWidgetSignature, StringComparison.Ordinal)) return;
        _lastWidgetSignature = sig;

        try
        {
            _ = _proxyForm.BeginInvoke(() => _proxyForm.ApplyWidgets(widgets));
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.WriteLine($"Automation widget sync skipped during teardown: {ex.Message}");
        }
    }

    public bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes)
    {
        if (_target is null)
        {
            width = 0;
            height = 0;
            rgbaBytes = Array.Empty<byte>();
            return false;
        }
        return _target.TryCaptureFramebufferPng(out width, out height, out rgbaBytes);
    }

    private void RunStaLoop()
    {
        try
        {
            Application.EnableVisualStyles();
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.WriteLine($"Automation widget sync skipped during teardown: {ex.Message}");
        }

        using var form = new ProxyHostForm(this, _width, _height);
        _proxyForm = form;
        form.Load += (_, _) =>
        {
            _server = new AutomationLoopbackServer(_serverOptions!, form, this);
            _server.Start();
            _ready.Set();
        };
        Application.Run(form);
    }

    private void Enqueue(QueuedAction action)
    {
        if (_queue.Count < MaxQueueSize)
        {
            _queue.Enqueue(action);
        }
    }

    public void Dispose()
    {
        if (_disposing) return;
        _disposing = true;
        _server?.Dispose();
        _server = null;

        if (_proxyForm is { IsDisposed: false, IsHandleCreated: true } pf)
        {
            try { _ = pf.BeginInvoke(() => pf.Close()); }
            catch (InvalidOperationException ex) { System.Diagnostics.Trace.WriteLine($"Proxy form close skipped: {ex.Message}"); }
        }
        _staThread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private sealed class ProxyHostForm : Form
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private readonly SdlAutomationProxyControl _owner;

        public ProxyHostForm(SdlAutomationProxyControl owner, int width, int height)
        {
            _owner = owner;
            Name = "SdlHostSurface";
            Text = "reEngine SDL3 Host Surface";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            ClientSize = new Size(width, height);
            Opacity = 0.0;
        }

        public void ApplyWidgets(IReadOnlyList<HostWidgetBounds> widgets)
        {
            foreach (Control existing in Controls.Cast<Control>().ToArray())
            {
                existing.Dispose();
            }
            Controls.Clear();
            foreach (HostWidgetBounds w in widgets)
            {
                var btn = new KeyForwardButton(this)
                {
                    Name = w.ControlId,
                    Text = w.Label,
                    Bounds = w.Bounds,
                    Visible = w.IsVisible,
                    Enabled = true
                };
                string id = w.ControlId;
                int cx = w.Bounds.X + (w.Bounds.Width / 2);
                int cy = w.Bounds.Y + (w.Bounds.Height / 2);
                btn.Click += (_, _) => _owner.Enqueue(new QueuedAction(false, id, cx, cy, null));
                Controls.Add(btn);
            }
        }

        internal void ForwardKeyMessage(int wParam)
        {
            string? keyName = MapVkToName(wParam);
            if (keyName is not null)
            {
                _owner.Enqueue(new QueuedAction(true, keyName, 0, 0, GetActiveModifiers()));
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WmKeyDown or WmSysKeyDown) ForwardKeyMessage((int)m.WParam);
            base.WndProc(ref m);
        }

        private sealed class KeyForwardButton : Button
        {
            private readonly ProxyHostForm _parent;
            public KeyForwardButton(ProxyHostForm parent) => _parent = parent;
            protected override void WndProc(ref Message m)
            {
                if (m.Msg is WmKeyDown or WmSysKeyDown) _parent.ForwardKeyMessage((int)m.WParam);
                base.WndProc(ref m);
            }
        }

        private static IReadOnlyList<string>? GetActiveModifiers()
        {
            Keys mods = Control.ModifierKeys;
            if ((mods & (Keys.Alt | Keys.Control | Keys.Shift)) == 0) return null;
            var list = new List<string>(3);
            if ((mods & Keys.Control) != 0) list.Add("CTRL");
            if ((mods & Keys.Alt) != 0) list.Add("ALT");
            if ((mods & Keys.Shift) != 0) list.Add("SHIFT");
            return list;
        }

        private static string? MapVkToName(int vk) => vk switch
        {
            0x70 => "F1",
            0x72 => "F3",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x78 => "F9",
            0x7A => "F11",
            0x7B => "F12",
            0xC0 => "Grave",
            0x1B => "ESC",
            0x09 => "TAB",
            0x0D => "ENTER",
            0x20 => "SPACE",
            0x25 => "LEFT",
            0x26 => "UP",
            0x27 => "RIGHT",
            0x28 => "DOWN",
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            _ => null
        };
    }
}
#endif

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVATacScan.Engine;
using Controls = AVATacScan.Engine.Controls;

namespace AVATacScan.Views;

/// <summary>
/// The screen: a fixed 1024×768 picture scaled to fit the window, drawn the way a colour vector monitor draws, in
/// glowing lines on black. Runs the engine from a 60 Hz timer.
/// </summary>
public sealed partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly DispatcherTimer? _timer;
    private DateTime _last = DateTime.Now;
    private bool _padPausePrev, _padBackPrev, _padUpPrev, _padDownPrev, _padLeftPrev, _padRightPrev, _padAcceptPrev, _padDonePrev;
    private double _clock;
    /// <summary>A note on the screen for a moment: a setting just changed.</summary>
    private string? _toast;
    private double _toastTime;

    public GameEngine Engine { get; }
    public SoundManager Sound { get; }
    public InputHandler Input { get; } = new();

    public GameCanvas(bool silent = false) : this(new GameEngine(), new SoundManager(silent), startTimer: true) { }

    /// <summary>For tests: an engine of your own, and no timer (call <see cref="Tick"/>).</summary>
    public GameCanvas(GameEngine engine, SoundManager sound, bool startTimer)
    {
        Engine = engine;
        Sound = sound;
        Engine.Sound += Sound.Play;
        Focusable = true;
        ClipToBounds = true;
        Input.TrackAt = (x, y) => Engine.World.Tunnel ? Field.TunnelPosAt(x, y) : Field.FlatPosAt(x, y);
        InitStars();
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer?.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        Sound.Dispose();
        Input.Dispose();
    }

    /// <summary>One frame: read the controls, run the game, redraw.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        Input.Update();
        HandlePad();
        Engine.Update(dt, Input.Player, Input.Start);
        Cursor = Engine.State == GameState.Playing ? new Cursor(StandardCursorType.None) : null;
        Advance(dt);
    }

    /// <summary>For tests: moves the drawing clock (and the stars) without running the game.</summary>
    public void Advance(double dt)
    {
        _clock += dt;
        _toastTime -= dt;
        MoveStars(dt);
        InvalidateVisual();
    }

    private void Toast(string text)
    {
        _toast = text;
        _toastTime = 2;
    }

    private void HandlePad()
    {
        bool Edge(bool now, ref bool prev) { bool e = now && !prev; prev = now; return e; }
        if (Edge(Input.PadPause, ref _padPausePrev))
        {
            if (Engine.State == GameState.Playing) Engine.Pause();
            else if (Engine.State == GameState.Paused) Engine.Resume();
        }
        if (Edge(Input.PadBack, ref _padBackPrev))
        {
            if (Engine.State == GameState.Paused) (TopLevel.GetTopLevel(this) as Window)?.Close();
            else if (Engine.State == GameState.Playing) Engine.Pause();
        }
        if (Engine.State == GameState.EnterInitials && Engine.Initials is { } entry)
        {
            if (Edge(Input.PadUp, ref _padUpPrev)) entry.Cycle(1);
            if (Edge(Input.PadDown, ref _padDownPrev)) entry.Cycle(-1);
            if (Edge(Input.PadLeft, ref _padLeftPrev)) entry.Back();
            if (Edge(Input.PadRight, ref _padRightPrev)) entry.Move(1);
            if (Edge(Input.PadAccept, ref _padAcceptPrev)) entry.Advance();
            if (Edge(Input.PadDone, ref _padDonePrev)) entry.Finish();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            SaveScreenshot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"AVATacScan_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
            return;
        }
        // While entering initials every key belongs to the entry.
        if (Engine.State == GameState.EnterInitials && Engine.Initials is { } entry)
        {
            InitialsKey(entry, e.Key);
            return;
        }
        switch (e.Key)
        {
            case Key.M:
                Sound.IsMuted = !Sound.IsMuted;
                Toast(Sound.IsMuted ? "SOUND OFF" : "SOUND ON");
                return;
            case Key.Escape:
                if (Engine.State == GameState.Playing) Engine.Pause();
                else if (Engine.State is GameState.Attract or GameState.HighScores) Engine.ReturnToTitle();
                else (TopLevel.GetTopLevel(this) as Window)?.Close();
                return;
            case Key.P:
                if (Engine.State == GameState.Playing) { Engine.Pause(); return; }
                break;
        }
        if (Engine.State == GameState.Paused)
        {
            Engine.Resume();
            return;
        }
        Input.OnKeyDown(e.Key, e.PhysicalKey);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.OnKeyUp(e.Key, e.PhysicalKey);
        e.Handled = true;
    }

    /// <summary>Where a point in the window is on the game's own screen.</summary>
    private Point ToScreen(Point p)
    {
        var (s, ox, oy) = Fit();
        return s <= 0 ? p : new Point((p.X - ox) / s, (p.Y - oy) / s);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = ToScreen(e.GetPosition(this));
        Input.MouseAt(p.X, p.Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        Input.MouseButton = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => Input.MouseButton = false;

    /// <summary>The wheel turns the squadron like the spinner, a notch at a time.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        Input.Wheel(e.Delta.Y != 0 ? -e.Delta.Y : e.Delta.X);
        e.Handled = true;
    }

    private static void InitialsKey(InitialsEntry entry, Key key)
    {
        switch (key)
        {
            case Key.Up: entry.Cycle(1); break;
            case Key.Down: entry.Cycle(-1); break;
            case Key.Left or Key.Back: entry.Back(); break;
            case Key.Right: entry.Move(1); break;
            case Key.Space: entry.Advance(); break;
            case Key.Enter: entry.Finish(); break;
            case Key.OemPeriod or Key.Decimal: entry.Type('.'); break;
            case >= Key.A and <= Key.Z: entry.Type((char)('A' + (key - Key.A))); break;
            case >= Key.D0 and <= Key.D9: entry.Type((char)('0' + (key - Key.D0))); break;
            case >= Key.NumPad0 and <= Key.NumPad9: entry.Type((char)('0' + (key - Key.NumPad0))); break;
        }
    }

    /// <summary>Saves what's on screen as a PNG.</summary>
    public void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>How the fixed screen fits the window: scale and letterbox offsets.</summary>
    private (double Scale, double X, double Y) Fit()
    {
        double s = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (s, (Bounds.Width - ScreenWidth * s) / 2, (Bounds.Height - ScreenHeight * s) / 2);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var (s, ox, oy) = Fit();
        if (s <= 0) return;
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
            DrawScreen(dc);
    }
}

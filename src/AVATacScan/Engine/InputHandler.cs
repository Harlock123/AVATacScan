using Avalonia.Input;

namespace AVATacScan.Engine;

/// <summary>
/// Keyboard, mouse and gamepad boiled down to the controls: the spinner (left and right along the track) and the
/// guns. The mouse works like the arcade machine's spinner: move it and the squadron flies to the pointer; its
/// wheel turns the squadron a notch at a time. A key or the stick takes over until the mouse moves again.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0);
    private double _mouseX = Field.Width / 2, _mouseY = Field.Height - 100;
    private bool _mouseSteers;
    private int _wheelFrames, _wheelDir;

    /// <summary>A mouse button held: the guns fire.</summary>
    public bool MouseButton { get; set; }

    /// <summary>The place on the track under a point on the screen (the flat's by default; the host knows when it's the tunnel's).</summary>
    public Func<double, double, double> TrackAt { get; set; } = Field.FlatPosAt;

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    /// <summary>The pointer moved, in the game's own 1024×768 screen: the squadron follows it from now.</summary>
    public void MouseAt(double x, double y)
    {
        if (Math.Abs(x - _mouseX) + Math.Abs(y - _mouseY) < 0.5) return;
        (_mouseX, _mouseY) = (x, y);
        _mouseSteers = true;
    }

    /// <summary>The mouse wheel turned: a notch of the spinner each way.</summary>
    public void Wheel(double delta)
    {
        if (delta == 0) return;
        _wheelDir = delta > 0 ? 1 : -1;
        _wheelFrames = Math.Min(30, _wheelFrames + 7);
        _mouseSteers = false;
    }

    /// <summary>Where the pointer is, in the game's own screen, and whether it's steering.</summary>
    public (double X, double Y) Pointer => (_mouseX, _mouseY);
    public bool MouseSteers => _mouseSteers;

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    /// <summary>Once a frame: the gamepad read, the wheel's notch run down.</summary>
    public void Update()
    {
        _pad.Update();
        if (_wheelFrames > 0) _wheelFrames--;
    }

    /// <summary>
    /// Left and right (the arrows, A and D, or the keypad) to fly the squadron along the track; Z, Space, Ctrl or
    /// Shift to fire (hold it for volley after volley). On a gamepad: either stick (pushed further, faster), the
    /// D-pad or the shoulder buttons, and A or the right trigger to fire.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            double spin = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (spin == 0) spin = pad.DPadLeft || pad.LeftBumper ? -1 : pad.DPadRight || pad.RightBumper ? 1 : 0;
            if (spin == 0) spin = Math.Abs(pad.LeftStickX) > Math.Abs(pad.RightStickX) ? pad.LeftStickX : pad.RightStickX;
            if (spin == 0 && _wheelFrames > 0) spin = _wheelDir;
            bool fire = Any(Key.Z, Key.Space, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift) || pad.ButtonA || pad.RightTrigger > 0.5f || MouseButton;
            // The keys or the stick take over from the mouse until it moves again.
            if (spin != 0 && _wheelFrames == 0) _mouseSteers = false;
            if (_mouseSteers) return new Controls(0, fire, true, TrackAt(_mouseX, _mouseY));
            return new Controls(spin, fire);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}

using Engine;
using Engine.Input;
namespace Game;

/// <summary>One button owns one finger, independent of the GUI's shared pointer.</summary>
public sealed class ScTouchCapture {
    public int? TouchId { get; private set; }
    public bool Pressed { get; private set; }
    public bool Clicked { get; private set; }
    public bool Cancelled { get; private set; }
    public void Step(IEnumerable<TouchLocation> touches, Func<Vector2, bool> hit, bool enabled) {
        Clicked = Cancelled = false;
        if (!enabled) { Cancelled = TouchId.HasValue; TouchId = null; Pressed = false; return; }
        if (TouchId.HasValue) {
            foreach (var touch in touches) if (touch.Id == TouchId.Value) {
                if (touch.State == TouchLocationState.Released) {
                    Clicked = hit(touch.Position); TouchId = null; Pressed = false;
                } else Pressed = true; // Moving off the button does not release the finger.
                return;
            }
            // Lost focus/cancelled OS gesture: cancel, never turn it into a throw.
            Cancelled = true; TouchId = null; Pressed = false; return;
        }
        Pressed = false;
        foreach (var touch in touches) if (touch.State == TouchLocationState.Pressed && hit(touch.Position)) {
            TouchId = touch.Id; Pressed = true; return;
        }
    }
}

public sealed class ScWeaponButtonInput {
    readonly ScTouchCapture m_touch = new();
    bool m_mousePressed;
    public bool Pressed { get; private set; }
    public bool Clicked { get; private set; }
    public bool Cancelled { get; private set; }
    public void Sample(BevelledButtonWidget button, bool touch, bool enabled) {
        if (touch || m_touch.TouchId.HasValue) {
            m_mousePressed = false;
            m_touch.Step(button.Input.TouchLocations, p => button.HitTestGlobal(p) == button.m_clickableWidget, enabled);
            Pressed = m_touch.Pressed; Clicked = m_touch.Clicked; Cancelled = m_touch.Cancelled;
        } else {
            Cancelled = !enabled && m_mousePressed;
            m_mousePressed = enabled && (button.m_clickableWidget.IsPressed || m_mousePressed && button.Input.Press.HasValue);
            Pressed = m_mousePressed; Clicked = enabled && button.IsClicked;
        }
    }
}

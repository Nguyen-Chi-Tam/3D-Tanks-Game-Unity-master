using UnityEngine;

// Centralized mobile input aggregator. Uses directional buttons (no joysticks).
public class MobileInput : MonoBehaviour
{
    public bool isMobile = false;
    public static MobileInput Instance;

    // Direction button states
    private bool _upPressed;
    private bool _downPressed;
    private bool _leftPressed;
    private bool _rightPressed;

    // Attack button states to mirror Input.GetButton* APIs
    public bool AttackDown { get; private set; }
    public bool AttackHeld { get; private set; }
    public bool AttackUp { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    // Clear edge-triggered flags after all Update() calls each frame.
    private void LateUpdate()
    {
        AttackDown = false;
        AttackUp = false;
    }

    public void OnAttackButtonDown()
    {
        AttackDown = true;
        AttackHeld = true;
    }

    public void OnAttackButtonUp()
    {
        AttackHeld = false;
        AttackUp = true;
    }

    public void SetMoveUp(bool pressed) => _upPressed = pressed;
    public void SetMoveDown(bool pressed) => _downPressed = pressed;
    public void SetTurnLeft(bool pressed) => _leftPressed = pressed;
    public void SetTurnRight(bool pressed) => _rightPressed = pressed;

    // Forward/backward input from buttons: Up = +1, Down = -1
    public float GetMoveInput()
    {
        if (!isMobile) return 0f;
        float v = 0f;
        if (_upPressed) v += 1f;
        if (_downPressed) v -= 1f;
        return Mathf.Clamp(v, -1f, 1f);
    }

    // Left/right turn input from buttons: Left = -1, Right = +1
    public float GetTurnInput()
    {
        if (!isMobile) return 0f;
        float h = 0f;
        if (_rightPressed) h += 1f;
        if (_leftPressed) h -= 1f;
        return Mathf.Clamp(h, -1f, 1f);
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

public class MobileMovementButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum Direction { Up, Down, Left, Right }
    public Direction direction;

    private MobileInput InputRef => MobileInput.Instance;

    public void OnPointerDown(PointerEventData eventData)
    {
        SetPressed(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        SetPressed(false);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // In case finger slides off the button
        SetPressed(false);
    }

    private void SetPressed(bool pressed)
    {
        if (InputRef == null) return;
        switch (direction)
        {
            case Direction.Up: InputRef.SetMoveUp(pressed); break;
            case Direction.Down: InputRef.SetMoveDown(pressed); break;
            case Direction.Left: InputRef.SetTurnLeft(pressed); break;
            case Direction.Right: InputRef.SetTurnRight(pressed); break;
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

public class SimpleJoystick : MonoBehaviour, IDragHandler, IPointerUpHandler, IPointerDownHandler
{
    public RectTransform background;
    public RectTransform handle;
    public float handleRange = 50f;
    public Vector2 Direction { get; private set; }

    private Vector2 _startPos;

    void Start()
    {
        if (background == null) background = GetComponent<RectTransform>();
        _startPos = handle.anchoredPosition;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 pos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, eventData.pressEventCamera, out pos);
        pos = Vector2.ClampMagnitude(pos, handleRange);
        handle.anchoredPosition = pos;
        Direction = pos / handleRange;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        handle.anchoredPosition = _startPos;
        Direction = Vector2.zero;
    }
}

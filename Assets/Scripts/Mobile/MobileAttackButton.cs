using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Mirrors keyboard behavior: down begins charging, up releases shot.
public class MobileAttackButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public Button attackButton;

    private void Awake()
    {
        if (attackButton == null)
            attackButton = GetComponent<Button>();
        // Optional: if an OnClick is present, simulate a quick tap (down+up)
        if (attackButton != null)
        {
            attackButton.onClick.RemoveAllListeners();
            attackButton.onClick.AddListener(() => {
                var mi = MobileInput.Instance;
                if (mi == null) return;
                mi.OnAttackButtonDown();
                mi.OnAttackButtonUp();
            });
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        var mi = MobileInput.Instance;
        if (mi != null) mi.OnAttackButtonDown();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        var mi = MobileInput.Instance;
        if (mi != null) mi.OnAttackButtonUp();
    }
}

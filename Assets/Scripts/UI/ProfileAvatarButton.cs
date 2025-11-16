using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on the Profile Pic object (which is a Button) to open the avatar selector
/// when clicked. It also auto-fixes the Button's Target Graphic warning by assigning
/// the local Image as target if present; otherwise it disables transitions.
/// </summary>
[RequireComponent(typeof(Button))]
[ExecuteAlways]
public class ProfileAvatarButton : MonoBehaviour
{
    [SerializeField] private ProfileUI profileUI; // Optional; auto-found in parents if not assigned

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();

        EnsureTargetGraphicAssigned();

        if (profileUI == null)
            profileUI = GetComponentInParent<ProfileUI>(true);
    }

    private void OnValidate()
    {
        // Keep inspector clean even outside Play Mode
        if (button == null) button = GetComponent<Button>();
        EnsureTargetGraphicAssigned();
    }

    private void OnEnable()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        if (profileUI != null)
            profileUI.ChooseAvatarFromDevice();
        else
            Debug.LogWarning("ProfileAvatarButton: Missing reference to ProfileUI. Assign it or place this button under the Profile panel.");
    }

    private void EnsureTargetGraphicAssigned()
    {
        if (button == null) return;
        if (button.targetGraphic != null) return;

        // 1) Try local Image
        var img = GetComponent<Image>();
        if (img != null)
        {
            button.targetGraphic = img;
            return;
        }

        // 2) Try a sibling named "Avatar" under the same parent
        if (transform.parent != null)
        {
            var avatar = transform.parent.Find("Avatar");
            if (avatar != null)
            {
                var siblingImg = avatar.GetComponent<Image>();
                if (siblingImg != null)
                {
                    button.targetGraphic = siblingImg;
                    return;
                }
            }

            // 3) Fallback: any Image under the same parent hierarchy
            var imgs = transform.parent.GetComponentsInChildren<Image>(true);
            foreach (var candidate in imgs)
            {
                if (candidate == null || candidate.gameObject == gameObject) continue;
                button.targetGraphic = candidate;
                return;
            }
        }

        // 4) Last resort: disable transition to avoid warning
        button.transition = Selectable.Transition.None;
    }
}

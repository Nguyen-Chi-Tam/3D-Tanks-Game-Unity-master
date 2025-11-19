using UnityEngine;

public class AspectRatioController : MonoBehaviour
{
    // Tỉ lệ mục tiêu: 16:9
    private float targetAspect = 16.0f / 9.0f;

    void Start()
    {
        // Tính toán tỉ lệ màn hình hiện tại của thiết bị
        float windowAspect = (float)Screen.width / (float)Screen.height;

        // Tính tỉ lệ chênh lệch
        float scaleHeight = windowAspect / targetAspect;

        Camera camera = GetComponent<Camera>();

        // Nếu tỉ lệ màn hình hiện tại THẤP hơn mục tiêu (màn hình cao/hẹp hơn)
        // -> Cần thêm viền đen trên và dưới (Letterbox)
        if (scaleHeight < 1.0f)
        {
            Rect rect = camera.rect;

            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0;
            rect.y = (1.0f - scaleHeight) / 2.0f;

            camera.rect = rect;
        }
        else // Nếu tỉ lệ màn hình hiện tại CAO hơn mục tiêu (màn hình dài/rộng hơn)
        {    // -> Cần thêm viền đen trái và phải (Pillarbox)
            float scaleWidth = 1.0f / scaleHeight;

            Rect rect = camera.rect;

            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) / 2.0f;
            rect.y = 0;

            camera.rect = rect;
        }
    }
}
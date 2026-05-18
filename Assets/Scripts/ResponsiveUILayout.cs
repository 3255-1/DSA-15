using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ResponsiveUILayout : MonoBehaviour
{
    static readonly Color ViewportBackground = new Color(0.17735851f, 0.17735851f, 0.17735851f, 1f);

    [SerializeField] RectTransform fullscreenBackground;
    [SerializeField] RawImage drawAreaRawImage;

    Camera renderCamera;
    bool layoutApplied;

    void Awake()
    {
        if (fullscreenBackground == null)
        {
            Transform bg = transform.Find("BackGround");
            if (bg != null)
                fullscreenBackground = bg as RectTransform;
        }

        if (drawAreaRawImage == null)
        {
            Transform drawArea = transform.Find("Main Wrapper/DrawArea/RawImage");
            if (drawArea != null)
                drawAreaRawImage = drawArea.GetComponent<RawImage>();
        }
    }

    void Start()
    {
        Apply(force: true);
    }

    public void Configure(RawImage rawImage)
    {
        drawAreaRawImage = rawImage;
        Apply(force: true);
    }

    public void Apply(bool force = false)
    {
        if (layoutApplied && !force) return;
        layoutApplied = true;

        StretchToParent(fullscreenBackground);

        if (drawAreaRawImage != null)
        {
            StretchToParent(drawAreaRawImage.rectTransform);
            Transform drawArea = drawAreaRawImage.transform.parent;
            if (drawArea != null)
            {
                Image drawAreaImage = drawArea.GetComponent<Image>();
                if (drawAreaImage != null)
                    drawAreaImage.color = Color.white;
            }
        }

        if (renderCamera == null)
        {
            foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.targetTexture == null) continue;
                renderCamera = cam;
                break;
            }
        }

        if (renderCamera != null)
            renderCamera.backgroundColor = ViewportBackground;
    }

    static void StretchToParent(RectTransform rect)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}

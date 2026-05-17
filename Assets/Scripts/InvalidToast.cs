using System.Collections;
using TMPro;
using UnityEngine;

public class InvalidToast : MonoBehaviour
{
    static InvalidToast activeToast;

    [SerializeField] float slideInDuration = 0.25f;
    [SerializeField] float holdDuration = 1.5f;
    [SerializeField] float slideOutDuration = 0.2f;
    [SerializeField] float hiddenScale = 0f;
    [SerializeField] float shownScale = 0.5f;
    [SerializeField] float topOffset = -70f;

    RectTransform rectTransform;
    TextMeshProUGUI messageText;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        messageText = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    public void SetMessage(string message)
    {
        if (messageText != null && !string.IsNullOrEmpty(message))
            messageText.text = message;
    }

    public void Play()
    {
        if (activeToast != null && activeToast != this)
            Destroy(activeToast.gameObject);

        activeToast = this;
        StopAllCoroutines();
        StartCoroutine(PlayRoutine());
    }

    IEnumerator PlayRoutine()
    {
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, topOffset);
        }

        transform.localScale = Vector3.one * hiddenScale;

        float t = 0f;
        while (t < slideInDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / slideInDuration);
            transform.localScale = Vector3.one * Mathf.Lerp(hiddenScale, shownScale, k);
            yield return null;
        }
        transform.localScale = Vector3.one * shownScale;

        yield return new WaitForSecondsRealtime(holdDuration);

        t = 0f;
        while (t < slideOutDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / slideOutDuration);
            transform.localScale = Vector3.one * Mathf.Lerp(shownScale, hiddenScale, k);
            yield return null;
        }

        if (activeToast == this)
            activeToast = null;
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (activeToast == this)
            activeToast = null;
    }
}

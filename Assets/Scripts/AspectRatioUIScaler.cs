using UnityEngine;

/// <summary>
/// Bu scripti bir UI grubunun (ör. TopBar, LevelContent, PlayButton'ı saran bir parent)
/// üzerine ekle. Ekranın aspect ratio'sunu algılar ve grubu X-Y oranını bozmadan
/// (uniform scale) büyütüp küçültür. Canvas Scaler ayarlarına dokunmaz, sadece
/// eklendiği objenin RectTransform'unu etkiler.
///
/// Kullanım:
/// 1) Script'i TopBar / LevelContent / PlayButton gibi grupların PARENT objesine ekle
///    (grubun kendisine de eklenebilir, fark etmez - sadece o objeyi ve altındaki
///    her şeyi birlikte ölçekler).
/// 2) referenceAspectRatio değerini, UI'nı tasarladığın ekran oranına göre ayarla.
///    Örnek: 9:19.5 ekran için 19.5f / 9f (yani ~2.1667)
/// 3) İstersen minScale / maxScale ile ölçeklemeyi sınırla.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class AspectRatioUIScaler : MonoBehaviour
{
    [Header("Referans Ekran Oranı")]
    [Tooltip("UI'nı tasarladığın ekranın height/width oranı. Örn: 9:19.5 ekran için 19.5/9 = 2.1667")]
    public float referenceAspectRatio = 19.5f / 9f;

    [Header("Ölçekleme Ayarları")]
    [Tooltip("0 = hiç ölçekleme yapma, 1 = tam orantılı ölçekleme. Aradaki değerlerle etkiyi yumuşatabilirsin.")]
    [Range(0f, 1f)]
    public float scaleStrength = 1f;

    [Tooltip("Ölçeğin inebileceği en düşük değer (referans orana tam uyan ekranlarda genelde 1 olmalı)")]
    public float minScale = 1f;

    [Tooltip("Ölçeğin çıkabileceği en yüksek değer (çok kısa/geniş ekranlarda aşırı büyümeyi engeller)")]
    public float maxScale = 1.6f;

    [Header("Debug")]
    [Tooltip("Inspector'da hesaplanan güncel scale değerini gösterir")]
    [SerializeField] private float currentAppliedScale = 1f;

    private RectTransform rectTransform;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        // Enable olduğunda hemen bir kere hesapla
        lastScreenWidth = -1;
        lastScreenHeight = -1;
        ApplyScaleIfNeeded();
    }

    private void Update()
    {
        // Sadece ekran boyutu gerçekten değiştiğinde yeniden hesapla (performans için)
        ApplyScaleIfNeeded();
    }

    private void ApplyScaleIfNeeded()
    {
        if (rectTransform == null) return;

        int w = Screen.width;
        int h = Screen.height;

        if (w == lastScreenWidth && h == lastScreenHeight)
            return;

        lastScreenWidth = w;
        lastScreenHeight = h;

        if (w <= 0 || h <= 0) return;

        float currentAspect = (float)h / w;

        // Ekran referanstan ne kadar "kısaldıysa" (9:16 gibi) o kadar büyüt.
        // Ekran referansa eşit veya daha "uzun" ise (9:19.5, 9:20 gibi) scale 1'de kalır.
        float rawScale = referenceAspectRatio / currentAspect;

        // Referanstan uzun ekranlarda (rawScale < 1) büyütme yapmıyoruz,
        // sadece referanstan kısa ekranlarda (rawScale > 1) büyütüyoruz.
        rawScale = Mathf.Max(1f, rawScale);

        // scaleStrength ile etkiyi yumuşat
        float finalScale = 1f + (rawScale - 1f) * scaleStrength;

        finalScale = Mathf.Clamp(finalScale, minScale, maxScale);

        currentAppliedScale = finalScale;
        rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);
    }
}
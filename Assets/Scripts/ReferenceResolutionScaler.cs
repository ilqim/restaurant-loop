using UnityEngine;

/// <summary>
/// Bu component'i eklediğin obje, verdiğin "referans çözünürlüğe" göre
/// o anki cihaz çözünürlüğüne orantılı şekilde küçülür / büyür.
/// Canvas Scaler'ın "Scale With Screen Size" mantığıyla aynı algoritmayı kullanır,
/// ama sadece bu objeye (ve altındakilere) uygulanır, Canvas'ı etkilemez.
///
/// Kullanım:
/// 1) Script'i ölçeklemek istediğin objeye (ör. TopBar, LevelContent, PlayButton) ekle.
/// 2) Reference Width / Reference Height alanlarına, UI'nı tasarladığın referans
///    çözünürlüğü gir (ör. 1080 x 2340).
/// 3) Match (0-1) ile ölçeklemenin genişliğe mi, yüksekliğe mi, yoksa ikisinin
///    ortalamasına mı göre yapılacağını ayarla:
///    - 0  = tamamen genişliğe göre ölçekle
///    - 1  = tamamen yüksekliğe göre ölçekle
///    - 0.5 = ikisinin dengeli ortalaması
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ReferenceResolutionScaler : MonoBehaviour
{
    [Header("Referans Çözünürlük")]
    [Tooltip("UI'nı tasarladığın referans genişlik (piksel)")]
    public float referenceWidth = 1080f;

    [Tooltip("UI'nı tasarladığın referans yükseklik (piksel)")]
    public float referenceHeight = 1920f;

    [Header("Ölçekleme Ayarı")]
    [Tooltip("0 = genişliğe göre, 1 = yüksekliğe göre, 0.5 = ikisinin ortalaması")]
    [Range(0f, 1f)]
    public float match = 0.5f;

    [Header("Sınırlar (Opsiyonel)")]
    public float minScale = 0.5f;
    public float maxScale = 2f;

    [Header("Debug")]
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

        lastScreenWidth = -1;
        lastScreenHeight = -1;
        ApplyScaleIfNeeded();
    }

    private void Update()
    {
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

        if (w <= 0 || h <= 0 || referenceWidth <= 0f || referenceHeight <= 0f)
            return;

        // Canvas Scaler'ın "Scale With Screen Size" ile aynı mantık:
        // logaritmik ortalama alarak X ve Y'yi tek bir uniform scale'e indiriyoruz.
        float logWidth = Mathf.Log(w / referenceWidth, 2f);
        float logHeight = Mathf.Log(h / referenceHeight, 2f);
        float logScale = Mathf.Lerp(logWidth, logHeight, match);
        float scale = Mathf.Pow(2f, logScale);

        scale = Mathf.Clamp(scale, minScale, maxScale);

        currentAppliedScale = scale;
        rectTransform.localScale = new Vector3(scale, scale, 1f);
    }
}
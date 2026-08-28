using UnityEngine;

[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public class ResponsiveCamera : MonoBehaviour
{
    [Header("Reference Settings")]
    [Tooltip("Target design width (e.g., 1080)")]
    [SerializeField] private float referenceWidth = 1080f;

    [Tooltip("Target design height (e.g., 1920)")]
    [SerializeField] private float referenceHeight = 1920f;

    [Header("Referans Kamera Pozisyonu (SADECE BİR KEZ ayarlanır)")]
    [Tooltip("Kameranın referans aspect'teki (Reference Width/Height) DOĞRU pozisyonu. " +
             "Component ilk eklendiğinde / Inspector'da sağ tık > Reset yapıldığında otomatik " +
             "yakalanır. HER Awake()'te CANLI transform'dan tekrar YAKALANMIYOR — aksi halde " +
             "Editor'de script her reload olduğunda bir önceki hesaplamanın SONUCUNU 'orijinal' " +
             "sanıp üstüne tekrar ölçek uygulardı (kamera zamanla merkeze 'çöker'di).")]
    [SerializeField] private Vector3 referencePosition;

    private Camera cam;

    // Kameranın (referans pozisyon + rotasyondan) baktığı yönün zeminle
    // (Y=0) kesiştiği nokta. Rotasyon hiç değişmediği ve referencePosition
    // sabit olduğu için bu değer de SABİTTİR — bir kere hesaplanır, hiçbir
    // ekstra obje/Transform gerekmez.
    private Vector3 groundFocusPoint;
    private bool groundFocusComputed;

    /// <summary>
    /// Component ilk eklendiğinde VEYA Inspector'da sağ tık > Reset
    /// yapıldığında Unity bunu bir kere çağırır.
    /// </summary>
    private void Reset()
    {
        referencePosition = transform.position;
        groundFocusComputed = false;
    }

    /// <summary>
    /// Kamerayı ELLE doğru konuma getirdikten sonra bu context menu ile
    /// referencePosition'ı GÜNCELLEYEBİLİRSİN.
    /// </summary>
    [ContextMenu("Capture Current Position As Reference")]
    private void CaptureReferencePosition()
    {
        referencePosition = transform.position;
        groundFocusComputed = false;
        AdjustCamera();
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();

        // Güvenlik: referencePosition hiç ayarlanmamışsa canlı pozisyonu
        // yakala — ama SADECE bu durumda, her Awake'te DEĞİL.
        if (referencePosition == Vector3.zero)
            referencePosition = transform.position;

        AdjustCamera();
    }

    private void Update()
    {
#if UNITY_EDITOR
        // Keeps camera synced during Game view resizing in Editor
        AdjustCamera();
#endif
    }

    private void EnsureGroundFocusPoint()
    {
        if (groundFocusComputed)
            return;

        // Kameranın forward'ı rotasyona bağlı, rotasyon script tarafından
        // HİÇ değiştirilmiyor — bu yüzden transform.forward burada güvenle
        // kullanılabilir (referencePosition zamanındaki rotasyonla aynı).
        Vector3 origin = referencePosition;
        Vector3 dir = transform.forward;

        if (Mathf.Abs(dir.y) > 0.0001f)
        {
            // Ray'in Y=0 düzlemini kestiği t parametresi.
            float t = -origin.y / dir.y;

            if (t > 0f)
            {
                groundFocusPoint = origin + dir * t;
                groundFocusComputed = true;
                return;
            }
        }

        // Kamera yukarı/yataya bakıyorsa (zeminle hiç kesişmiyorsa) güvenli
        // fallback: dünya merkezi.
        groundFocusPoint = Vector3.zero;
        groundFocusComputed = true;
    }

    public void AdjustCamera()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam.orthographic) return; // Bu script SADECE perspective kameralar için.

        EnsureGroundFocusPoint();

        float referenceAspect = referenceWidth / referenceHeight;
        float currentAspect = (float)Screen.width / Screen.height;

        // ------------------------------------------------------------
        // NEDEN FOV DEĞİL DE MESAFE (DOLLY)?
        // Kamera aşağıya AÇILI durduğu için FOV değiştirmek basit bir
        // "zoom" gibi davranmıyor — FOV küçüldükçe görünen pencere
        // zeminde ÇOK DAHA UZAK bir noktaya kayıyor (board'un üzerinden
        // kayıp boş zemin görünmesinin sebebi buydu).
        //
        // Bunun yerine FOV ve ROTASYONA hiç dokunmuyoruz — kamerayı,
        // KENDİ BAKTIĞI yönün zeminle kesiştiği noktaya (groundFocusPoint
        // — otomatik hesaplanır, hiçbir ekstra obje gerekmez) olan
        // UZAKLIĞINI ölçekliyoruz (dolly in/out). Saf bir öteleme olduğu
        // için görünüm hiç "kaymaz", sadece büyür/küçülür.
        // ------------------------------------------------------------
        float distanceScale = referenceAspect / currentAspect;

        Vector3 offsetFromFocus = referencePosition - groundFocusPoint;
        transform.position = groundFocusPoint + offsetFromFocus * distanceScale;
    }
}
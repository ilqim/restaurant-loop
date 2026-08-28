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

    [Header("Board / İçerik Merkezi")]
    [Tooltip("Kameranın odaklandığı board'un/içeriğin merkezi. Boş bırakılırsa dünya merkezi (0,0,0) kullanılır.")]
    [SerializeField] private Transform focusPoint;

    [Header("Referans Kamera Pozisyonu (SADECE BİR KEZ ayarlanır)")]
    [Tooltip("Kameranın referans aspect'teki (Reference Width/Height) DOĞRU pozisyonu. " +
             "Component ilk eklendiğinde / 'Reset' yapıldığında otomatik yakalanır. " +
             "BUNU HER Awake()'te CANLI transform'dan tekrar YAKALAMIYORUZ — aksi halde " +
             "Editor'de script her reload olduğunda (ExecuteAlways), zaten bir önceki " +
             "dolly'nin SONUCUNU 'orijinal pozisyon' sanıp üstüne tekrar ölçek uygular; " +
             "bu da kamerayı her reload'da biraz daha merkeze doğru 'çöktürür' " +
             "(sonunda neredeyse 0,0,0'a yapışması bu birikimli hatadan kaynaklanıyordu).")]
    [SerializeField] private Vector3 referencePosition;

    private Camera cam;

    /// <summary>
    /// Component ilk eklendiğinde VEYA Inspector'da sağ tık > Reset
    /// yapıldığında Unity bunu bir kere çağırır — referancePosition'ı o
    /// anki (doğru, elle ayarlanmış) kamera pozisyonundan yakalar.
    /// </summary>
    private void Reset()
    {
        referencePosition = transform.position;
    }

    /// <summary>
    /// Kamerayı ELLE doğru konuma getirdikten sonra bu context menu ile
    /// referancePosition'ı GÜNCELLEYEBİLİRSİN — örn. reference aspect'te
    /// (1170x2532) Game view'ı seçip kamerayı elle iyi bir yere koyduktan
    /// sonra buna tıkla, o pozisyon kalıcı referans olarak kaydedilsin.
    /// </summary>
    [ContextMenu("Capture Current Position As Reference")]
    private void CaptureReferencePosition()
    {
        referencePosition = transform.position;
        AdjustCamera();
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();

        // Güvenlik: referencePosition hiç ayarlanmamışsa (ör. script yeni
        // eklendi, Reset() bir şekilde tetiklenmedi) canlı pozisyonu
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

    public void AdjustCamera()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam.orthographic) return; // Bu script SADECE perspective kameralar için.

        float referenceAspect = referenceWidth / referenceHeight;
        float currentAspect = (float)Screen.width / Screen.height;

        // ------------------------------------------------------------
        // NEDEN FOV DEĞİL DE MESAFE (DOLLY)?
        //
        // Kamera aşağıya doğru AÇILI (ör. Rotation X=69°) durduğu için,
        // FOV'u değiştirmek basit bir "zoom" gibi davranmıyor — FOV
        // küçüldükçe, kameranın baktığı dar koni zemini kameradan çok
        // DAHA UZAK bir noktada kesiyor, yani görünen pencere board'un
        // ÜZERİNDEN KAYIP boş zemine doğru kayıyor (kenarlarda/üstte
        // boşluk hatasının asıl sebebi buydu).
        //
        // Bunun yerine FOV ve ROTASYONA hiç dokunmuyoruz — kamerayı
        // odak noktasına (focusPoint) olan UZAKLIĞINI ölçekliyoruz
        // (dolly in/out). Bu SAF bir öteleme olduğu için görünüm hiç
        // "kaymaz", sadece büyür/küçülür — tıpkı eski orthographic
        // size mantığındaki gibi, ama açılı kamerada da doğru çalışır.
        //
        // distanceScale: ekran referanstan DAR ise (>1) kamera geri
        // çekilir (daha çok alan görünür); GENİŞ ise (<1) kamera yaklaşır
        // (daha az alan görünür, ama YATAYDA görünen genişlik sabit kalır).
        // ------------------------------------------------------------
        float distanceScale = referenceAspect / currentAspect;

        Vector3 center = focusPoint != null ? focusPoint.position : Vector3.zero;
        Vector3 offsetFromCenter = referencePosition - center;

        transform.position = center + offsetFromCenter * distanceScale;
    }
}
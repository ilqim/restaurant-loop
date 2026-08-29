using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// PERSPECTIVE kamera için: her cihazda AYNI (gerçek, zemin üzerindeki)
    /// genişliği gösterir, ama farklı yükseklikler gösterebilir — fazla
    /// dikey alan HER ZAMAN YUKARI doğru açılır, aşağı doğru asla (queue'nun
    /// 2. satırından sonrasını göstermez). Kameranın ROTASYONUNA (açısına)
    /// hiçbir zaman dokunulmaz — sadece POZİSYONU değişir.
    ///
    /// MANTIK:
    /// Kamera zemine AÇILI baktığı için, "genişlik" tanımını kameranın
    /// forward eksenine dik bir düzlemde değil, GERÇEK ZEMİN DÜZLEMİNDE
    /// (bottomAnchor'ın Y seviyesinde) hesaplıyoruz — alt-sol ve alt-sağ
    /// köşe ray'lerinin zemine değdiği noktalar arasındaki mesafe.
    ///
    /// Kamera, "alt-orta ray'inin" HER ZAMAN bottomAnchor'dan geçtiği tek
    /// bir doğru üzerinde durur (rotasyon sabit olduğu için bu doğru da
    /// sabittir). Bu doğru üzerindeki mesafe (t), zemin genişliğinin t ile
    /// DOĞRUSAL orantılı olduğu gösterilebilir — bu yüzden targetWidth'i
    /// tek bir bölme işlemiyle (kapalı form) tam olarak elde ediyoruz,
    /// yaklaşık/iteratif bir çözüm değil.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class FixedWidthPerspectiveCamera : MonoBehaviour
    {
        [Header("Sabit Genişlik")]
        [Tooltip("Her cihazda gösterilecek SABİT zemin genişliği (world unit) — bottomAnchor'ın olduğu satırda, ekranın solundan sağına kadar.")]
        [SerializeField] private float targetWidth = 10f;

        [Header("Alt Sınır (Queue 2. Satır)")]
        [Tooltip("Ekranın ALT-ORTA noktasının HER ZAMAN tam üzerinde duracağı, dünyadaki SABİT nokta — örn. queue'nun 2. satırının hemen bittiği yer. Kamera bu noktanın altını ASLA göstermez.")]
        [SerializeField] private Transform bottomAnchor;

        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            AdjustCamera();
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Editor'de Game view boyutu değiştikçe canlı güncellensin.
            AdjustCamera();
#endif
        }

        [ContextMenu("Adjust Now")]
        public void AdjustCamera()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam.orthographic) return; // Bu script SADECE perspective kameralar için.
            if (bottomAnchor == null) return;

            // Ekranın alt kenarındaki üç ray: sol, orta, sağ. Bunların
            // YÖNLERİ, kameranın rotasyonu sabit olduğu için (ve aspect
            // sadece x eksenini etkilediği için) her çağrıda güncel
            // aspect'e göre doğru hesaplanıyor.
            Vector3 centerDir = cam.ViewportPointToRay(new Vector3(0.5f, 0f, 1f)).direction;
            Vector3 leftDir = cam.ViewportPointToRay(new Vector3(0f, 0f, 1f)).direction;
            Vector3 rightDir = cam.ViewportPointToRay(new Vector3(1f, 0f, 1f)).direction;

            // Kamera pozisyonu P(t) = bottomAnchor - t * centerDir olacak
            // şekilde parametrize edildiğinde, bir ray'in (D yönünde) zemine
            // (bottomAnchor'ın Y seviyesine) değdiği nokta, bottomAnchor'a
            // göre t * offset(D) kadar kayıyor — offset(D) SABİT bir vektör
            // (t'den bağımsız), aşağıda hesaplanıyor. Bu yüzden zemin
            // genişliği t ile TAM DOĞRUSAL orantılı; tek bölme ile kesin
            // çözülebiliyor.
            Vector3 leftOffset = GroundOffsetPerUnitT(centerDir, leftDir);
            Vector3 rightOffset = GroundOffsetPerUnitT(centerDir, rightDir);

            float widthPerUnitT = Vector3.Distance(leftOffset, rightOffset);

            if (widthPerUnitT < 0.0001f)
                return; // Dejenere durum (örn. FOV=0), güvenlik için çık.

            float t = targetWidth / widthPerUnitT;

            // Kamerayı, alt-orta ray'in HER ZAMAN bottomAnchor'dan geçtiği
            // sabit doğru üzerinde, hesaplanan t mesafesine konumlandır.
            transform.position = bottomAnchor.position - centerDir.normalized * t;

            // ROTASYONA KESİNLİKLE DOKUNMUYORUZ — sabit kalıyor.
        }

        /// <summary>
        /// D yönündeki ray'in, kamera P(t) = bottomAnchor - t*centerDir
        /// konumundayken zemine (bottomAnchor.y seviyesine) değdiği noktanın,
        /// bottomAnchor'a göre olan farkının t BAŞINA (yani t=1 için) değeri.
        /// Türetim: P(t).y + s*D.y = bottomAnchor.y  =>  s = t*centerDir.y/D.y
        /// Değme noktası = P(t) + s*D = bottomAnchor + t*(-centerDir + (centerDir.y/D.y)*D)
        /// Buradaki parantez içi, t'den bağımsız SABİT bir vektördür.
        /// </summary>
        private static Vector3 GroundOffsetPerUnitT(Vector3 centerDir, Vector3 rayDir)
        {
            if (Mathf.Abs(rayDir.y) < 0.0001f)
                return Vector3.zero; // Ray zemine hiç değmiyor (yataya çok yakın) — güvenlik.

            float s = centerDir.y / rayDir.y;
            return -centerDir + rayDir * s;
        }
    }
}
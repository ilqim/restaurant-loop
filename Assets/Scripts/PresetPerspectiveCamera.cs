using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RestaurantLoop
{
    [System.Serializable]
    public struct CameraAspectPreset
    {
        [Tooltip("Genişlik / Yükseklik oranı — örn. 1170/2532 = 0.4621.")]
        public float aspect;

        [Tooltip("Bu aspect'te kameranın DOĞRU (elle bulduğun) pozisyonu.")]
        public Vector3 position;
    }

    /// <summary>
    /// Matematiksel formül yerine, birkaç yaygın çözünürlükte ELLE bulduğun
    /// doğru kamera pozisyonlarını kaydedip, aradaki (kalibre edilmemiş)
    /// aspect oranları için bunlar arasında YUMUŞAKÇA interpolasyon yapar.
    ///
    /// KULLANIM:
    /// 1) Game view'da bir çözünürlük seç (örn. 9:19.5).
    /// 2) Sahnede kamerayı ELLE, gözle doğru görünecek şekilde konumlandır
    ///    (rotasyona dokunma — o zaten hiç değişmeyecek).
    /// 3) Bu component'in Inspector'ında "Add Current As Preset" (context
    ///    menu) ile o anki aspect+pozisyonu listeye ekle.
    /// 4) Game view'da BAŞKA bir çözünürlüğe geç (örn. 16:9), kamerayı
    ///    yine elle doğru yere getir, tekrar "Add Current As Preset".
    /// 5) Birkaç farklı aspect için bunu tekrarla (ör. en dar, en geniş,
    ///    ortalama bir tane) — aradaki TÜM aspect'ler bunlar arasında
    ///    interpolasyonla otomatik doğru çıkar.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class PresetPerspectiveCamera : MonoBehaviour
    {
        [Tooltip("Aspect'e göre SIRALI tutulmasına gerek yok — script her seferinde kendi sıralar.")]
        [SerializeField] private List<CameraAspectPreset> presets = new();

        private void Awake()
        {
            Apply();
        }

        private void Update()
        {
#if UNITY_EDITOR
            // ÖNEMLİ: Sadece Editor'de PLAY MODUNDA DEĞİLKEN çalışsın —
            // Play modunda her frame kamerayı buraya geri çekip, örn.
            // QueueManager'ın Select Booster kamera animasyonunu
            // (DOTween DOMove) anında ezmesin.
            if (!Application.isPlaying)
                Apply();
#endif
        }

        [ContextMenu("Adjust Now")]
        public void Apply()
        {
            if (presets == null || presets.Count == 0)
                return;

            float aspect = (float)Screen.width / Screen.height;

            var sorted = presets.OrderBy(p => p.aspect).ToList();

            // Aralığın dışındaysa (kalibre edilenden daha dar/geniş bir
            // cihaz), en yakın uçtaki pozisyonda sabit kal (clamp) —
            // extrapolate etmiyoruz, riskli olabilir.
            if (aspect <= sorted[0].aspect)
            {
                transform.position = sorted[0].position;
                return;
            }

            if (aspect >= sorted[^1].aspect)
            {
                transform.position = sorted[^1].position;
                return;
            }

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i];
                var b = sorted[i + 1];

                if (aspect >= a.aspect && aspect <= b.aspect)
                {
                    float t = Mathf.InverseLerp(a.aspect, b.aspect, aspect);
                    transform.position = Vector3.Lerp(a.position, b.position, t);
                    return;
                }
            }

            // ROTASYONA KESİNLİKLE DOKUNMUYORUZ — sabit kalıyor.
        }

        /// <summary>
        /// Editor'de Inspector'ın sağ üst "..." menüsünden (ya da component
        /// başlığına sağ tık) çağrılabilir — o anki gerçek Game view
        /// aspect'ini ve kameranın ŞU ANKİ (elle ayarladığın) pozisyonunu
        /// listeye yeni bir satır olarak ekler.
        /// </summary>
        [ContextMenu("Add Current As Preset")]
        public void AddCurrentAsPreset()
        {
            float aspect = (float)Screen.width / Screen.height;

            presets.Add(new CameraAspectPreset
            {
                aspect = aspect,
                position = transform.position
            });

            Debug.Log($"PresetPerspectiveCamera: Yeni preset eklendi — aspect={aspect:F4}, position={transform.position}");
        }
    }
}
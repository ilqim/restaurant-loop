using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    /// <summary>
    /// Shuffle booster butonu. Basıldığında QueueManager.ShuffleQueue()'yu
    /// çağırır, hakkı PlayerData üzerinden (kalıcı olarak) 1 azaltır.
    /// Hak 0'a inince VEYA level'a göre henüz açılmamışsa buton otomatik
    /// olarak interactable=false olur.
    ///
    /// GLOW EFEKTİ: Basılır basılmaz glowImage anında tam opak (alfa=1)
    /// olur, glowHoldDuration kadar öyle kalır, sonra glowFadeDuration
    /// süresinde yumuşakça sönümlenip (alfa 1->0) normal görünüme döner.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ShuffleBoosterButton : MonoBehaviour, IBoosterLevelGate
    {
        [Header("Referanslar")]
        [Tooltip("Boş bırakılırsa sahnede otomatik aranır.")]
        [SerializeField] private QueueManager queueManager;
        [Tooltip("Boş bırakılırsa bu objenin üzerindeki Button kullanılır.")]
        [SerializeField] private Button button;
        [Tooltip("Opsiyonel — kalan hak sayısını gösteren text (örn. '99').")]
        [SerializeField] private TMP_Text countText;

        [Header("Kilitli/Açık Göstergesi (gri gösterim yerine)")]
        [Tooltip("Buton unlocked (kullanılabilir) ise SetActive(true), kilitliyse SetActive(false) olacak obje.")]
        [SerializeField] private GameObject unlockIndicator;

        [Header("Basıldığında Glow Efekti")]
        [Tooltip("Basılınca anında tam opak olacak, sonra sönümlenecek glow görseli.")]
        [SerializeField] private Image glowImage;
        [Tooltip("Glow tam opaklıkta (alfa=1) ne kadar süre kalsın, sonra sönmeye başlasın.")]
        [SerializeField] private float glowHoldDuration = 0.2f;
        [Tooltip("Glow'un sönümlenme (alfa 1->0) süresi.")]
        [SerializeField] private float glowFadeDuration = 0.3f;

        // LevelManager'dan gelen "bu level'de açık mı" bilgisi — RefreshLevelGate
        // ile güncellenir, RefreshUI bunu hak sayısıyla BİRLİKTE değerlendirir.
        private bool unlockedByLevel = true;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (queueManager == null) queueManager = FindFirstObjectByType<QueueManager>();

            // Glow başlangıçta görünmez (alfa=0).
            if (glowImage != null)
            {
                Color c = glowImage.color;
                c.a = 0f;
                glowImage.color = c;
            }
        }

        private void OnEnable()
        {
            PlayerData.BoosterCountChanged += OnBoosterCountChanged;
            RefreshLevelGate();
        }

        private void OnDisable()
        {
            PlayerData.BoosterCountChanged -= OnBoosterCountChanged;
        }

        private void OnBoosterCountChanged(BoosterType type, int newCount)
        {
            if (type == BoosterType.Shuffle)
                RefreshUI();
        }

        /// <summary>
        /// IBoosterLevelGate — LevelManager, Game sahnesi her yüklendiğinde
        /// bunu çağırır. "Şu anki level'de bu booster açık mı" bilgisini
        /// LevelManager'dan sorup UI'ı günceller.
        /// </summary>
        public void RefreshLevelGate()
        {
            unlockedByLevel = LevelManager.Instance == null ||
                               LevelManager.Instance.IsBoosterUnlocked(BoosterType.Shuffle);
            RefreshUI();
        }

        /// <summary>Butonun OnClick()'ine bağlanacak metod.</summary>
        public void OnShuffleButtonPressed()
        {
            if (!unlockedByLevel)
                return;

            if (queueManager == null)
            {
                Debug.LogWarning("ShuffleBoosterButton: Sahnede QueueManager bulunamadı.");
                return;
            }

            // TrySpendBooster hem hakkın yeterli olup olmadığını kontrol
            // eder hem de yeterliyse 1 düşürür — false dönerse (hak 0 ise)
            // shuffle hiç tetiklenmez. Normalde buton zaten interactable=false
            // olacağı için buraya 0 hakla gelinmez, ama güvenlik için kontrol var.
            if (!PlayerData.TrySpendBooster(BoosterType.Shuffle))
                return;

            // İSTEK: Shuffle GERÇEKTEN gerçekleştiğinde (hak düşürüldükten
            // hemen sonra) kendine özel bir ses çalsın — genel buton tıklama
            // sesinden AYRI bir SFX (SfxId.Shuffle).
            AudioEvents.PlayShuffle();
            queueManager.ShuffleQueue();

            PlayGlowPulse();

            // RefreshUI zaten OnBoosterCountChanged üzerinden otomatik
            // tetiklenecek (TrySpendBooster -> SetBoosterCount -> event),
            // ama tek satır ekstra maliyeti yok, garanti olsun diye burada da çağırıyoruz.
            RefreshUI();
        }

        /// <summary>
        /// Glow'u anında tam opak yapar, glowHoldDuration kadar bekletir,
        /// sonra glowFadeDuration'da yumuşakça 0'a söndürür.
        /// </summary>
        private void PlayGlowPulse()
        {
            if (glowImage == null) return;

            glowImage.DOKill();

            Color c = glowImage.color;
            c.a = 1f;
            glowImage.color = c;

            DOVirtual.DelayedCall(glowHoldDuration, () =>
            {
                if (glowImage != null)
                    glowImage.DOFade(0f, glowFadeDuration);
            });
        }

        private void RefreshUI()
        {
            int remaining = PlayerData.ShuffleBoosterCount;
            bool isUnlocked = unlockedByLevel && remaining > 0;

            if (countText != null)
                countText.text = remaining.ToString();

            if (button != null)
                button.interactable = isUnlocked;

            if (unlockIndicator != null)
                unlockIndicator.SetActive(isUnlocked);
        }
    }
}
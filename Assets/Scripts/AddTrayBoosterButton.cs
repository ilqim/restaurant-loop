using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    /// <summary>
    /// GLOW EFEKTİ: Basılır basılmaz glowImage anında tam opak (alfa=1)
    /// olur, glowHoldDuration kadar öyle kalır, sonra glowFadeDuration
    /// süresinde yumuşakça sönümlenip (alfa 1->0) normal görünüme döner.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class AddTrayBoosterButton : MonoBehaviour, IBoosterLevelGate
    {
        [Header("Referanslar")]
        [Tooltip("Boş bırakılırsa sahnede otomatik aranır.")]
        [SerializeField] private TrayManager trayManager;
        [Tooltip("Boş bırakılırsa bu objenin üzerindeki Button kullanılır.")]
        [SerializeField] private Button button;
        [Tooltip("Opsiyonel — kalan hak sayısını gösteren text.")]
        [SerializeField] private TMP_Text countText;

        [Header("Limit")]
        [Tooltip("Bir level içerisinde bu booster en fazla kaç kez kullanılabilir.")]
        [SerializeField] private int maxUsesPerLevel = 1;

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

        private int usesThisLevel = 0;

        // LevelManager'dan gelen "bu level'de açık mı" bilgisi.
        private bool unlockedByLevel = true;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (trayManager == null) trayManager = FindFirstObjectByType<TrayManager>();

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
            if (type == BoosterType.AddTray)
                RefreshUI();
        }

        /// <summary>
        /// IBoosterLevelGate — LevelManager, Game sahnesi her yüklendiğinde
        /// bunu çağırır.
        /// </summary>
        public void RefreshLevelGate()
        {
            unlockedByLevel = LevelManager.Instance == null ||
                               LevelManager.Instance.IsBoosterUnlocked(BoosterType.AddTray);
            RefreshUI();
        }

        public void OnAddTrayButtonPressed()
        {
            if (!unlockedByLevel)
                return;

            if (trayManager == null)
                trayManager = FindFirstObjectByType<TrayManager>();

            if (trayManager == null)
            {
                Debug.LogWarning("AddTrayBoosterButton: Sahnede TrayManager bulunamadı.");
                return;
            }

            if (usesThisLevel >= maxUsesPerLevel)
                return;

            if (!PlayerData.TrySpendBooster(BoosterType.AddTray))
                return;

            AudioEvents.PlayButtonClick();
            usesThisLevel++;

            trayManager.AddExtraTray();

            PlayGlowPulse();

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
            int remaining = PlayerData.AddTrayBoosterCount;
            bool isUnlocked = unlockedByLevel && remaining > 0 && usesThisLevel < maxUsesPerLevel;

            if (countText != null)
                countText.text = remaining.ToString();

            if (button != null)
                button.interactable = isUnlocked;

            if (unlockIndicator != null)
                unlockIndicator.SetActive(isUnlocked);
        }
    }
}
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    /// <summary>
    /// GLOW EFEKTİ (diğer 2 booster'dan FARKLI): Basılır basılmaz glowImage
    /// ANINDA tam opak (alfa=1) olur ve select modu bitene (yemek seçilene)
    /// KADAR öyle kalır — sabit bir süre sonra otomatik sönmez. Select modu
    /// bitince (SelectModeEnded event'i) glowFadeDuration'da yumuşakça
    /// sönümlenip normal görünüme döner. Bu sırada buton zaten
    /// interactable=false (RefreshUI'daki IsSelectModeActive kontrolü ile),
    /// yani "işlevsiz ama glow'lu" durumu doğal olarak sağlanıyor.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SelectBoosterButton : MonoBehaviour, IBoosterLevelGate
    {
        [Header("Referanslar")]
        [Tooltip("Boş bırakılırsa sahnede otomatik aranır.")]
        [SerializeField] private QueueManager queueManager;
        [Tooltip("Boş bırakılırsa bu objenin üzerindeki Button kullanılır.")]
        [SerializeField] private Button button;
        [Tooltip("Opsiyonel — kalan hak sayısını gösteren text.")]
        [SerializeField] private TMP_Text countText;

        [Header("Kilitli/Açık Göstergesi (gri gösterim yerine)")]
        [Tooltip("Buton unlocked (kullanılabilir) ise SetActive(true), kilitliyse SetActive(false) olacak obje.")]
        [SerializeField] private GameObject unlockIndicator;

        [Header("Basıldığında Glow Efekti (select modu bitene kadar sabit kalır)")]
        [Tooltip("Basılınca anında tam opak olacak, select modu bitince sönümlenecek glow görseli.")]
        [SerializeField] private Image glowImage;
        [Tooltip("Select modu bittikten sonra glow'un sönümlenme (alfa 1->0) süresi.")]
        [SerializeField] private float glowFadeDuration = 0.3f;

        // LevelManager'dan gelen "bu level'de açık mı" bilgisi.
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
            // ÖNEMLİ: Artık doğrudan RefreshUI'a değil, OnSelectModeEnded'e
            // abone oluyoruz — o hem glow'u söndürür HEM RefreshUI'ı çağırır.
            if (queueManager != null) queueManager.SelectModeEnded += OnSelectModeEnded;
            RefreshLevelGate();
        }

        private void OnDisable()
        {
            PlayerData.BoosterCountChanged -= OnBoosterCountChanged;
            if (queueManager != null) queueManager.SelectModeEnded -= OnSelectModeEnded;
        }

        private void OnBoosterCountChanged(BoosterType type, int newCount)
        {
            if (type == BoosterType.Select)
                RefreshUI();
        }

        /// <summary>
        /// IBoosterLevelGate — LevelManager, Game sahnesi her yüklendiğinde
        /// bunu çağırır.
        /// </summary>
        public void RefreshLevelGate()
        {
            unlockedByLevel = LevelManager.Instance == null ||
                               LevelManager.Instance.IsBoosterUnlocked(BoosterType.Select);
            RefreshUI();
        }

        public void OnSelectButtonPressed()
        {
            if (!unlockedByLevel)
                return;

            if (queueManager == null) queueManager = FindFirstObjectByType<QueueManager>();
            if (queueManager == null)
            {
                Debug.LogWarning("SelectBoosterButton: Sahnede QueueManager bulunamadı.");
                return;
            }

            if (queueManager.IsSelectModeActive)
                return;

            if (!PlayerData.TrySpendBooster(BoosterType.Select))
                return;

            AudioEvents.PlayButtonClick();
            queueManager.EnterSelectBoosterMode();

            // Glow'u ANINDA tam opak yap — yemek seçilene kadar (select modu
            // bitene kadar) böyle kalacak, sabit bir süre sonra sönmeyecek.
            ShowGlowInstant();

            RefreshUI();
        }

        /// <summary>Select modu bittiğinde (SelectModeEnded) tetiklenir.</summary>
        private void OnSelectModeEnded()
        {
            FadeGlowOut();
            RefreshUI();
        }

        private void ShowGlowInstant()
        {
            if (glowImage == null) return;

            glowImage.DOKill();

            Color c = glowImage.color;
            c.a = 1f;
            glowImage.color = c;
        }

        private void FadeGlowOut()
        {
            if (glowImage == null) return;

            glowImage.DOKill();
            glowImage.DOFade(0f, glowFadeDuration);
        }

        private void RefreshUI()
        {
            int remaining = PlayerData.SelectBoosterCount;
            bool isUnlocked = unlockedByLevel && remaining > 0 &&
                               (queueManager == null || !queueManager.IsSelectModeActive);

            if (countText != null)
                countText.text = remaining.ToString();

            if (button != null)
                button.interactable = isUnlocked;

            if (unlockIndicator != null)
                unlockIndicator.SetActive(isUnlocked);
        }
    }
}
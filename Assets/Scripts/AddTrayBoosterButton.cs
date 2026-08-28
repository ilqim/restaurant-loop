using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
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

        private int usesThisLevel = 0;

        // LevelManager'dan gelen "bu level'de açık mı" bilgisi.
        private bool unlockedByLevel = true;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (trayManager == null) trayManager = FindFirstObjectByType<TrayManager>();
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

            RefreshUI();
        }

        private void RefreshUI()
        {
            int remaining = PlayerData.AddTrayBoosterCount;

            if (countText != null)
                countText.text = remaining.ToString();

            if (button != null)
                button.interactable = unlockedByLevel && remaining > 0 && usesThisLevel < maxUsesPerLevel;
        }
    }
}
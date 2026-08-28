using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
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

        // LevelManager'dan gelen "bu level'de açık mı" bilgisi.
        private bool unlockedByLevel = true;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (queueManager == null) queueManager = FindFirstObjectByType<QueueManager>();
        }

        private void OnEnable()
        {
            PlayerData.BoosterCountChanged += OnBoosterCountChanged;
            if (queueManager != null) queueManager.SelectModeEnded += RefreshUI;
            RefreshLevelGate();
        }

        private void OnDisable()
        {
            PlayerData.BoosterCountChanged -= OnBoosterCountChanged;
            if (queueManager != null) queueManager.SelectModeEnded -= RefreshUI;
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

            RefreshUI();
        }

        private void RefreshUI()
        {
            int remaining = PlayerData.SelectBoosterCount;

            if (countText != null)
                countText.text = remaining.ToString();

            if (button != null)
                button.interactable = unlockedByLevel && remaining > 0 &&
                                       (queueManager == null || !queueManager.IsSelectModeActive);
        }
    }
}
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    [RequireComponent(typeof(Button))]
    public class SelectBoosterButton : MonoBehaviour
    {
        [Header("Referanslar")]
        [Tooltip("Boş bırakılırsa sahnede otomatik aranır.")]
        [SerializeField] private QueueManager queueManager;
        [Tooltip("Boş bırakılırsa bu objenin üzerindeki Button kullanılır.")]
        [SerializeField] private Button button;
        [Tooltip("Opsiyonel — kalan hak sayısını gösteren text.")]
        [SerializeField] private TMP_Text countText;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (queueManager == null) queueManager = FindFirstObjectByType<QueueManager>();
        }

        private void OnEnable()
        {
            PlayerData.BoosterCountChanged += OnBoosterCountChanged;
            if (queueManager != null) queueManager.SelectModeEnded += RefreshUI;
            RefreshUI();
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

        public void OnSelectButtonPressed()
        {
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
                button.interactable = remaining > 0 && (queueManager == null || !queueManager.IsSelectModeActive);
        }
    }
}
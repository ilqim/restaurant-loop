using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    public class EconomyDisplayUI : MonoBehaviour
    {
        [Header("Coin UI (Assign TMP or Text)")]
        [SerializeField] private TextMeshProUGUI coinTextTMP;
        [SerializeField] private Text coinTextLegacy;

        [Header("Heart UI (Assign TMP or Text)")]
        [SerializeField] private TextMeshProUGUI heartTextTMP;
        [SerializeField] private Text heartTextLegacy;

        private void OnEnable()
        {
            PlayerData.CoinsChanged += UpdateCoinVisual;
            PlayerData.HeartsChanged += UpdateHeartVisual;

            UpdateCoinVisual(PlayerData.Coins);
            UpdateHeartVisual(PlayerData.Hearts);
        }

        private void OnDisable()
        {
            PlayerData.CoinsChanged -= UpdateCoinVisual;
            PlayerData.HeartsChanged -= UpdateHeartVisual;
        }

        private void UpdateCoinVisual(int count)
        {
            string formatted = count.ToString();
            if (coinTextTMP != null) coinTextTMP.text = formatted;
            if (coinTextLegacy != null) coinTextLegacy.text = formatted;
        }

        private void UpdateHeartVisual(int count)
        {
            string formatted = $"{count}/{PlayerData.MaxHearts}";
            if (heartTextTMP != null) heartTextTMP.text = formatted;
            if (heartTextLegacy != null) heartTextLegacy.text = formatted;
        }
    }
}
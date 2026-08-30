using System;
using TMPro;
using Unity.VisualScripting;
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
        [SerializeField] private string fullText = "FULL";

        [Header("Heart Timer UI")]
        [SerializeField] private TextMeshProUGUI timerTextTMP;

        private void OnEnable()
        {
            PlayerData.CoinsChanged += UpdateCoinVisual;
            PlayerData.HeartsChanged += UpdateHeartVisual;

            UpdateCoinVisual(PlayerData.Coins);
            UpdateHeartVisual(PlayerData.Hearts);
            UpdateTimerVisual();
        }

        private void OnDisable()
        {
            PlayerData.CoinsChanged -= UpdateCoinVisual;
            PlayerData.HeartsChanged -= UpdateHeartVisual;
        }

        private void Update()
        {
            PlayerData.CheckAndRegenerateHearts();
            UpdateTimerVisual();
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

        private void UpdateTimerVisual()
        {
            if (timerTextTMP == null)
                return;

            string timerString;

            if (PlayerData.Hearts >= PlayerData.MaxHearts)
            {
                timerString = fullText;
            }
            else
            {
                TimeSpan remaining = PlayerData.GetTimeToNextHeart();
                timerString = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }

            if (timerTextTMP != null) timerTextTMP.text = timerString;
        }
    }
}
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    public class LevelCompleteUI : MonoBehaviour
    {
        [Header("Panel References")]
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private GameObject popupContent;

        [Header("Coin Display (TMP or Legacy Text)")]
        [SerializeField] private TextMeshProUGUI earnedCoinsTMP;
        [SerializeField] private Text earnedCoinsLegacy;

        [Header("Animation Settings")]
        [SerializeField] private float fadeDuration = 0.35f;
        [SerializeField] private float countUpDuration = 0.8f;

        private void Awake()
        {
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (popupContent != null)
                popupContent.SetActive(false);
        }

        public void Show(int earnedAmount)
        {
            if (popupContent != null)
                popupContent.SetActive(true);

            StartCoroutine(ShowSequenceRoutine(earnedAmount));
        }

        private IEnumerator ShowSequenceRoutine(int earnedAmount)
        {
            // 1. Fade in panel
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.blocksRaycasts = true;
                float elapsed = 0f;

                while (elapsed < fadeDuration)
                {
                    elapsed += Time.deltaTime;
                    panelCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
                    yield return null;
                }

                panelCanvasGroup.alpha = 1f;
                panelCanvasGroup.interactable = true;
            }

            // 2. Count up coins
            float countTimer = 0f;
            while (countTimer < countUpDuration)
            {
                countTimer += Time.deltaTime;
                int currentDisplay = Mathf.RoundToInt(Mathf.Lerp(0, earnedAmount, countTimer / countUpDuration));
                SetCoinText($"+{currentDisplay}");
                yield return null;
            }

            SetCoinText($"+{earnedAmount}");
        }

        private void SetCoinText(string text)
        {
            if (earnedCoinsTMP != null) earnedCoinsTMP.text = text;
            if (earnedCoinsLegacy != null) earnedCoinsLegacy.text = text;
        }

        public void OnContinueButtonPressed()
        {
            AudioEvents.PlayButtonClick();

            if (panelCanvasGroup != null)
                panelCanvasGroup.interactable = false;

            // Return to Main Menu via SceneFlowManager
            if (SceneFlowManager.Instance != null)
            {
                SceneFlowManager.Instance.LoadMainMenuScene();
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            }
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
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

        [Header("Coin Animation Settings (DOTween)")]
        [SerializeField] private GameObject coinPrefab;
        [SerializeField] private Transform coinContainer;
        [SerializeField] private RectTransform coinStartPoint;
        [SerializeField] private RectTransform coinTargetPoint;
        [SerializeField] private int animatedCoinCount = 15;
        [SerializeField] private float moveDuration = 0.8f;
        [SerializeField] private float totalDelay = 0.4f;
        [SerializeField] private float randomOffsetRange = 75f;
        [SerializeField] private Ease moveEase = Ease.InBack;

        [Header("Animation Settings")]
        [SerializeField] private float fadeDuration = 0.35f;

        private readonly List<GameObject> activeCoins = new();

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
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.DOFade(1f, fadeDuration);
                yield return new WaitForSeconds(fadeDuration);
                panelCanvasGroup.interactable = true;
            }

            // 2. Play Coin Fly Animation
            if (coinPrefab != null && coinStartPoint != null && coinTargetPoint != null)
            {
                PlayCoinFlyAnimation(earnedAmount);
            }
            else
            {
                SetCoinText($"+{earnedAmount}");
            }
        }

        private void PlayCoinFlyAnimation(int earnedAmount)
        {
            float delayPerCoin = totalDelay / Mathf.Max(1, animatedCoinCount);
            int displayedCoins = 0;
            int stepAmount = Mathf.Max(1, earnedAmount / animatedCoinCount);

            Transform parent = coinContainer != null ? coinContainer : transform;

            for (int i = 0; i < animatedCoinCount; i++)
            {
                GameObject coin = Instantiate(coinPrefab, parent);
                activeCoins.Add(coin);

                RectTransform rect = coin.GetComponent<RectTransform>();
                Vector2 randomOffset = new Vector2(
                    Random.Range(-randomOffsetRange, randomOffsetRange),
                    Random.Range(-randomOffsetRange, randomOffsetRange)
                );

                rect.position = coinStartPoint.position + (Vector3)randomOffset;
                rect.localScale = Vector3.one * 0.1f;

                float delay = i * delayPerCoin;

                // Scale up
                rect.DOScale(1f, 0.2f).SetDelay(delay);

                // Move to target
                rect.DOMove(coinTargetPoint.position, moveDuration)
                    .SetDelay(delay)
                    .SetEase(moveEase)
                    .OnComplete(() =>
                    {
                        AudioEvents.PlayCoinEarn();
                        displayedCoins = Mathf.Min(earnedAmount, displayedCoins + stepAmount);
                        SetCoinText($"+{displayedCoins}");

                        // Punch scale the target UI icon if desired
                        coinTargetPoint.DOPunchScale(Vector3.one * 0.15f, 0.1f, 5, 1);

                        activeCoins.Remove(coin);
                        Destroy(coin);
                    });
            }

            // Final text sync
            DOVirtual.DelayedCall(totalDelay + moveDuration, () =>
            {
                SetCoinText($"+{earnedAmount}");
            });
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

            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.LoadMainMenuScene();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}
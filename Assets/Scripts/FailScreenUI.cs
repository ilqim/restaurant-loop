using System.Collections;
using DG.Tweening;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Level FAIL olduğunda gösterilen panel. LevelCompleteUI ile AYNI
    /// fade-in mantığı — sadece coin animasyonu yok (kazanç olmadığı için).
    /// </summary>
    public class FailScreenUI : MonoBehaviour
    {
        [Header("Panel References")]
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private GameObject popupContent;

        [Header("Animation Settings")]
        [SerializeField] private float fadeDuration = 0.35f;

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

        public void Show()
        {
            if (popupContent != null)
                popupContent.SetActive(true);

            StartCoroutine(ShowSequenceRoutine());
        }

        private IEnumerator ShowSequenceRoutine()
        {
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.blocksRaycasts = true;
                panelCanvasGroup.alpha = 0f;
                // PAUSE-GÜVENLİ: SetUpdate(true) — GameManager, Fail'den ~1sn
                // sonra Time.timeScale = 0 yapıyor. Bu tween scaled kalsaydı,
                // pause tam bu fade sırasında tetiklenirse yarım kalırdı.
                panelCanvasGroup.DOFade(1f, fadeDuration).SetUpdate(true);
                // PAUSE-GÜVENLİ: WaitForSecondsRealtime — aynı sebep.
                yield return new WaitForSecondsRealtime(fadeDuration);
                panelCanvasGroup.interactable = true;
            }
        }

        /// <summary>"Tekrar Dene" butonuna bağla — level'i (SceneFlowManager ile) yeniden yükler.</summary>
        public void OnRetryButtonPressed()
        {
            AudioEvents.PlayButtonClick();

            if (panelCanvasGroup != null)
                panelCanvasGroup.interactable = false;

            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.LoadGameplayScene();
            else
                Debug.LogWarning("FailScreenUI: SceneFlowManager.Instance bulunamadı — retry yapılamadı.");
        }

        /// <summary>"Ana Menü" butonuna bağla — LevelCompleteUI'daki Continue ile aynı mantık.</summary>
        public void OnMainMenuButtonPressed()
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
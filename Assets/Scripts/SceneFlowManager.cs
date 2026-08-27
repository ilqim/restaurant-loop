using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RestaurantLoop
{
    public class SceneFlowManager : MonoBehaviour
    {
        public static SceneFlowManager Instance { get; private set; }

        [Header("Scene Build Names / Indices")]
        [SerializeField] private string mainMenuSceneName = "Main Menu";
        [SerializeField] private string gameplaySceneName = "Game";

        [Header("Startup Screen UI")]
        [SerializeField] private CanvasGroup startupCanvasGroup;
        [SerializeField] private float startupHoldDuration = 2.0f;
        [SerializeField] private float startupFadeDuration = 0.5f;

        [Header("Loading Screen Overlay UI")]
        [SerializeField] private CanvasGroup loadingCanvasGroup;
        [SerializeField] private Slider loadingProgressBar;
        [SerializeField] private float fadeDuration = 0.3f;
        [SerializeField] private float minimumLoadingScreenTime = 0.8f;

        // Tracks whether the game has already completed its initial boot sequence
        private static bool hasCompletedStartupSequence = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (loadingCanvasGroup != null)
            {
                loadingCanvasGroup.alpha = 0f;
                loadingCanvasGroup.blocksRaycasts = false;
            }
        }

        private void Start()
        {
            // If we've already done the startup screen in this session, keep it hidden
            if (hasCompletedStartupSequence)
            {
                HideStartupScreenImmediately();
                AudioEvents.PlayMusic();
                return;
            }

            StartCoroutine(StartupSequenceRoutine());
        }

        // ==========================================
        // STARTUP FLOW
        // ==========================================
        private IEnumerator StartupSequenceRoutine()
        {
            if (startupCanvasGroup != null)
            {
                startupCanvasGroup.alpha = 1f;
                startupCanvasGroup.blocksRaycasts = true;

                yield return new WaitForSeconds(startupHoldDuration);

                // Fade out Startup Screen
                float elapsed = 0f;
                while (elapsed < startupFadeDuration)
                {
                    elapsed += Time.deltaTime;
                    startupCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / startupFadeDuration);
                    yield return null;
                }

                HideStartupScreenImmediately();
            }

            hasCompletedStartupSequence = true;
            AudioEvents.PlayMusic();
        }

        private void HideStartupScreenImmediately()
        {
            if (startupCanvasGroup != null)
            {
                startupCanvasGroup.alpha = 0f;
                startupCanvasGroup.interactable = false;
                startupCanvasGroup.blocksRaycasts = false;
                startupCanvasGroup.gameObject.SetActive(false);
            }
        }

        // ==========================================
        // SCENE TRANSITIONS
        // ==========================================
        public void LoadGameplayScene()
        {
            StartCoroutine(LoadSceneAsyncRoutine(gameplaySceneName, isEnteringGameplay: true));
        }

        public void LoadMainMenuScene()
        {
            StartCoroutine(LoadSceneAsyncRoutine(mainMenuSceneName, isEnteringGameplay: false));
        }

        private IEnumerator LoadSceneAsyncRoutine(string sceneName, bool isEnteringGameplay)
        {
            // 1. Fade IN Loading Screen
            if (loadingProgressBar != null)
                loadingProgressBar.value = 0f;

            yield return StartCoroutine(FadeCanvasGroup(loadingCanvasGroup, 1f, fadeDuration));

            if (isEnteringGameplay)
            {
                AudioEvents.StopMusic();
            }

            // 2. Load Target Scene Asynchronously
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            op.allowSceneActivation = false;

            float loadTimer = 0f;

            while (!op.isDone)
            {
                loadTimer += Time.deltaTime;
                
                // Normalizes Unity's 0.0 - 0.9 progress range
                float progress = Mathf.Clamp01(op.progress / 0.9f);

                if (loadingProgressBar != null)
                    loadingProgressBar.value = progress;

                // Wait until scene is loaded in memory AND minimum load duration has passed
                if (op.progress >= 0.9f && loadTimer >= minimumLoadingScreenTime)
                {
                    op.allowSceneActivation = true;
                }

                yield return null;
            }

            // Ensure startup screen remains disabled if returning to Main Menu
            HideStartupScreenImmediately();

            if (!isEnteringGameplay)
            {
                AudioEvents.PlayMusic();
            }

            // 3. Fade OUT Loading Screen
            yield return StartCoroutine(FadeCanvasGroup(loadingCanvasGroup, 0f, fadeDuration));
        }

        private IEnumerator FadeCanvasGroup(CanvasGroup group, float targetAlpha, float duration)
        {
            if (group == null) yield break;

            group.blocksRaycasts = targetAlpha > 0.01f;
            group.interactable = targetAlpha > 0.01f;
            float startAlpha = group.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                group.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
                yield return null;
            }

            group.alpha = targetAlpha;
        }
    }
}
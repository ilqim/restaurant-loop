using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

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

        [Header("Loading Text Animation")]
        [SerializeField] private TMP_Text loadingText;
        [SerializeField] private float loadingTextInterval = 0.25f;

        [Header("Loading Screen")]
        [SerializeField] private float fadeDuration = 0.3f;
        [SerializeField] private float minimumLoadingScreenTime = 0.8f;

        private static bool hasCompletedStartupSequence = false;

        private Coroutine loadingTextCoroutine;

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
                loadingCanvasGroup.interactable = false;
            }
        }

        private void Start()
        {
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

                // PAUSE-GÜVENLİ: WaitForSecondsRealtime — Time.timeScale = 0
                // olsa bile (ör. bir önceki Win/Fail'den kalma durumda) bu
                // bekleme donmasın diye.
                yield return new WaitForSecondsRealtime(startupHoldDuration);

                float elapsed = 0f;

                while (elapsed < startupFadeDuration)
                {
                    // PAUSE-GÜVENLİ: Time.unscaledDeltaTime — Time.timeScale
                    // = 0 iken normal Time.deltaTime hep 0 döner, bu da bu
                    // döngünün sonsuza kadar takılı kalmasına yol açardı.
                    elapsed += Time.unscaledDeltaTime;

                    startupCanvasGroup.alpha =
                        Mathf.Lerp(1f, 0f, elapsed / startupFadeDuration);

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
            StartCoroutine(
                LoadSceneAsyncRoutine(
                    gameplaySceneName,
                    isEnteringGameplay: true
                )
            );
        }

        public void LoadMainMenuScene()
        {
            StartCoroutine(
                LoadSceneAsyncRoutine(
                    mainMenuSceneName,
                    isEnteringGameplay: false
                )
            );
        }

        private IEnumerator LoadSceneAsyncRoutine(
            string sceneName,
            bool isEnteringGameplay)
        {
            // 1. Fade IN Loading Screen
            yield return StartCoroutine(
                FadeCanvasGroup(
                    loadingCanvasGroup,
                    1f,
                    fadeDuration
                )
            );

            // Start Loading text animation
            StartLoadingTextAnimation();

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
                // PAUSE-GÜVENLİ: Time.unscaledDeltaTime — bu, "buton
                // çalışmıyor" gibi görünen asıl sorunu çözüyor. Time.timeScale
                // = 0 iken normal Time.deltaTime hep 0 döndüğü için, bu
                // sayaç asla ilerlemez ve sahne geçişi sessizce sonsuza
                // kadar takılı kalırdı (RETRY/HOME butonlarına basınca
                // "hiçbir şey olmuyormuş" gibi görünmesinin sebebi buydu).
                loadTimer += Time.unscaledDeltaTime;

                // Unity loads the scene to 90%, then waits for activation.
                if (op.progress >= 0.9f &&
                    loadTimer >= minimumLoadingScreenTime)
                {
                    op.allowSceneActivation = true;
                }

                yield return null;
            }

            // Stop Loading text animation
            StopLoadingTextAnimation();

            // Ensure startup screen remains disabled
            HideStartupScreenImmediately();

            if (!isEnteringGameplay)
            {
                AudioEvents.PlayMusic();
            }

            // 3. Fade OUT Loading Screen
            yield return StartCoroutine(
                FadeCanvasGroup(
                    loadingCanvasGroup,
                    0f,
                    fadeDuration
                )
            );
        }

        // ==========================================
        // LOADING TEXT ANIMATION
        // ==========================================

        private void StartLoadingTextAnimation()
        {
            if (loadingText == null)
                return;

            StopLoadingTextAnimation();

            loadingTextCoroutine =
                StartCoroutine(LoadingTextAnimationRoutine());
        }

        private void StopLoadingTextAnimation()
        {
            if (loadingTextCoroutine != null)
            {
                StopCoroutine(loadingTextCoroutine);
                loadingTextCoroutine = null;
            }
        }

        private IEnumerator LoadingTextAnimationRoutine()
        {
            string[] loadingStates =
            {
                "Loading...",
                "Loading..",
                "Loading."
            };

            int index = 0;

            while (true)
            {
                loadingText.text = loadingStates[index];

                index++;

                if (index >= loadingStates.Length)
                    index = 0;

                // PAUSE-GÜVENLİ: WaitForSecondsRealtime — aynı sebep.
                yield return new WaitForSecondsRealtime(loadingTextInterval);
            }
        }

        // ==========================================
        // CANVAS FADE
        // ==========================================

        private IEnumerator FadeCanvasGroup(
            CanvasGroup group,
            float targetAlpha,
            float duration)
        {
            if (group == null)
                yield break;

            group.blocksRaycasts = targetAlpha > 0.01f;
            group.interactable = targetAlpha > 0.01f;

            float startAlpha = group.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                // PAUSE-GÜVENLİ: Time.unscaledDeltaTime — aynı sebep.
                elapsed += Time.unscaledDeltaTime;

                group.alpha =
                    Mathf.Lerp(
                        startAlpha,
                        targetAlpha,
                        elapsed / duration
                    );

                yield return null;
            }

            group.alpha = targetAlpha;
        }
    }
}
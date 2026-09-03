using System.Collections;
using TMPro;
using UnityEngine;

namespace RestaurantLoop
{
    public class CurrencyBar : MonoBehaviour
    {
        public static CurrencyBar Instance { get; private set; }

        [Header("Coin Fly Target Anchor")]
        [Tooltip("The RectTransform coins will fly to (e.g., the coin icon on this bar). If empty, uses this transform.")]
        [SerializeField] private RectTransform coinTargetAnchor;

        public RectTransform CoinTargetAnchor => coinTargetAnchor != null ? coinTargetAnchor : GetComponent<RectTransform>();

        [Header("UI")]
        [SerializeField] private TMP_Text coinsText;

        [Header("Sayaç Animasyonu (parametrik)")]
        [Tooltip("Coin kazanıldığında eski değerden yeni değere sayarak artma süresi (saniye).")]
        [SerializeField] private float countUpDuration = 0.6f;
        [Tooltip("Sayaç animasyonunun hız eğrisi (0-1 aralığında ilerleme).")]
        [SerializeField] private AnimationCurve countUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Persistence")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        private int displayedCoins;
        private Coroutine countRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (dontDestroyOnLoad)
            {
                // If it's a child of a canvas, detach to root so DontDestroyOnLoad works properly,
                // or ensure its root Canvas has DontDestroyOnLoad.
                if (transform.parent != null)
                {
                    DontDestroyOnLoad(transform.root.gameObject);
                }
                else
                {
                    DontDestroyOnLoad(gameObject);
                }
            }

            displayedCoins = PlayerData.Coins;
            RefreshTextInstant();
        }

        private void OnEnable()
        {
            PlayerData.CoinsChanged += HandleCoinsChanged;
            displayedCoins = PlayerData.Coins;
            RefreshTextInstant();
        }

        private void OnDisable()
        {
            PlayerData.CoinsChanged -= HandleCoinsChanged;
        }

        private void HandleCoinsChanged(int totalCoins)
        {
            if (countRoutine == null)
            {
                displayedCoins = totalCoins;
                RefreshTextInstant();
            }
        }

        public void SetVisible(bool isVisible)
        {
            gameObject.SetActive(isVisible);
            if (isVisible)
            {
                displayedCoins = PlayerData.Coins;
                RefreshTextInstant();
            }
        }

        private void RefreshTextInstant()
        {
            if (coinsText != null)
                coinsText.text = displayedCoins.ToString();
        }

        public void AddCoinsAnimated(int amount)
        {
            if (amount <= 0) return;

            int from = PlayerData.Coins;
            PlayerData.AddCoins(amount);
            int to = PlayerData.Coins;

            if (countRoutine != null)
                StopCoroutine(countRoutine);

            countRoutine = StartCoroutine(CountRoutine(from, to, countUpDuration));
        }

        private IEnumerator CountRoutine(int from, int to, float duration)
        {
            duration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = countUpCurve.Evaluate(Mathf.Clamp01(elapsed / duration));
                displayedCoins = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
                RefreshTextInstant();
                yield return null;
            }

            displayedCoins = to;
            RefreshTextInstant();
            countRoutine = null;
        }
    }
}
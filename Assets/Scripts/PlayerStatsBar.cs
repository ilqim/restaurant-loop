using System;
using System.Collections;
using TMPro;
using UnityEngine;
using DG.Tweening;

namespace RestaurantLoop
{
    /// <summary>
    /// Oyuncunun anlık PARA ve CAN (kalp) durumunu gösteren üst bar.
    ///
    /// ÖNEMLİ MİMARİ KARAR: Bu artık DontDestroyOnLoad bir singleton DEĞİL.
    /// Gerçek veri zaten PlayerData içinde (statik, sahneler arası kalıcı)
    /// tutuluyor — bu component SADECE o veriyi EKRANDA GÖSTEREN, bulunduğu
    /// sahneye/Canvas'a özel, sıradan bir UI parçası.
    ///
    /// Bunun faydası: Main Menu'deki bar'ın konumu/boyutu ile Game
    /// sahnesindeki (örn. WinFailCanvas'taki) bar'ın konumu/boyutu TAMAMEN
    /// BAĞIMSIZ olur — her ekran kendi PlayerStatsBar instance'ını, kendi
    /// Canvas'ı içinde, istediği yerde/boyutta tutar. Sahne geçişlerinde
    /// "hangi obje hayatta kaldı, kim kimi Destroy etti" karmaşası da
    /// tamamen ortadan kalkar.
    ///
    /// Değerler her zaman OnEnable'da PlayerData'dan taze okunur, bu yüzden
    /// hangi sahnede/hangi objede durursa dursun her zaman doğru gösterir.
    /// Aynı anda birden fazla PlayerStatsBar (Main Menu'nünki + WinFail'inki)
    /// sahnede bulunabilir; hepsi bağımsızdır, çakışmazlar.
    /// </summary>
    public class PlayerStatsBar : MonoBehaviour
    {
        [Header("Coin Fly Target Anchor")]
        [Tooltip("Coin uçuşma animasyonunun hedefi (örn. bu bar'daki coin ikonu). Boş bırakılırsa bu objenin RectTransform'u kullanılır.")]
        [SerializeField] private RectTransform coinTargetAnchor;

        public RectTransform CoinTargetAnchor => coinTargetAnchor != null ? coinTargetAnchor : GetComponent<RectTransform>();

        [Header("Para UI")]
        [SerializeField] private TMP_Text coinsText;

        [Header("Can UI")]
        [SerializeField] private TMP_Text heartsText;
        [Tooltip("Can DOLU DEĞİLKEN bir sonraki cana kaç dakika/saniye kaldığını " +
                 "'MM:SS' formatında gösteren metin. Can TAM DOLUYSA yerine " +
                 "'Hearts Full Text' yazılır. PlayerData.NextHeartRegenTime zaten " +
                 "kalıcı (PlayerPrefs) olduğundan Main Menu'de de senkron devam eder.")]
        [SerializeField] private TMP_Text heartRegenTimerText;
        [Tooltip("Can tam doluyken (Hearts >= MaxHearts) 'Heart Regen Timer Text' alanında gösterilecek metin.")]
        [SerializeField] private string heartsFullText = "FULL";
        [Tooltip("Can azalınca kısa bir 'punch' (küçülüp büyüme) efekti oynatılacak ikon/obje. Boş bırakılabilir.")]
        [SerializeField] private RectTransform heartPunchTarget;
        [SerializeField] private float heartPunchScale = 0.25f;
        [SerializeField] private float heartPunchDuration = 0.3f;
        [Tooltip("Bar/ekran görünür olduktan sonra, punch efekti başlamadan önce ne kadar beklensin. " +
                 "0 verirsen bar açılır açılmaz anında zıplar — istenen 'bir an sonra' hissi için " +
                 "0.3-0.5 arası bir değer öner.")]
        [SerializeField] private float heartPunchDelay = 0.4f;

        [Header("Sayaç Animasyonu (parametrik)")]
        [Tooltip("Coin kazanıldığında eski değerden yeni değere sayarak artma süresi (saniye).")]
        [SerializeField] private float countUpDuration = 0.6f;
        [Tooltip("Sayaç animasyonunun hız eğrisi (0-1 aralığında ilerleme).")]
        [SerializeField] private AnimationCurve countUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private int displayedCoins;
        private int displayedHearts;
        private Coroutine coinCountRoutine;
        private Coroutine heartRegenTimerRoutine;

        private void Awake()
        {
            displayedCoins = PlayerData.Coins;
            displayedHearts = PlayerData.Hearts;
            RefreshCoinsInstant();
            RefreshHeartsInstant();
            RefreshHeartRegenTimerInstant();
        }

        private void OnEnable()
        {
            PlayerData.CoinsChanged += HandleCoinsChanged;
            PlayerData.HeartsChanged += HandleHeartsChanged;

            // Her aktif olduğunda PlayerData'dan TAZE okuyoruz — bu obje
            // hiçbir zaman "eski sahneden kalma yanlış değer" göstermez.
            displayedCoins = PlayerData.Coins;
            displayedHearts = PlayerData.Hearts;
            RefreshCoinsInstant();
            RefreshHeartsInstant();
            RefreshHeartRegenTimerInstant();

            // Geri sayım metni saniyede bir güncellensin — PlayerData.Hearts
            // okuması zaten CheckAndRegenerateHearts()'i tetikliyor, yani bu
            // döngü hem metni günceller hem de can dolduğunda otomatik
            // HeartsChanged event'inin ateşlenmesini garantiler.
            if (heartRegenTimerRoutine != null)
                StopCoroutine(heartRegenTimerRoutine);
            heartRegenTimerRoutine = StartCoroutine(HeartRegenTimerRoutine());
        }

        private void OnDisable()
        {
            PlayerData.CoinsChanged -= HandleCoinsChanged;
            PlayerData.HeartsChanged -= HandleHeartsChanged;

            if (heartRegenTimerRoutine != null)
            {
                StopCoroutine(heartRegenTimerRoutine);
                heartRegenTimerRoutine = null;
            }
        }

        private void HandleCoinsChanged(int totalCoins)
        {
            // Zaten bir sayaç animasyonu sürüyorsa (AddCoinsAnimated), onun
            // üstüne binmesin — o zaten kendi hedefine doğru ilerliyor.
            if (coinCountRoutine == null)
            {
                displayedCoins = totalCoins;
                RefreshCoinsInstant();
            }
        }

        private void HandleHeartsChanged(int newHearts)
        {
            displayedHearts = newHearts;
            RefreshHeartsInstant();
            RefreshHeartRegenTimerInstant();
        }

        /// <summary>
        /// Saniyede bir "MM:SS" geri sayım metnini günceller. Bar pasifken
        /// (OnDisable'da) durur, tekrar aktif olunca (OnEnable) kaldığı
        /// yerden değil, PlayerData'daki GERÇEK zamandan devam eder — çünkü
        /// zaten zaman PlayerPrefs'te (DateTime) tutuluyor, coroutine'in
        /// kendisi sadece bir GÖSTERGE, veri kaynağı değil.
        /// </summary>
        private IEnumerator HeartRegenTimerRoutine()
        {
            while (true)
            {
                RefreshHeartRegenTimerInstant();
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private void RefreshHeartRegenTimerInstant()
        {
            if (heartRegenTimerText == null) return;

            if (PlayerData.Hearts >= PlayerData.MaxHearts)
            {
                heartRegenTimerText.text = heartsFullText;
                return;
            }

            TimeSpan remaining = PlayerData.GetTimeToNextHeart();
            int minutes = Mathf.Max(0, (int)remaining.TotalMinutes);
            int seconds = Mathf.Max(0, remaining.Seconds);
            heartRegenTimerText.text = $"{minutes:00}:{seconds:00}";
        }

        public void SetVisible(bool isVisible)
        {
            gameObject.SetActive(isVisible);
            if (isVisible)
            {
                displayedCoins = PlayerData.Coins;
                displayedHearts = PlayerData.Hearts;
                RefreshCoinsInstant();
                RefreshHeartsInstant();
                RefreshHeartRegenTimerInstant();
            }
        }

        private void RefreshCoinsInstant()
        {
            if (coinsText != null)
                coinsText.text = displayedCoins.ToString();
        }

        private void RefreshHeartsInstant()
        {
            if (heartsText != null)
                heartsText.text = displayedHearts.ToString();
        }

        /// <summary>
        /// Coin kazanma — eski değerden yeni değere doğru sayarak artırır
        /// (WIN ekranında kullanılır). PlayerData.AddCoins'i KENDİSİ çağırır.
        /// </summary>
        public void AddCoinsAnimated(int amount)
        {
            if (amount <= 0) return;

            int from = PlayerData.Coins;
            PlayerData.AddCoins(amount);
            int to = PlayerData.Coins;

            // GÜVENLİK: Obje pasifse StartCoroutine ÇALIŞMAZ (Unity hata
            // fırlatır). Bu normalde YANLIŞ KABLOLAMAYI işaret eder (bar
            // SetVisible(true) ile önce açılmalıydı) — ama coin'in en
            // azından KAYBOLMAMASI için burada anında (animasyonsuz)
            // güncelliyoruz, oyunu çökertmiyoruz.
            if (!gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"PlayerStatsBar ('{name}'): AddCoinsAnimated çağrıldığında obje PASİF durumda — " +
                                  "coin animasyonsuz eklendi. GameManager/LevelCompleteUI'deki bar referanslarının " +
                                  "AYNI objeye işaret ettiğinden ve SetVisible(true)'ın animasyondan ÖNCE " +
                                  "çağrıldığından emin ol.");
                displayedCoins = to;
                RefreshCoinsInstant();
                return;
            }

            if (coinCountRoutine != null)
                StopCoroutine(coinCountRoutine);

            coinCountRoutine = StartCoroutine(CoinCountRoutine(from, to, countUpDuration));
        }

        private IEnumerator CoinCountRoutine(int from, int to, float duration)
        {
            duration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = countUpCurve.Evaluate(Mathf.Clamp01(elapsed / duration));
                displayedCoins = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
                RefreshCoinsInstant();
                yield return null;
            }

            displayedCoins = to;
            RefreshCoinsInstant();
            coinCountRoutine = null;
        }

        /// <summary>
        /// Can kaybı görseli — PlayerData.ConsumeHeart() ÇAĞIRAN TARAF
        /// (GameManager.FailLevel()) tarafından ÖNCEDEN yapılmış olmalı;
        /// bu metod SADECE görseli günceller: metni HEMEN yeni (azalmış)
        /// değere çeker, kalp ikonuna ise heartPunchDelay kadar bekledikten
        /// sonra TEK SEFERLİK bir "punch" efekti oynatır — bar açılır
        /// açılmaz değil, bir an sonra (FAIL ekranında kullanılır).
        /// </summary>
        public void PlayHeartLossAnimation()
        {
            displayedHearts = PlayerData.Hearts;
            RefreshHeartsInstant();

            if (heartPunchTarget == null)
                return;

            heartPunchTarget.DOKill();
            heartPunchTarget.localScale = Vector3.one;

            // TEK SEFERLİK punch — DOVirtual.DelayedCall zaten bir kere
            // çalışır, bu yüzden art arda çağrılsa bile üst üste binmiyor
            // (bir önceki DOKill ile temizleniyor).
            DOVirtual.DelayedCall(heartPunchDelay, () =>
            {
                if (heartPunchTarget == null) return;

                heartPunchTarget.DOPunchScale(Vector3.one * heartPunchScale, heartPunchDuration, 8, 0.8f)
                                 .SetUpdate(true); // pause-güvenli: Time.timeScale=0 olsa bile oynasın
            }).SetUpdate(true); // pause-güvenli: gecikme de Time.timeScale'e bağlı kalmasın
        }
    }
}
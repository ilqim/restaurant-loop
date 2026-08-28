using System.Collections;
using TMPro;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Coin bar'ı. ÖNEMLİ: Bu component PERSISTENT (DontDestroyOnLoad) DEĞİL —
    /// Main Menu ve Game sahnelerinin HER BİRİNE kendi bağımsız instance'ını
    /// koy (aynı script, iki ayrı obje). Buna rağmen ikisi de doğru sayıyı
    /// gösterir çünkü asıl kalıcı olan şey UI objesi değil, PlayerData
    /// (static sınıf, PlayerPrefs üzerinden zaten kalıcı) — UI'ın kendisinin
    /// kalıcı olmasına hiç gerek yok.
    ///
    /// Bunu persistent yapmamanın avantajı: iki farklı sahnenin Canvas'ları
    /// arasında render sırası (hangisi önde/arkada çizilecek) karışıklığı
    /// hiç yaşanmıyor — her sahne sadece KENDİ Canvas'ını, kendi coin
    /// bar'ıyla birlikte çiziyor, başka sahneden kalma bir obje devrede değil.
    /// </summary>
    public class CurrencyBar : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text coinsText;

        [Header("Sayaç Animasyonu (parametrik)")]
        [Tooltip("Coin kazanıldığında eski değerden yeni değere sayarak artma süresi (saniye).")]
        [SerializeField] private float countUpDuration = 0.6f;
        [Tooltip("Sayaç animasyonunun hız eğrisi (0-1 aralığında ilerleme).")]
        [SerializeField] private AnimationCurve countUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private int displayedCoins;
        private Coroutine countRoutine;

        private void Awake()
        {
            // PlayerData zaten kalıcı (PlayerPrefs) — burada sadece o anki
            // gerçek değeri okuyup ekrana anlık yazıyoruz, animasyonsuz.
            displayedCoins = PlayerData.Coins;
            RefreshTextInstant();
        }

        private void RefreshTextInstant()
        {
            if (coinsText != null)
                coinsText.text = displayedCoins.ToString();
        }

        /// <summary>
        /// Coin'i GERÇEKTEN ekler (PlayerData'ya kalıcı yazar) VE bu bar
        /// üzerinde eski değerden yeni değere sayan bir animasyon oynatır.
        /// Süre/eğri Inspector'dan (countUpDuration, countUpCurve) ayarlanır.
        /// </summary>
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
                elapsed += Time.deltaTime;
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
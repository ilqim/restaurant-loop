using System.Collections;
using UnityEngine;

namespace RestaurantLoop
{
    public enum GameState
    {
        Playing,
        Win,
        Fail
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Debug")]
        [SerializeField] private GameState currentState = GameState.Playing;

        [Header("Economy Rewards & Penalties")]
        [SerializeField] private int coinsPerWin = 40;

        [Header("Kazanma Particle Efektleri")]
        [Tooltip("Kazanınca HEMEN oynatılacak 1. particle efekti.")]
        [SerializeField] private ParticleSystem winParticle1;
        [Tooltip("Kazanınca HEMEN oynatılacak 2. particle efekti.")]
        [SerializeField] private ParticleSystem winParticle2;
        [Tooltip("Particle'lar oynamaya başladıktan kaç saniye sonra Win ekranı (LevelCompleteUI) gösterilsin.")]
        [SerializeField] private float winScreenDelayAfterParticles = 2f;

        [Header("Win/Fail Sonrası Durdurma")]
        [Tooltip("Win/Fail olduktan kaç saniye sonra oyunun TAMAMEN durdurulacağı (Time.timeScale = 0). " +
                 "Panelin fade-in animasyonunun tamamlanmasına yetecek kadar süre bırak — aksi halde fade " +
                 "yarım kalmış görünür.")]
        [SerializeField] private float pauseDelayAfterGameEnd = 1f;

        [Header("UI Reference")]
        [SerializeField] private LevelCompleteUI levelCompleteUI;
        [Tooltip("Level FAIL olduğunda gösterilecek panel. Boş bırakılırsa Start()'ta otomatik aranır.")]
        [SerializeField] private FailScreenUI failScreenUI;
        [Tooltip("In-game'de gösterilen 'Level X' üst bar objesi — kazanınca gizlenir (CurrencyBar ile üst üste binmesin diye).")]
        [SerializeField] private GameObject levelTopBar;
        [Tooltip("Bu SAHNEYE (Game) ait, bağımsız CurrencyBar instance'ı. Boş bırakılırsa Start()'ta otomatik aranır.")]
        [SerializeField] private CurrencyBar currencyBar;

        [Header("Yeni Booster Duyuru Ekranları")]
        [Tooltip("Level başladıktan kaç saniye sonra (varsa) 'yeni booster açıldı' ekranı gösterilsin.")]
        [SerializeField] private float newBoosterScreenDelay = 2f;
        [Tooltip("Bu ekranın fade-in süresi.")]
        [SerializeField] private float newBoosterFadeDuration = 0.35f;
        [Tooltip("Select booster'ın 'yeni açıldı' ekranı — LevelManager'daki Select unlock level'ına TAM olarak eşitse gösterilir.")]
        [SerializeField] private CanvasGroup selectBoosterNewScreen;
        [Tooltip("Add Tray booster'ın 'yeni açıldı' ekranı.")]
        [SerializeField] private CanvasGroup addTrayBoosterNewScreen;
        [Tooltip("Shuffle booster'ın 'yeni açıldı' ekranı.")]
        [SerializeField] private CanvasGroup shuffleBoosterNewScreen;

        private CanvasGroup activeNewBoosterScreen;

        private CustomerManager customerManager;

        public GameState CurrentState => currentState;
        public bool IsPlaying => currentState == GameState.Playing;
        public bool IsWin => currentState == GameState.Win;
        public bool IsFail => currentState == GameState.Fail;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            customerManager = FindFirstObjectByType<CustomerManager>();

            if (customerManager == null)
            {
                Debug.LogWarning(
                    "GameManager: CustomerManager bulunamadı."
                );
            }

            if(levelCompleteUI == null)
            {
                levelCompleteUI = FindFirstObjectByType<LevelCompleteUI>();
            }

            if (failScreenUI == null)
            {
                failScreenUI = FindFirstObjectByType<FailScreenUI>();
            }

            if (currencyBar == null)
            {
                currencyBar = FindFirstObjectByType<CurrencyBar>();
            }

            currentState = GameState.Playing;

            // ÖNEMLİ: Önceki bir Win/Fail'den kalma duraklamayı (Time.timeScale=0)
            // temizliyoruz — bu sahne her yüklendiğinde oyun GARANTİ olarak
            // normal hızda başlasın.
            Time.timeScale = 1f;

            // Bu level'de yeni açılan bir booster varsa (LevelManager'daki
            // unlock level'lardan biri TAM OLARAK şu anki level'e eşitse),
            // birkaç saniye sonra ilgili "yeni booster" ekranını göster.
            EnsureNewBoosterScreensHidden();
            StartCoroutine(ShowNewBoosterScreenAfterDelay());

            Debug.Log("===== GAME START =====");
        }

        private void Update()
        {
            // Oyun zaten bittiyse tekrar kontrol etme.
            if (!IsPlaying)
                return;

            CheckWinCondition();
        }

        /// <summary>
        /// Bir Food slota girmeye çalıştığında hiç boş slot yoksa
        /// SlotManager burayı çağırır.
        /// </summary>
        public void FailLevel()
        {
            if (!IsPlaying)
                return;

            currentState = GameState.Fail;

            //SFX & Heart Cezası
            AudioEvents.PlayLevelFail();
            PlayerData.ConsumeHeart();

            // İSTEK: Kaybedince level müziği durup, level müziklerinden
            // AYRI bir "fail müziği" çalmaya başlasın.
            AudioEvents.StopMusic();
            AudioEvents.PlayFailMusic();

            Debug.Log($"FAIL! Remaining hearts: {PlayerData.Hearts}");

            if (failScreenUI != null)
            {
                failScreenUI.Show();
            }
            else
            {
                Debug.LogWarning("GameManager: FailScreenUI bulunamadı — fail ekranı gösterilemedi.");
            }

            Debug.Log("=================================");
            Debug.Log("FAIL!");
            Debug.Log("Slotlar tamamen dolu. Yeni Food yerleştirilemedi.");
            Debug.Log("=================================");

            // ÖNEMLİ: FAIL'de level İLERLEMİYOR — oyuncu aynı level'i
            // tekrar oynayacak. PlayerData.CurrentLevel'e hiç dokunmuyoruz.

            // Panel fade-in'i tamamlansın diye 1sn (parametrik) bekleyip
            // oyunu tamamen durduruyoruz.
            PauseGameplayAfterDelay();
        }

        /// <summary>
        /// Tüm müşteriler servis edildiğinde WIN.
        /// </summary>
        public void CheckWinCondition()
        {
            if (!IsPlaying)
                return;

            if (customerManager == null)
                return;

            if (customerManager.RemainingCustomerCount <= 0)
            {
                WinLevel();
            }
        }

        private void WinLevel()
        {
            if (!IsPlaying)
                return;

            currentState = GameState.Win;
            AudioEvents.StopMusic();
            //SFX
            AudioEvents.PlayLevelComplete();

            // İSTEK: Particle'lar HEMEN oynasın — win ekranını (LevelCompleteUI)
            // beklemesinler.
            if (winParticle1 != null) winParticle1.Play();
            if (winParticle2 != null) winParticle2.Play();

            if (levelTopBar != null)
                levelTopBar.SetActive(false);

            // ÖNEMLİ: Coin ekleme (ve buna bağlı para sesi) ARTIK BURADA
            // ÇAĞRILMIYOR — oyun kazanıldığı anda değil, para GERÇEKTEN
            // verilirken (LevelCompleteUI'nin coin uçuşma animasyonuyla
            // AYNI anda, ShowLevelCompleteUIAfterDelay() içinde) çağrılıyor.

            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.AdvanceToNextLevel();
                Debug.Log($"WIN! Bir sonraki level: {LevelManager.Instance.CurrentLevel}");
            }
            else
            {
                Debug.LogWarning("GameManager: LevelManager.Instance bulunamadı — level ilerlemesi atlandı " +
                                  "(muhtemelen Main Menu'den geçmeden direkt bu sahneden test ediliyorsun).");
            }

            Debug.Log("=================================");
            Debug.Log("WIN!");
            Debug.Log("Tüm müşterilerin siparişleri karşılandı.");
            Debug.Log("=================================");

            // İSTEK: Win ekranı (LevelCompleteUI), particle'lar oynamaya
            // başladıktan winScreenDelayAfterParticles saniye sonra gelsin.
            // Oyunu durdurma (PauseGameplayAfterDelay) da ARTIK ekran
            // GERÇEKTEN gösterildiği andan itibaren sayılıyor — aksi halde
            // ekran daha görünmeden oyun donabilirdi.
            StartCoroutine(ShowLevelCompleteUIAfterDelay());
        }

        private IEnumerator ShowLevelCompleteUIAfterDelay()
        {
            yield return new WaitForSecondsRealtime(winScreenDelayAfterParticles);

            // İSTEK: Para (coin) TAM BURADA, ekran/animasyon görünürken
            // veriliyor — PlayerData.AddCoins içindeki para sesi de bu
            // yüzden artık kazanma anında değil, TAM BU ANDA çalıyor.
            if (currencyBar != null)
            {
                currencyBar.gameObject.SetActive(true);
                currencyBar.AddCoinsAnimated(coinsPerWin);
            }
            else
            {
                Debug.LogWarning("GameManager: CurrencyBar bulunamadı — coin animasyonsuz eklendi.");
                PlayerData.AddCoins(coinsPerWin);
            }

            Debug.Log($"WIN! Awarded {coinsPerWin} coins. Total: {PlayerData.Coins}");

            if (levelCompleteUI != null)
            {
                levelCompleteUI.Show(coinsPerWin);
            }

            // Panel fade-in'i tamamlansın diye pauseDelayAfterGameEnd kadar
            // bekleyip oyunu tamamen durduruyoruz.
            PauseGameplayAfterDelay();
        }

        /// <summary>
        /// Win/Fail olduktan pauseDelayAfterGameEnd saniye sonra oyunu
        /// TAMAMEN durdurur (Time.timeScale = 0). WaitForSecondsRealtime
        /// kullanıyoruz ki timeScale'e bağlı kalmasın — normal WaitForSeconds
        /// kullansaydık, timeScale sıfırlandığı anda kendi beklemesi de
        /// donardı (paradoks).
        /// </summary>
        private void PauseGameplayAfterDelay()
        {
            StartCoroutine(PauseAfterDelayRoutine());
        }

        private IEnumerator PauseAfterDelayRoutine()
        {
            yield return new WaitForSecondsRealtime(pauseDelayAfterGameEnd);
            Time.timeScale = 0f;
        }

        // ============================================================
        // YENİ BOOSTER DUYURU EKRANLARI
        // ============================================================

        /// <summary>
        /// Sahne her yüklendiğinde (Retry/level geçişi dahil) 3 ekran da
        /// baştan görünmez olsun diye garanti altına alınıyor — bir
        /// önceki oturumdan kalma açık bir ekran olmasın.
        /// </summary>
        private void EnsureNewBoosterScreensHidden()
        {
            HideCanvasGroupInstant(selectBoosterNewScreen);
            HideCanvasGroupInstant(addTrayBoosterNewScreen);
            HideCanvasGroupInstant(shuffleBoosterNewScreen);
        }

        private static void HideCanvasGroupInstant(CanvasGroup group)
        {
            if (group == null) return;

            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.gameObject.SetActive(false);
        }

        /// <summary>
        /// Level başladıktan newBoosterScreenDelay saniye sonra, LevelManager'daki
        /// booster unlock level'larından HERHANGİ BİRİ şu anki level'e TAM
        /// olarak eşitse, ilgili "yeni booster açıldı" ekranını gösterir.
        /// WaitForSecondsRealtime kullanılıyor — pause-güvenli, PauseGameplayAfterDelay
        /// ile aynı mantık.
        /// </summary>
        private IEnumerator ShowNewBoosterScreenAfterDelay()
        {
            yield return new WaitForSecondsRealtime(newBoosterScreenDelay);

            if (LevelManager.Instance == null)
                yield break;

            int currentLevel = LevelManager.Instance.CurrentLevel;

            CanvasGroup screenToShow = null;

            if (currentLevel == LevelManager.Instance.GetBoosterUnlockLevel(BoosterType.Select))
                screenToShow = selectBoosterNewScreen;
            else if (currentLevel == LevelManager.Instance.GetBoosterUnlockLevel(BoosterType.AddTray))
                screenToShow = addTrayBoosterNewScreen;
            else if (currentLevel == LevelManager.Instance.GetBoosterUnlockLevel(BoosterType.Shuffle))
                screenToShow = shuffleBoosterNewScreen;

            if (screenToShow == null)
                yield break;

            activeNewBoosterScreen = screenToShow;
            yield return FadeInCanvasGroup(screenToShow, newBoosterFadeDuration);
        }

        private IEnumerator FadeInCanvasGroup(CanvasGroup group, float duration)
        {
            group.gameObject.SetActive(true);
            group.alpha = 0f;
            group.blocksRaycasts = true;
            group.interactable = true;

            duration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime; // pause-güvenli
                group.alpha = Mathf.Lerp(0f, 1f, elapsed / duration);
                yield return null;
            }

            group.alpha = 1f;
        }

        /// <summary>
        /// "Yeni booster" ekranındaki kapatma/OK butonuna bağla — hangi
        /// ekran açıksa onu kapatır.
        /// </summary>
        public void CloseNewBoosterScreen()
        {
            if (activeNewBoosterScreen == null)
                return;

            AudioEvents.PlayButtonClick();
            HideCanvasGroupInstant(activeNewBoosterScreen);
            activeNewBoosterScreen = null;
        }

        /// <summary>
        /// Debug/test için oyunu tekrar Playing durumuna alır.
        /// Şimdilik kullanılmıyor.
        /// </summary>
        public void ResetGameState()
        {
            currentState = GameState.Playing;

            Debug.Log("GameManager: Oyun tekrar Playing durumuna geçti.");
        }
    }
}
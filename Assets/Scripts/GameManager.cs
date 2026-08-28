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

            if (levelTopBar != null)
                levelTopBar.SetActive(false);

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

            if(levelCompleteUI != null)
            {
                levelCompleteUI.Show(coinsPerWin);
            }

            Debug.Log($"WIN! Awarded {coinsPerWin} coins. Total: {PlayerData.Coins}");

            Debug.Log("=================================");
            Debug.Log("WIN!");
            Debug.Log("Tüm müşterilerin siparişleri karşılandı.");
            Debug.Log("=================================");

            // Panel fade-in'i tamamlansın diye 1sn (parametrik) bekleyip
            // oyunu tamamen durduruyoruz.
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
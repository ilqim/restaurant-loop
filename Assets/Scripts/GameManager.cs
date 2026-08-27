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

        [Header("UI Reference")]
        [SerializeField] private LevelCompleteUI levelCompleteUI;

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

            currentState = GameState.Playing;

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

            Debug.Log($"FAIL! Remaining hearts: {PlayerData.Hearts}");

            Debug.Log("=================================");
            Debug.Log("FAIL!");
            Debug.Log("Slotlar tamamen dolu. Yeni Food yerleştirilemedi.");
            Debug.Log("=================================");

            // ÖNEMLİ: FAIL'de level İLERLEMİYOR — oyuncu aynı level'i
            // tekrar oynayacak. PlayerData.CurrentLevel'e hiç dokunmuyoruz.
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

            //SFX & COIN REWARD
            AudioEvents.PlayLevelComplete();
            PlayerData.AddCoins(coinsPerWin);

            // ÖNEMLİ: Level kazanıldığı AN, bir sonraki level'e ilerliyoruz.
            // Bu sadece PlayerData.CurrentLevel'i (PlayerPrefs'e kalıcı olarak)
            // bir arttırır — HİÇBİR sahne yüklemez. Sahne geçişi (Ana Menü'ye
            // dönme, bir sonraki level'i başlatma vb.) LevelCompleteUI'daki
            // butonlar üzerinden ayrıca, SceneFlowManager ile yapılmaya devam
            // ediyor. Burada ilerletmenin amacı: kullanıcı Ana Menü'ye
            // döndüğünde, MainMenuLevelDisplay'in artık doğru (bir sonraki)
            // level numarasını göstermesi ve Play'e tekrar basıldığında
            // SceneFlowManager'ın doğru level'in LevelData'sını yüklemesi.
            LevelManager.Instance.AdvanceToNextLevel();

            if(levelCompleteUI != null)
            {
                levelCompleteUI.Show(coinsPerWin);
            }

            Debug.Log($"WIN! Awarded {coinsPerWin} coins. Total: {PlayerData.Coins}");
            Debug.Log($"WIN! Bir sonraki level: {LevelManager.Instance.CurrentLevel}");

            Debug.Log("=================================");
            Debug.Log("WIN!");
            Debug.Log("Tüm müşterilerin siparişleri karşılandı.");
            Debug.Log("=================================");
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
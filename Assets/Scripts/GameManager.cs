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

            Debug.Log("=================================");
            Debug.Log("FAIL!");
            Debug.Log("Slotlar tamamen dolu. Yeni Food yerleştirilemedi.");
            Debug.Log("=================================");
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
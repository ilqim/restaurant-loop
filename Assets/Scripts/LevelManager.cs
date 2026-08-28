using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RestaurantLoop
{
    /// <summary>
    /// Level listesindeki tek bir satır — hangi LevelData ve hangi zorlukta
    /// olduğu. Inspector'da bunu görüp her level'in yanına zorluğunu
    /// (Easy/Hard/SuperHard) seçebilirsin.
    /// </summary>
    [System.Serializable]
    public struct LevelEntry
    {
        public LevelData data;
        public LevelDifficulty difficulty;
    }

    /// <summary>
    /// Tüm level'lar TEK bir "Game" sahnesinde yaşıyor — LevelManager bu
    /// yüzden HİÇBİR sahne yüklemez, sadece:
    /// 1) "Şu an hangi level'deyiz" bilgisini tutar (PlayerData üzerinden kalıcı).
    /// 2) Game sahnesi her açıldığında (SceneFlowManager tarafından, sabit
    ///    "Game" sahne adıyla), sahnedeki TÜM ILevelDataReceiver
    ///    implementasyonlarına (GridManager, QueueManager,
    ///    LevelConservationChecker...) o anki level'in LevelData'sını verir.
    /// 3) Her level'in zorluğunu (Easy/Hard/SuperHard) tutar — Main Menu'deki
    ///    difficulty balonlarının hangi varyantı gösterileceğini buradan sorar.
    ///
    /// Sahneler arası kalıcı (DontDestroyOnLoad) bir singleton — Main Menu
    /// (veya en baştaki bootstrap sahnesi) içine BİR KERE koyulmalı.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Level Veritabanı")]
        [Tooltip("Sırasıyla Level 1, Level 2, Level 3... İndeks 0 = Level 1. " +
                 "Her satırda hem o level'in LevelData'sını HEM DE zorluğunu " +
                 "(Easy/Hard/SuperHard) seçiyorsun — Main Menu'deki difficulty " +
                 "balonları buradan besleniyor.")]
        [SerializeField] private List<LevelEntry> levels = new();

        [Header("Booster Level Kilitleri (parametrik)")]
        [Tooltip("Select booster bu level numarasından İTİBAREN (>=) açılır/interactable olur. Öncesinde soluk/kilitli durur.")]
        [SerializeField] private int selectBoosterUnlockLevel = 20;
        [Tooltip("Add Tray booster bu level numarasından İTİBAREN (>=) açılır/interactable olur.")]
        [SerializeField] private int addTrayBoosterUnlockLevel = 9;
        [Tooltip("Shuffle booster bu level numarasından İTİBAREN (>=) açılır/interactable olur.")]
        [SerializeField] private int shuffleBoosterUnlockLevel = 15;

        [Header("Level Müziği")]
        [Tooltip("Bu sahne yüklendiğinde, o anki level'in zorluğuna göre müzik otomatik çalar (AudioManager.PlayMusicForDifficulty). SceneFlowManager'daki 'Gameplay Scene Name' ile aynı olmalı.")]
        [SerializeField] private string gameplaySceneName = "Game";

        public int TotalLevelCount => levels.Count;

        /// <summary>Şu anki level numarası (1'den başlar), toplam level sayısına clamp'lenmiş.</summary>
        public int CurrentLevel => Mathf.Clamp(PlayerData.CurrentLevel, 1, Mathf.Max(1, levels.Count));

        /// <summary>Şu anki level'in LevelData'sı.</summary>
        public LevelData CurrentLevelData => GetLevelData(CurrentLevel);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>Bu booster tipinin hangi level'da (>=) açıldığını döner.</summary>
        public int GetBoosterUnlockLevel(BoosterType type) => type switch
        {
            BoosterType.Select => selectBoosterUnlockLevel,
            BoosterType.AddTray => addTrayBoosterUnlockLevel,
            BoosterType.Shuffle => shuffleBoosterUnlockLevel,
            _ => 1
        };

        /// <summary>Şu anki level'de bu booster açık mı (unlock level'a ulaşılmış mı)?</summary>
        public bool IsBoosterUnlocked(BoosterType type) => CurrentLevel >= GetBoosterUnlockLevel(type);

        public LevelData GetLevelData(int levelNumber)
        {
            int index = levelNumber - 1;

            if (index < 0 || index >= levels.Count)
            {
                Debug.LogWarning($"LevelManager: Level {levelNumber} için LevelData bulunamadı " +
                                  $"(levels listesinde {levels.Count} eleman var).");
                return null;
            }

            return levels[index].data;
        }

        /// <summary>
        /// Belirtilen level numarasının zorluğunu döner. Liste dışında bir
        /// numara istenirse (örn. toplam level sayısını aşan bir "future"
        /// slot) false döner — çağıran taraf bu durumda ilgili balonu
        /// tamamen gizlemeli (hiçbiri aktif olmasın).
        /// </summary>
        public bool TryGetLevelDifficulty(int levelNumber, out LevelDifficulty difficulty)
        {
            int index = levelNumber - 1;

            if (index < 0 || index >= levels.Count)
            {
                difficulty = LevelDifficulty.Easy;
                return false;
            }

            difficulty = levels[index].difficulty;
            return true;
        }

        /// <summary>
        /// Belirtilen level'i kalıcı olarak "şu anki level" yapar — SAHNE YÜKLEMEZ.
        /// Sahne yükleme işini SceneFlowManager.LoadGameplayScene() yapıyor;
        /// bunu ondan ÖNCE çağırıp hangi level'e gidileceğini seçmiş olursun
        /// (örn. bir level-select ekranından belirli bir level'e tıklanınca).
        /// </summary>
        public void SelectLevel(int levelNumber)
        {
            PlayerData.CurrentLevel = Mathf.Clamp(levelNumber, 1, Mathf.Max(1, levels.Count));
        }

        /// <summary>Level tamamlandığında çağır — bir sonraki level'e geçer (son leveldeyse aynı kalır).</summary>
        public void AdvanceToNextLevel()
        {
            int next = Mathf.Min(CurrentLevel + 1, Mathf.Max(1, levels.Count));
            PlayerData.CurrentLevel = next;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LevelData data = CurrentLevelData;

            var allMonoBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            // 1) Level verisi — GridManager/QueueManager/LevelConservationChecker vb.
            if (data != null)
            {
                foreach (var receiver in allMonoBehaviours.OfType<ILevelDataReceiver>())
                {
                    receiver.SetLevelData(data);
                }
            }

            // 2) Booster butonları — Main Menu'de kurulu olsa bile, Game
            // sahnesi her yüklendiğinde buradaki butonlar otomatik bulunup
            // "bu level'de açık mı" bilgisine göre kilit/interactable
            // durumları güncellenir.
            foreach (var gate in allMonoBehaviours.OfType<IBoosterLevelGate>())
            {
                gate.RefreshLevelGate();
            }

            // 3) LEVEL MÜZİĞİ — SADECE Game sahnesinde, o anki level'in
            // zorluğuna (Easy/Hard/SuperHard) göre otomatik çalar. AudioManager'a
            // DOĞRUDAN referans TUTMUYORUZ — mimari gereği tüm ses istekleri
            // AudioEvents üzerinden gider (AudioManager sahnede yoksa bile
            // hiçbir şey patlamaz, sadece sessiz kalır).
            if (scene.name == gameplaySceneName)
            {
                if (TryGetLevelDifficulty(CurrentLevel, out LevelDifficulty difficulty))
                {
                    AudioEvents.PlayMusicForDifficulty(difficulty);
                }
                else
                {
                    Debug.LogWarning($"LevelManager: Level {CurrentLevel} için zorluk bilgisi bulunamadı — level müziği çalınamadı.");
                }
            }
        }
    }
}
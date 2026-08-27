using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RestaurantLoop
{
    /// <summary>
    /// Tüm level'lar TEK bir "Game" sahnesinde yaşıyor — LevelManager bu
    /// yüzden HİÇBİR sahne yüklemez, sadece:
    /// 1) "Şu an hangi level'deyiz" bilgisini tutar (PlayerData üzerinden kalıcı).
    /// 2) Game sahnesi her açıldığında (SceneFlowManager tarafından, sabit
    ///    "Game" sahne adıyla), sahnedeki TÜM ILevelDataReceiver
    ///    implementasyonlarına (GridManager, QueueManager,
    ///    LevelConservationChecker...) o anki level'in LevelData'sını verir.
    ///
    /// Sahneler arası kalıcı (DontDestroyOnLoad) bir singleton — Main Menu
    /// (veya en baştaki bootstrap sahnesi) içine BİR KERE koyulmalı.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Level Veritabanı")]
        [Tooltip("Sırasıyla Level 1, Level 2, Level 3... İndeks 0 = Level 1. " +
                 "Buraya kaç LevelData asset'i eklersen oyunda o kadar level olur.")]
        [SerializeField] private List<LevelData> levels = new();

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

        public LevelData GetLevelData(int levelNumber)
        {
            int index = levelNumber - 1;

            if (index < 0 || index >= levels.Count)
            {
                Debug.LogWarning($"LevelManager: Level {levelNumber} için LevelData bulunamadı " +
                                  $"(levels listesinde {levels.Count} eleman var).");
                return null;
            }

            return levels[index];
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

            if (data == null)
                return;

            // Sahnedeki TÜM ILevelDataReceiver implementasyonlarını bul
            // (GridManager, QueueManager, LevelConservationChecker vb.) ve
            // şu anki level'in verisini ver. Main Menu gibi bu component'leri
            // içermeyen sahnelerde liste boş döner, hiçbir şey yapılmaz.
            var receivers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ILevelDataReceiver>();

            foreach (var receiver in receivers)
            {
                receiver.SetLevelData(data);
            }
        }
    }
}
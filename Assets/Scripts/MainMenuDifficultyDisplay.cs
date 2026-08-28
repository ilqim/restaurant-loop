using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Tek bir level göstergesi "slotu" — üç varyant objesi (Easy/Hard/
    /// SuperHard, her biri kendi içinde balon+text arka planı+zorluk text'i
    /// taşıyan tam bir GameObject) arasından o level'in gerçek zorluğuna
    /// uyanı aktif eder, diğer ikisini gizler.
    /// </summary>
    [System.Serializable]
    public class DifficultySlot
    {
        [Tooltip("Balon + text arka planı + 'Easy' yazan text'i içeren TAM obje.")]
        public GameObject easyVariant;
        [Tooltip("Balon + text arka planı + 'Hard' yazan text'i içeren TAM obje.")]
        public GameObject hardVariant;
        [Tooltip("Balon + text arka planı + 'Super Hard' yazan text'i içeren TAM obje.")]
        public GameObject superHardVariant;

        /// <summary>
        /// difficulty null ise (örn. bu slot toplam level sayısını aşan bir
        /// "future" level'a denk geliyorsa) ÜÇÜ DE gizlenir.
        /// </summary>
        public void Apply(LevelDifficulty? difficulty)
        {
            SetActiveSafe(easyVariant, difficulty == LevelDifficulty.Easy);
            SetActiveSafe(hardVariant, difficulty == LevelDifficulty.Hard);
            SetActiveSafe(superHardVariant, difficulty == LevelDifficulty.SuperHard);
        }

        private static void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null)
                go.SetActive(active);
        }
    }

    /// <summary>
    /// Main Menu'deki 4 level göstergesi (current level + sonraki 3 level)
    /// için, her birinin zorluğuna göre doğru balon varyantını gösterir.
    /// Her slot senin sahnede önceden kurduğun 3 GameObject'i (Easy/Hard/
    /// SuperHard) referans alıyor — bu script sadece hangisinin aktif
    /// olacağına karar veriyor, obje oluşturmuyor/yok etmiyor.
    /// </summary>
    public class MainMenuDifficultyDisplay : MonoBehaviour
    {
        [Header("Current Level")]
        [SerializeField] private DifficultySlot currentLevelSlot;

        [Header("Future +1")]
        [SerializeField] private DifficultySlot future1Slot;

        [Header("Future +2")]
        [SerializeField] private DifficultySlot future2Slot;

        [Header("Future +3")]
        [SerializeField] private DifficultySlot future3Slot;

        private void Start()
        {
            Refresh();
        }

        /// <summary>Level ilerlemesi değiştiyse (örn. bir level bitirildiyse) dışarıdan tekrar çağrılabilir.</summary>
        public void Refresh()
        {
            if (LevelManager.Instance == null)
            {
                Debug.LogWarning("MainMenuDifficultyDisplay: Sahnede/kalıcı olarak bir LevelManager bulunamadı.");
                return;
            }

            int current = LevelManager.Instance.CurrentLevel;

            currentLevelSlot?.Apply(GetDifficultyOrNull(current));
            future1Slot?.Apply(GetDifficultyOrNull(current + 1));
            future2Slot?.Apply(GetDifficultyOrNull(current + 2));
            future3Slot?.Apply(GetDifficultyOrNull(current + 3));
        }

        private LevelDifficulty? GetDifficultyOrNull(int levelNumber)
        {
            return LevelManager.Instance.TryGetLevelDifficulty(levelNumber, out var d)
                ? d
                : (LevelDifficulty?)null;
        }
    }
}
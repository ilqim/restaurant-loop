using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Level başlarken, queue'daki toplam yemek kapasitesini (food type
    /// bazında, "renk toplamı") o tipte kaç müşteri olduğuyla karşılaştırır
    /// ve Console'a "EŞİT" / "DEĞİL" olarak yazar. Saf bir level-tasarım QA
    /// aracı — gameplay'e hiçbir etkisi yok, sadece bilgilendirme.
    ///
    /// Herhangi bir objeye ekle (örn. GridManager'ın olduğu obje, ya da
    /// boş bir "LevelValidation" objesi), LevelData'yı ata (boşsa
    /// GridManager'dan otomatik alır).
    /// </summary>
    public class LevelConservationChecker : MonoBehaviour
    {
        [Tooltip("Boş bırakılırsa GridManager'daki LevelDataRef kullanılır.")]
        [SerializeField] private LevelData levelData;
        [SerializeField] private GridManager gridManager;

        private void Start()
        {
            if (levelData == null && gridManager != null) levelData = gridManager.LevelDataRef;
            if (levelData == null) gridManager = FindFirstObjectByType<GridManager>();
            if (levelData == null && gridManager != null) levelData = gridManager.LevelDataRef;

            if (levelData == null)
            {
                Debug.LogWarning("LevelConservationChecker: LevelData bulunamadı, kontrol atlanıyor.");
                return;
            }

            CheckConservation();
        }

        private void CheckConservation()
        {
            var supplyByType = new Dictionary<FoodType, int>();
            foreach (var entry in levelData.queue)
            {
                supplyByType.TryGetValue(entry.food, out int cur);
                supplyByType[entry.food] = cur + entry.capacity;
            }

            var demandByType = new Dictionary<FoodType, int>();
            foreach (var entry in levelData.customers)
            {
                demandByType.TryGetValue(entry.food, out int cur);
                demandByType[entry.food] = cur + 1;
            }

            var allTypes = supplyByType.Keys.Union(demandByType.Keys).OrderBy(t => t.ToString());
            bool allEqual = true;

            Debug.Log("===== Level Korunum Kontrolü =====");
            foreach (var type in allTypes)
            {
                int supply = supplyByType.TryGetValue(type, out var s) ? s : 0;
                int demand = demandByType.TryGetValue(type, out var d) ? d : 0;
                bool equal = supply == demand;
                if (!equal) allEqual = false;

                Debug.Log($"{type}: queue toplam kapasite={supply}, müşteri sayısı={demand} -> {(equal ? "EŞİT" : "DEĞİL")}");
            }
            Debug.Log($"===== Sonuç: {(allEqual ? "TÜM tipler EŞİT" : "EN AZ BİR TİP EŞİT DEĞİL")} =====");
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    [System.Serializable]
    public struct FoodItemPrefab
    {
        public FoodType food;
        public GameObject prefab;
    }

    /// <summary>
    /// Conveyor'daki ana yemek (Food.cs), uygun bir müşterinin hizasına
    /// geldiğinde konveyörden ayrılmıyor — bunun yerine bu pool'dan
    /// (mesela) bir hamburger klonu alıp müşteriye "fırlatıyor". Klon
    /// müşteriye vardığında tekrar bu pool'a geri dönüyor — Instantiate/
    /// Destroy her seferinde çalışmıyor, mobilde ucuz.
    /// </summary>
    public class FoodPool : MonoBehaviour
    {
        [Header("Yemek tipi başına 'fırlatılan' görsel prefab")]
        [SerializeField]
        private List<FoodItemPrefab> prefabs = new()
        {
            new FoodItemPrefab { food = FoodType.Hamburger },
            new FoodItemPrefab { food = FoodType.Fries },
            new FoodItemPrefab { food = FoodType.Drink },
            new FoodItemPrefab { food = FoodType.Sushi },
            new FoodItemPrefab { food = FoodType.Steak },
            new FoodItemPrefab { food = FoodType.Dessert },
        };

        private readonly Dictionary<FoodType, GameObject> prefabLookup = new();
        private readonly Dictionary<FoodType, Queue<GameObject>> pools = new();

        void Awake()
        {
            foreach (var entry in prefabs)
            {
                if (entry.prefab == null) continue;
                prefabLookup[entry.food] = entry.prefab;
            }
        }

        /// <summary>
        /// Pool'dan bir instance alır (varsa yeniden kullanır, yoksa
        /// Instantiate eder), verilen pozisyon/rotasyona yerleştirir ve
        /// aktif eder.
        /// </summary>
        public GameObject Get(FoodType food, Vector3 position, Quaternion rotation)
        {
            if (pools.TryGetValue(food, out var queue))
            {
                while (queue.Count > 0)
                {
                    var reused = queue.Dequeue();
                    if (reused == null) continue; // sahne değişimi vs. yok olmuşsa atla
                    reused.transform.SetPositionAndRotation(position, rotation);
                    reused.SetActive(true);
                    return reused;
                }
            }

            if (!prefabLookup.TryGetValue(food, out var prefab) || prefab == null)
            {
                Debug.LogWarning($"FoodPool: '{food}' için prefab atanmamış (FoodPool > Yemek tipi başına prefab).");
                return null;
            }

            return Instantiate(prefab, position, rotation);
        }

        /// <summary>
        /// Instance'ı devre dışı bırakıp pool'a geri koyar.
        /// </summary>
        public void Release(FoodType food, GameObject instance)
        {
            if (instance == null) return;

            instance.SetActive(false);

            if (!pools.TryGetValue(food, out var queue))
            {
                queue = new Queue<GameObject>();
                pools[food] = queue;
            }

            queue.Enqueue(instance);
        }
    }
}
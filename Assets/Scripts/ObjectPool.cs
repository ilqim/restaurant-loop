using System;
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    [Serializable]
    public struct PoolPrewarmConfig
    {
        public GameObject prefab;
        [Tooltip("Level/oyun başında bu prefab'tan önceden kaç inaktif instance üretilsin.")]
        public int initialSize;
    }

    [DefaultExecutionOrder(-200)]
    public class ObjectPool : MonoBehaviour
    {
        private static ObjectPool instance;

        public static ObjectPool Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<ObjectPool>();
                    if (instance == null)
                        Debug.LogError("ObjectPool: Sahnede hiçbir ObjectPool objesi yok.");
                }
                return instance;
            }
        }

        [Header("Başlangıç Pool Büyüklükleri — her prefab için ayrı ayarla")]
        [Tooltip("Örn: Hamburger delivery klonu için 8, Hamburger customer için 6 gibi. Boş bırakılan/0 olanlar tamamen lazy kalır.")]
        [SerializeField] private List<PoolPrewarmConfig> prewarmConfigs = new();

        private readonly Dictionary<GameObject, Stack<GameObject>> pools = new();

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning($"Sahnede birden fazla ObjectPool var — '{gameObject.name}' siliniyor.");
                Destroy(gameObject);
                return;
            }
            instance = this;

            foreach (var config in prewarmConfigs)
            {
                if (config.prefab == null || config.initialSize <= 0) continue;
                Prewarm(config.prefab, config.initialSize);
            }
        }

        /// <summary>
        /// N tane inaktif instance önceden üretip stack'e koyar. Inspector'daki
        /// prewarmConfigs listesi zaten Awake'te bunu otomatik çağırıyor —
        /// bunu ayrıca runtime'da (örn. level başlarken, o levelin gerçek
        /// müşteri sayısına göre) elle de çağırabilirsin.
        /// </summary>
        public void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;

            if (!pools.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>();
                pools[prefab] = stack;
            }

            for (int i = 0; i < count; i++)
            {
                var spawned = Instantiate(prefab);
                var pooled = spawned.AddComponent<PooledObject>();
                pooled.SourcePrefab = prefab;
                spawned.SetActive(false);
                spawned.transform.SetParent(transform);
                stack.Push(spawned);
            }
        }

        /// <summary>Şu an bir prefab için havuzda bekleyen (inaktif) instance sayısı — debug/kontrol için.</summary>
        public int GetPooledCount(GameObject prefab)
        {
            return pools.TryGetValue(prefab, out var stack) ? stack.Count : 0;
        }

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (prefab == null)
            {
                Debug.LogError("ObjectPool.Get: prefab null.");
                return null;
            }

            if (!pools.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>();
                pools[prefab] = stack;
            }

            GameObject spawned = stack.Count > 0 ? stack.Pop() : Instantiate(prefab);

            var pooled = spawned.GetComponent<PooledObject>();
            if (pooled == null) pooled = spawned.AddComponent<PooledObject>();
            pooled.SourcePrefab = prefab;

            if (parent != null) spawned.transform.SetParent(parent);
            spawned.transform.SetPositionAndRotation(position, rotation);
            spawned.SetActive(true);
            return spawned;
        }

        public void Return(GameObject instance)
        {
            if (instance == null) return;

            var pooled = instance.GetComponent<PooledObject>();
            if (pooled == null || pooled.SourcePrefab == null)
            {
                Debug.LogWarning($"'{instance.name}' bir PooledObject değil veya SourcePrefab yok — pool'a iade edilemedi, Destroy ediliyor.");
                Destroy(instance);
                return;
            }

            instance.SetActive(false);

            if (!pools.TryGetValue(pooled.SourcePrefab, out var stack))
            {
                stack = new Stack<GameObject>();
                pools[pooled.SourcePrefab] = stack;
            }
            stack.Push(instance);
        }
    }
}
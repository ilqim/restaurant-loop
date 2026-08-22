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
                pooled.OriginalLocalScale = prefab.transform.localScale;
                spawned.SetActive(false);
                spawned.transform.SetParent(transform, false);
                stack.Push(spawned);
            }
        }

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

            GameObject spawned;
            Vector3 originalScale;

            if (stack.Count > 0)
            {
                spawned = stack.Pop();
                var existingPooled = spawned.GetComponent<PooledObject>();
                // Zaten kayıtlıysa onu kullan, yoksa (teorik olarak
                // olmamalı ama güvenlik için) prefab'tan oku.
                originalScale = existingPooled != null ? existingPooled.OriginalLocalScale : prefab.transform.localScale;
            }
            else
            {
                spawned = Instantiate(prefab);
                originalScale = prefab.transform.localScale;
            }

            var pooled = spawned.GetComponent<PooledObject>();
            if (pooled == null) pooled = spawned.AddComponent<PooledObject>();
            pooled.SourcePrefab = prefab;
            pooled.OriginalLocalScale = originalScale;

            // worldPositionStays=false: Unity'nin varsayılan reparent
            // davranışı (eski dünya boyutunu korumak için scale'i otomatik
            // yeniden hesaplaması) burada devre dışı — biz scale'i zaten
            // aşağıda ELLE, prefab'ın KENDİ orijinal değerine sıfırlıyoruz.
            if (parent != null)
                spawned.transform.SetParent(parent, false);

            spawned.transform.localScale = originalScale;
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
            instance.transform.SetParent(transform, false);

            if (!pools.TryGetValue(pooled.SourcePrefab, out var stack))
            {
                stack = new Stack<GameObject>();
                pools[pooled.SourcePrefab] = stack;
            }
            stack.Push(instance);
        }
    }
}
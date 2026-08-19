using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
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
            if (stack.Count > 0)
            {
                spawned = stack.Pop();
            }
            else
            {
                spawned = Instantiate(prefab);
            }

            // GÜVENLİ HALE GETİRİLDİ: AddComponent yerine önce var olanı ara.
            // Prefab üzerinde elle (yanlışlıkla) eklenmiş bir PooledObject
            // kalıntısı olsa bile artık İKİNCİ bir tane oluşturulmuyor —
            // hangisi varsa onun SourcePrefab'ı burada doğru şekilde set
            // ediliyor. "PooledObject değil veya SourcePrefab yok" hatasının
            // kök nedeni buydu.
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
                Debug.LogWarning($"'{instance.name}' bir PooledObject değil veya SourcePrefab yok — pool'a iade edilemedi, Destroy ediliyor. (ObjectPool.Get ile mi spawn edildi kontrol et.)");
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
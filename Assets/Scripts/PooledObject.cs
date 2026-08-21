using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Get() ile spawn edilen her instance'a otomatik eklenir/kullanılır.
    /// SourcePrefab'a ek olarak artık prefab'ın KENDİ orijinal scale'ini
    /// de tutuyor — reparent sonrası scale'i sabit 1'e değil, doğru
    /// (senin prefab'ta ayarladığın) değere sıfırlamak için gerekiyor.
    /// </summary>
    [DisallowMultipleComponent]
    public class PooledObject : MonoBehaviour
    {
        public GameObject SourcePrefab { get; set; }
        public Vector3 OriginalLocalScale { get; set; } = Vector3.one;
    }
}
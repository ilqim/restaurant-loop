using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Get() ile spawn edilen her instance'a otomatik eklenir/kullanılır.
    /// DisallowMultipleComponent: bir objede asla ikinci bir PooledObject
    /// eklenemez — elle Inspector'dan tekrar eklemeye çalışırsan Unity
    /// engeller. Bunu HİÇBİR prefab'a elle ekleme, sadece ObjectPool.Get
    /// çağırdığında runtime'da otomatik oluşur.
    /// </summary>
    [DisallowMultipleComponent]
    public class PooledObject : MonoBehaviour
    {
        public GameObject SourcePrefab { get; set; }
    }
}
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Raycast ile tıklanabilen her şeyin implement ettiği ortak arayüz.
    /// InputManager sadece bu arayüzü arıyor — QueueSlot, Slot ya da
    /// ileride eklenecek başka bir tıklanabilir tip fark etmiyor,
    /// InputManager hiçbirine özel olarak bağlı değil.
    /// </summary>
    public interface IQueueClickable
    {
        void HandleClick();
    }

    /// <summary>
    /// Bir queue hücresinin tıklanabilir collider'ını taşır. Food'un
    /// KENDİSİNDE collider YOK — tıklanabilir alan burada, sabit bir
    /// grid hücresinde duruyor. QueueManager bir food'u bu hücreye
    /// "ışınladığında" AssignFood ile hangi food'un burada durduğunu
    /// bildiriyor.
    ///
    /// GÖRSEL: Ayrı bir prefab instantiate ETMİYORUZ. Bu objenin ÜZERİNDE
    /// zaten duran SpriteRenderer'ın sprite/color'ını, atanan food'a göre
    /// değiştiriyoruz. Obje sayısı hiç artmıyor, extra collider/script
    /// oluşmuyor, GC allocation yok — en ucuz yöntem.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class QueueSlot : MonoBehaviour, IQueueClickable
    {
        private Food assignedFood;
        private SpriteRenderer spriteRenderer;

        [Header("Sayı Etiketi")]
        [Tooltip("İçindeki food'un kapasitesini gösteren 3D etiket (Canvas değil, normal derinlik testine tabi).")]
        [SerializeField] private WorldSpaceCountLabel countLabel;

        public Food AssignedFood => assignedFood;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            // QueueSlot'ta "boş" durumun default bir görünümü YOK — Slot.cs'in
            // aksine burada boşken hiçbir şekil gösterilmiyor. Bu yüzden
            // prefab'deki sprite'ı "default" olarak saklamıyoruz, renderer'ı
            // doğrudan kapatıyoruz.
            spriteRenderer.enabled = false;
        }

        public void AssignFood(Food food)
        {
            assignedFood = food;

            if (food != null)
            {
                spriteRenderer.enabled = true;
                if (food.QueueSprite != null)
                    spriteRenderer.sprite = food.QueueSprite;
                spriteRenderer.color = food.QueueColor;

                countLabel?.SetCount(food.Capacity);
            }
            else
            {
                ClearFood();
            }
        }

        public void ClearFood()
        {
            assignedFood = null;
            spriteRenderer.enabled = false;
            countLabel?.Clear();
        }

        public void HandleClick()
        {
            if (assignedFood == null) return;
            assignedFood.ActivateFromTap();
        }
    }
}
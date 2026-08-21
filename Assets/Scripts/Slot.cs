using UnityEngine;

namespace RestaurantLoop
{
    public enum SlotState
    {
        Empty,
        Occupied
    }

    /// <summary>
    /// Food-slot (konveyör sonu). Tıklanabilir collider'ı DA burada taşıyor
    /// — ayrı bir "SlotClickTarget" component'ine gerek yok, Slot kendisi
    /// IQueueClickable implement ediyor. Food'un kendisinde collider yok.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Slot : MonoBehaviour, IQueueClickable
    {
        [Header("State")]
        [SerializeField] private SlotState currentState = SlotState.Empty;

        [Header("Food")]
        [SerializeField] private Food currentFood;

        [Header("Yerleşim")]
        [Tooltip("Food, slot pozisyonunun ne kadar yukarısına yerleştirilsin (dünya Y ekseni).")]
        [SerializeField] private float foodYOffset = 0.3f;

        public SlotState CurrentState => currentState;
        public Food CurrentFood => currentFood;

        public bool IsEmpty => currentState == SlotState.Empty;

        public bool TryPlaceFood(Food food)
        {
            if (currentState == SlotState.Occupied)
                return false;

            if (food == null)
                return false;

            currentFood = food;
            currentState = SlotState.Occupied;

            Vector3 pos = transform.position;
            pos.y += foodYOffset;
            food.transform.position = pos;

            // Food'un "slottan çıkmak istiyorum" isteğine ABONE OLUYORUZ.
            // Bu, food'un elle çağırdığı, garanti sıralı bir istek —
            // eski "state değişti, umarım biri yakalar" mantığından farklı.
            food.ReenterConveyorRequested += OnReenterRequested;

            food.SetInFoodSlot();

            return true;
        }

        /// <summary>
        /// Food (slottayken) tıklanıp konveyöre dönmek istediğinde tetiklenir.
        /// SIRALAMA KESİN: önce burada slot boşalıyor, SONRA food'a
        /// "artık hareket edebilirsin" deniyor.
        /// </summary>
        private void OnReenterRequested(Food food)
        {
            food.ReenterConveyorRequested -= OnReenterRequested; // tek seferlik, tekrar abone kalmasın

            RemoveFood();                    // 1) ÖNCE slot boşalır
            food.EnterConveyorFromSlot();     // 2) SONRA food hareket etmeye başlar
        }

        public void RemoveFood()
        {
            if (currentFood != null)
                currentFood.ReenterConveyorRequested -= OnReenterRequested; // güvenlik: her ihtimalde aboneliği temizle

            currentFood = null;
            currentState = SlotState.Empty;
        }

        /// <summary>
        /// Eski SlotClickTarget'ın yaptığı iş — artık ayrı bir component
        /// değil, doğrudan Slot'un kendisi. IQueueClickable.HandleClick.
        /// </summary>
        public void HandleClick()
        {
            if (IsEmpty) return;
            CurrentFood?.ActivateFromTap();
        }
    }
}
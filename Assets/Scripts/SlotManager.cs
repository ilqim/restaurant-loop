using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public class SlotManager : MonoBehaviour
    {
        [Header("Slots")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        public bool TryPlaceFood(Food food)
        {
            if (food == null)
                return false;

            // Soldan sağa sırayla kontrol et.
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];

                if (slot == null)
                    continue;

                // Dolu slotları geç.
                if (!slot.IsEmpty)
                    continue;

                // İlk boş slota yerleştir.
                return slot.TryPlaceFood(food);
            }

            // Hiç boş slot yok.
            return false;
        }
    }
}
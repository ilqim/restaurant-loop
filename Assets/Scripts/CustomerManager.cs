using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Sahnedeki tüm bekleyen (Idle/Blocked) müşterileri (row,col) bazında
    /// tutar ve erişilebilirliği hesaplar.
    ///
    /// KURAL: Bir müşteri, KENDİ SATIRINDA en küçük veya en büyük dolu
    /// kolon index'ine sahipse YA DA KENDİ SÜTUNUNDA en küçük veya en
    /// büyük dolu satır index'ine sahipse -> Idle. Aksi halde -> Blocked.
    ///
    /// Hesaplama HER FRAME değil, sadece bir müşteri sahneye
    /// girdiğinde (RegisterCustomer) veya ayrıldığında (UnregisterCustomer)
    /// tetiklenir. Level başına müşteri sayısı küçük olduğu için
    /// (grid boyutuyla sınırlı) bu O(n) hesap mobilde önemsizdir.
    /// </summary>
    public class CustomerManager : MonoBehaviour
    {
        private readonly Dictionary<Vector2Int, Customer> customersByCell = new();

        public void RegisterCustomer(Customer customer)
        {
            var cell = new Vector2Int(customer.Row, customer.Col);
            customersByCell[cell] = customer;
            RecalculateAccessibility();
        }

        public void UnregisterCustomer(Customer customer)
        {
            var cell = new Vector2Int(customer.Row, customer.Col);
            if (customersByCell.TryGetValue(cell, out var existing) && existing == customer)
                customersByCell.Remove(cell);
            RecalculateAccessibility();
        }

        /// <summary>
        /// Dışarıdan (örn. bir müşteri Leaving'i tamamlayıp hücresini
        /// boşalttığında ama obje hemen Destroy edilmiyorsa) manuel
        /// tetiklemek istersen kullanabilirsin.
        /// </summary>
        public void ForceRecalculate() => RecalculateAccessibility();

        /// <summary>
        /// Food.cs, conveyor üzerindeki her waypoint adımında bunu çağırır.
        /// blockOrigin/blockSize, conveyor'ın o anki 2x2 (veya farklı boyutlu)
        /// bloğunun kapladığı satır/sütun aralığını temsil eder. Bir müşteri,
        /// bu blokla AYNI SATIR ya da AYNI SÜTUN aralığındaysa (tam aynı hücre
        /// olması gerekmiyor — blok birden fazla satır/sütun kaplayabilir),
        /// istediği yemek türü eşleşiyorsa ve Idle durumdaysa uygun adaydır.
        /// Birden fazla aday varsa blok merkezine en yakın olan seçilir.
        /// </summary>
        public bool TryFindDeliverableCustomer(FoodType food, Vector2Int blockOrigin, int blockSize, out Customer result)
        {
            result = null;
            float bestDistSqr = float.MaxValue;

            int rowMin = blockOrigin.x;
            int rowMax = blockOrigin.x + blockSize - 1;
            int colMin = blockOrigin.y;
            int colMax = blockOrigin.y + blockSize - 1;
            Vector2 blockCenter = new Vector2(blockOrigin.x + (blockSize - 1) * 0.5f, blockOrigin.y + (blockSize - 1) * 0.5f);

            foreach (var kvp in customersByCell)
            {
                var customer = kvp.Value;
                if (customer.CurrentState != CustomerState.Idle) continue;
                if (customer.DesiredFood != food) continue;

                bool rowAligned = customer.Row >= rowMin && customer.Row <= rowMax;
                bool colAligned = customer.Col >= colMin && customer.Col <= colMax;
                if (!rowAligned && !colAligned) continue;

                float dRow = customer.Row - blockCenter.x;
                float dCol = customer.Col - blockCenter.y;
                float distSqr = dRow * dRow + dCol * dCol;

                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    result = customer;
                }
            }

            return result != null;
        }

        private void RecalculateAccessibility()
        {
            if (customersByCell.Count == 0) return;

            var rowMinCol = new Dictionary<int, int>();
            var rowMaxCol = new Dictionary<int, int>();
            var colMinRow = new Dictionary<int, int>();
            var colMaxRow = new Dictionary<int, int>();

            foreach (var cell in customersByCell.Keys)
            {
                int row = cell.x;
                int col = cell.y;

                if (!rowMinCol.TryGetValue(row, out int curMinCol) || col < curMinCol) rowMinCol[row] = col;
                if (!rowMaxCol.TryGetValue(row, out int curMaxCol) || col > curMaxCol) rowMaxCol[row] = col;
                if (!colMinRow.TryGetValue(col, out int curMinRow) || row < curMinRow) colMinRow[col] = row;
                if (!colMaxRow.TryGetValue(col, out int curMaxRow) || row > curMaxRow) colMaxRow[col] = row;
            }

            foreach (var kvp in customersByCell)
            {
                var cell = kvp.Key;
                var customer = kvp.Value;

                // Eating/HappyJump/Leaving/Angry state'indeki müşteriye dokunma —
                // sadece bekleyen (Idle/Blocked) müşteriler bu sistemle güncellenir.
                if (!customer.IsWaiting) continue;

                bool isRowEdge = cell.y == rowMinCol[cell.x] || cell.y == rowMaxCol[cell.x];
                bool isColEdge = cell.x == colMinRow[cell.y] || cell.x == colMaxRow[cell.y];

                customer.SetState(isRowEdge || isColEdge ? CustomerState.Idle : CustomerState.Blocked);
            }
        }
    }
}
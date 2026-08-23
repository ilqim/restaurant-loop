using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Sahnedeki tüm bekleyen (Idle/Blocked) müşterileri (row,col) bazında
    /// tutar.
    ///
    /// RecalculateAccessibility() sadece GÖRSEL saydamlık için global
    /// "en az bir yönden açık mı" hesabı yapar — bu artık SERVİS kararı
    /// için kullanılmıyor, çünkü yöne duyarsız (bir müşteri satırın diğer
    /// ucundan açıksa, stack'in geldiği yönden önünde biri olsa bile
    /// "Idle" görünüyordu — asıl bug buydu).
    ///
    /// TryFindDeliverableCustomer artık KENDİ BAŞINA, stack'in konumundan
    /// bakarak "bu hatta gerçekten en yakın müşteri kim" diye hesaplıyor.
    /// Dört şart burada birlikte, doğru sırayla garanti ediliyor:
    ///   1) Blocked olmama  -> hatta en yakın olmayan hiç aday olamaz.
    ///   2) Zaten rezerve edilmemiş olma -> başka bir Food/Tray ZATEN bu
    ///      müşteriye yemek getiriyorsa (henüz ulaşmamış olsa bile) bir
    ///      daha aday olarak seçilmez. (Bkz. Customer.IsReceivingFood /
    ///      TryReserveForDelivery — rezervasyon artık Food ve Tray
    ///      arasında ORTAK, çünkü eskiden Tray kendi ayrı rezervasyon
    ///      setini tutuyordu ve bu kontrol hiç yapılmıyordu; bu da aynı
    ///      müşteriye birden fazla yemek gönderilmesine yol açan asıl
    ///      sebepti.)
    ///   3) Aynı food type  -> en yakın olan, istenen tipte değilse hiç
    ///      kimseye servis yapılmaz (arkadaki doğru tipe ASLA atlanmaz).
    ///   4) Önünde engel yok -> "en yakın" tanımının kendisi bu.
    /// </summary>
    public class CustomerManager : MonoBehaviour
    {
        private readonly Dictionary<Vector2Int, Customer> customersByCell = new();
        public int RemainingCustomerCount => customersByCell.Count;

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

        public void ForceRecalculate() => RecalculateAccessibility();

        /// <summary>
        /// Food.cs / Tray.cs, teslimat denemesi sırasında bunu çağırır.
        /// Artık global Idle bayrağına HİÇ bakmıyor — her aday için
        /// kendi satırında/sütununda gerçekten en yakın (yani önünde
        /// kimse olmayan) VE henüz rezerve edilmemiş olup olmadığını
        /// ayrı ayrı hesaplıyor.
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
                var candidate = kvp.Value;

                // Eating/HappyJump/Leaving/Angry -> zaten servis sürecinde, aday olamaz.
                if (!candidate.IsWaiting) continue;

                // Zaten başka bir Food/Tray tarafından rezerve edilmiş
                // (yemek yola çıkmış ama henüz ulaşmamış) -> aday olamaz.
                // NOT: Rezervasyon candidate'in state'ini DEĞİŞTİRMEZ
                // (hâlâ Idle görünür), bu yüzden bu kontrol IsWaiting'den
                // AYRI ve MUTLAKA gerekli.
                if (candidate.IsReceivingFood) continue;

                bool rowAligned = candidate.Row >= rowMin && candidate.Row <= rowMax;
                bool colAligned = candidate.Col >= colMin && candidate.Col <= colMax;
                if (!rowAligned && !colAligned) continue;

                // Asıl düzeltme: candidate, kendi satırında/sütununda
                // GERÇEKTEN en yakın mı? Değilse aradaki (herhangi bir
                // tipteki) müşteri onu engelliyor demektir — food type'ı
                // ne olursa olsun bu candidate şu an servis edilemez.
                bool losRow = rowAligned && IsNearestAlongRow(candidate, blockCenter.y);
                bool losCol = colAligned && IsNearestAlongColumn(candidate, blockCenter.x);
                if (!losRow && !losCol) continue;

                // Hat açık (en yakın candidate bu) — şimdi food type kontrolü.
                // Yanlış tipteyse bu STACK ona hizmet edemez; ama bu satırda/
                // sütunda ondan daha uzaktaki doğru tipteki başka birine de
                // ASLA atlamayız, çünkü onlar zaten yukarıdaki IsNearestAlong
                // kontrolünden geçemeyip elenmiş olurdu (candidate onları
                // engelliyor).
                if (candidate.DesiredFood != food) continue;

                float dRow = candidate.Row - blockCenter.x;
                float dCol = candidate.Col - blockCenter.y;
                float distSqr = dRow * dRow + dCol * dCol;

                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    result = candidate;
                }
            }

            return result != null;
        }

        /// <summary>
        /// candidate'in kendi satırında (candidate.Row), approachCol'a en
        /// yakın müşteri GERÇEKTEN candidate'in kendisi mi? (Food type
        /// fark etmeksizin TÜM bekleyen müşteriler arasında — rezerve
        /// edilmiş olsa bile, çünkü fiziksel olarak hâlâ oradadır ve
        /// arkasındakileri engellemeye devam eder.)
        /// </summary>
        private bool IsNearestAlongRow(Customer candidate, float approachCol)
        {
            Customer nearest = null;
            float bestDist = float.MaxValue;

            foreach (var kvp in customersByCell)
            {
                var c = kvp.Value;
                if (c.Row != candidate.Row) continue;
                if (!c.IsWaiting) continue; // servis sürecindeki biri fiziksel engel sayılmaz (yakında ayrılacak)

                float d = Mathf.Abs(c.Col - approachCol);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = c;
                }
            }

            return nearest == candidate;
        }

        /// <summary>Aynı mantık, sütun ekseninde.</summary>
        private bool IsNearestAlongColumn(Customer candidate, float approachRow)
        {
            Customer nearest = null;
            float bestDist = float.MaxValue;

            foreach (var kvp in customersByCell)
            {
                var c = kvp.Value;
                if (c.Col != candidate.Col) continue;
                if (!c.IsWaiting) continue;

                float d = Mathf.Abs(c.Row - approachRow);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = c;
                }
            }

            return nearest == candidate;
        }

        /// <summary>
        /// SADECE GÖRSEL saydamlık için — "en az bir yönden açık mı" global
        /// hesabı. Servis kararında ARTIK kullanılmıyor (TryFindDeliverableCustomer
        /// kendi satır/sütun bazlı en-yakın hesabını yapıyor). Bu yüzden bir
        /// müşteri burada "Idle" (parlak) görünse bile, stack'in geldiği
        /// spesifik yönden önünde biri varsa servis edilmeyebilir — bu bilinen
        /// bir basitleştirme, tamamen yön-duyarlı görsel istersen ayrıca
        /// söyle, ApplyVisual'ı da bu hesaba bağlarız.
        /// </summary>
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

                if (!customer.IsWaiting) continue;

                // Serving durumundaki müşteri zaten bir teslimatı
                // bekliyor — bu global görsel yeniden hesaplama onun
                // state'ini Idle/Blocked'a geri ÇEVİRMEMELİ, yoksa
                // rezervasyon sürerken state sessizce Idle'a döner ve
                // state üzerinden bakan kontroller yanıltılır.
                if (customer.CurrentState == CustomerState.Serving) continue;

                bool isRowEdge = cell.y == rowMinCol[cell.x] || cell.y == rowMaxCol[cell.x];
                bool isColEdge = cell.x == colMinRow[cell.y] || cell.x == colMaxRow[cell.y];

                customer.SetState(isRowEdge || isColEdge ? CustomerState.Idle : CustomerState.Blocked);
            }
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Konveyördeki tepsilerin teslimat ATEŞLEME sırasını belirleyen
    /// GLOBAL ve STATİK koordinatör.
    ///
    /// Neden statik/global (tek bir TrayManager'a bağlı DEĞİL):
    /// Sahnede satır ve sütun konveyörleri gibi birden fazla TrayManager
    /// olabilir. Öncelik sırası sadece "aynı TrayManager içindeki
    /// tepsiler" arasında değil, TÜM sahnedeki tepsiler arasında tutarlı
    /// olmalı — yoksa satır konveyöründeki bir tepsi ile sütun
    /// konveyöründeki bir tepsi yine aynı frame'de aynı müşteriyi
    /// hedefleyebilir (her biri kendi TrayManager'ının LateUpdate'inde,
    /// birbirinden habersiz).
    ///
    /// Neden food type başına AYRI kuyruk:
    /// Bir müşteri sadece TEK bir food type isteyebilir
    /// (Customer.DesiredFood), bu yüzden farklı food type'taki tepsiler
    /// ZATEN aynı müşteriye asla aday olamaz (CustomerManager filtreler).
    /// Yani hamburger tepsisinin fries kuyruğunu beklemesine hiç gerek
    /// yok — her food type kendi bağımsız FIFO kuyruğunda ilerler.
    ///
    /// FIFO garantisi:
    /// Bir tepsi Register edildiğinde kuyruğun SONUNA eklenir (önce giren
    /// önde/öncelikli kalır). Despawn/disable olduğunda Unregister edilip
    /// listeden çıkarılır — List.Remove doğal olarak sonrakileri öne
    /// kaydırır, yani standart queue davranışı.
    ///
    /// "Kimse ortak müşteriye talip değilse herkes normal ateş eder":
    /// Bu kuyruk sadece İŞLEME SIRASINI belirler, kimseyi "beklet"mez.
    /// Her tepsi her frame kendi bekleyen planlarını denemeye çalışır;
    /// sadece GERÇEKTEN aynı müşteriyi hedefleyen bir çakışma olduğunda,
    /// öncelikli olmayan taraf o denemede pas geçer (Customer'ın atomik
    /// rezervasyonu zaten dolu bulunur) — çakışma yoksa herkes aynı
    /// frame'de sorunsuzca ateş eder.
    /// </summary>
    internal static class TrayDeliveryQueue
    {
        private static readonly Dictionary<FoodType, List<Tray>> queuesByFoodType = new();

        // Aynı frame içinde birden fazla TrayManager bu koordinatörü
        // tetiklemeye çalışabilir (örn. satır + sütun TrayManager'ları).
        // Bu sayaç sayesinde işleme frame başına SADECE BİR KEZ yapılır.
        private static int lastProcessedFrame = -1;

        public static void Register(Tray tray, FoodType foodType)
        {
            if (tray == null)
                return;

            if (!queuesByFoodType.TryGetValue(foodType, out List<Tray> queue))
            {
                queue = new List<Tray>();
                queuesByFoodType[foodType] = queue;
            }

            if (!queue.Contains(tray))
                queue.Add(tray);
        }

        public static void Unregister(Tray tray, FoodType foodType)
        {
            if (tray == null)
                return;

            if (queuesByFoodType.TryGetValue(foodType, out List<Tray> queue))
                queue.Remove(tray);
        }

        /// <summary>
        /// Bu frame'de henüz işlenmediyse, HER food type kuyruğunu kendi
        /// FIFO sırasıyla (önce giren önce) işler — her tepsinin
        /// ProcessCheckedDeliveryPlans()'ını tetikler. Sahnede kaç tane
        /// TrayManager olursa olsun, bu metodu ilk çağıran TrayManager
        /// işi yapar; diğerlerinin çağrısı o frame için no-op olur.
        /// </summary>
        public static void ProcessAllQueuesOncePerFrame()
        {
            if (lastProcessedFrame == Time.frameCount)
                return;

            lastProcessedFrame = Time.frameCount;

            foreach (var kvp in queuesByFoodType)
            {
                List<Tray> queue = kvp.Value;

                if (queue.Count == 0)
                    continue;

                // Anlık kopya: bir tepsi ateş edip tükenirse Despawn
                // olur, bu da Unregister ile bu listeyi DEĞİŞTİRİR.
                // Aynı listeyi enumerate ederken değiştirmek
                // "Collection was modified" hatasına yol açar.
                Tray[] traySnapshot = queue.ToArray();

                foreach (Tray tray in traySnapshot)
                {
                    if (tray != null)
                        tray.ProcessCheckedDeliveryPlans();
                }
            }
        }
    }
}
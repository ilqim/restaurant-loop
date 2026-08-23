using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public static class ConveyorPathBuilder
    {
        // Saat yönünde, Kuzey'den başlayan 8 komşu — TEKİL HÜCRE bazlı.
        // Kalın (2 hücre) boyanmış bir şeritte bu trace SADECE dış konturu
        // izler; iç sıradaki hücreler ziyaret edilmez ve bu artık bir hata
        // değil — onlar sadece görsel genişlik dolgusu.
        private static readonly Vector2Int[] Dirs =
        {
            new(-1, 0), new(-1, 1), new(0, 1), new(1, 1),
            new(1, 0),  new(1, -1), new(0, -1), new(-1, -1),
        };

        public static List<Vector2Int> BuildPath(LevelData data, out bool valid, out string reason,
            bool reverseDirection = false)
        {
            valid = false;
            reason = "";

            if (data.baseRow < 0 || data.baseCol < 0)
            {
                reason = "Base ayarlanmamış.";
                return new List<Vector2Int>();
            }

            var conveyorSet = new HashSet<Vector2Int>();
            for (int r = 0; r < data.rows; r++)
                for (int c = 0; c < data.columns; c++)
                    if (data.GetCell(r, c) == CellType.Conveyor)
                        conveyorSet.Add(new Vector2Int(r, c));

            if (conveyorSet.Count < 4)
            {
                reason = "Conveyor hücre sayısı çok az (en az 4 gerekli).";
                return new List<Vector2Int>();
            }

            var start = new Vector2Int(data.baseRow, data.baseCol);
            if (!conveyorSet.Contains(start))
            {
                reason = "Base hücresi Conveyor değil — dış kontur üzerinde tekil bir hücreye Base koymalısın.";
                return new List<Vector2Int>();
            }

            // -----------------------------------------------------------
            // TRAY BASE KURALI:
            // Tray Base tanımlıysa, path'in Start'tan çıkış yönü OTOMATİK
            // olarak Tray Base'in TERSİ yöne göre belirlenir. Bu, dışarıdan
            // verilen 'reverseDirection' parametresinin ÖNÜNE geçer — Tray
            // Base tanımlıyken manuel override anlamsız kalır, çünkü kural
            // her zaman "Tray Base'in tersi yöne git"tir.
            //
            // Nasıl: aynı izleme algoritmasını (TracePath) HER İKİ yönde de
            // (ileri/geri) deneyip, hangisinin İLK ADIMI Tray Base'e göre
            // daha "uzak" bir yöne gidiyorsa onu seçiyoruz. İzleme
            // algoritmasının kendisi hiç değişmedi — sadece hangi yönde
            // çağrılacağını seçen bir katman bu.
            //
            // Tray Base tanımlı değilse eski davranış (parametre olduğu
            // gibi kullanılır) birebir korunur.
            // -----------------------------------------------------------
            bool effectiveReverse = reverseDirection;

            bool hasTrayBase = data.trayBaseRow >= 0 && data.trayBaseCol >= 0;
            if (hasTrayBase)
            {
                var trayBase = new Vector2Int(data.trayBaseRow, data.trayBaseCol);

                var forwardPath = TracePath(conveyorSet, start, false, out bool forwardValid, out _);
                var backwardPath = TracePath(conveyorSet, start, true, out bool backwardValid, out _);

                if (forwardValid && backwardValid && forwardPath.Count > 1 && backwardPath.Count > 1)
                {
                    Vector2Int toTrayBase = trayBase - start;
                    Vector2Int forwardFirstStep = forwardPath[1] - start;
                    Vector2Int backwardFirstStep = backwardPath[1] - start;

                    // Dot product: pozitif = Tray Base yönüne doğru,
                    // negatif = Tray Base'den uzağa doğru. Daha küçük
                    // (daha negatif) olanı, yani Tray Base'den daha uzağa
                    // giden ilk adımı seçiyoruz.
                    int forwardDot = forwardFirstStep.x * toTrayBase.x + forwardFirstStep.y * toTrayBase.y;
                    int backwardDot = backwardFirstStep.x * toTrayBase.x + backwardFirstStep.y * toTrayBase.y;

                    effectiveReverse = backwardDot < forwardDot;

                    Debug.Log(
                        $"[ConveyorPathBuilder] Start={start} TrayBase={trayBase} " +
                        $"forwardFirstStep={forwardFirstStep} (dot={forwardDot}, {forwardPath.Count} hücre) " +
                        $"backwardFirstStep={backwardFirstStep} (dot={backwardDot}, {backwardPath.Count} hücre) " +
                        $"=> effectiveReverse={effectiveReverse}"
                    );
                }
                else if (backwardValid && !forwardValid)
                {
                    effectiveReverse = true;
                    Debug.Log("[ConveyorPathBuilder] Sadece backward yön geçerli, onu kullanıyorum.");
                }
                else if (forwardValid && !backwardValid)
                {
                    effectiveReverse = false;
                    Debug.Log("[ConveyorPathBuilder] Sadece forward yön geçerli, onu kullanıyorum.");
                }
                else
                {
                    Debug.LogWarning("[ConveyorPathBuilder] Tray Base var ama ne forward ne backward yön geçerli — asıl trace kendi hatasını verecek.");
                }
                // İkisi de geçersizse aşağıdaki asıl trace zaten kendi
                // hata mesajını üretecek — burada susuyoruz.
            }

            return TracePath(conveyorSet, start, effectiveReverse, out valid, out reason);
        }

        /// <summary>
        /// Asıl Moore-neighbor kontur izleme algoritması — DEĞİŞMEDİ.
        /// Sadece eskiden BuildPath'in içinde satır içi duran kodun
        /// aynısı; iki yönü de deneyip karşılaştırabilmek için ayrı bir
        /// metoda çıkarıldı.
        /// </summary>
        private static List<Vector2Int> TracePath(
            HashSet<Vector2Int> conveyorSet,
            Vector2Int start,
            bool reverseDirection,
            out bool valid,
            out string reason)
        {
            valid = false;
            reason = "";

            int backtrackDir = 6; // "Batı'dan geldik" varsayımı

            var path = new List<Vector2Int> { start };
            var current = start;
            int steps = 0;
            int maxSteps = conveyorSet.Count * 8 + 32;

            while (true)
            {
                int foundDirIndex = -1;
                for (int k = 1; k <= 8; k++)
                {
                    int idx = reverseDirection
                        ? ((backtrackDir - k) % 8 + 8) % 8
                        : (backtrackDir + k) % 8;

                    var candidate = current + Dirs[idx];
                    if (conveyorSet.Contains(candidate)) { foundDirIndex = idx; break; }
                }

                if (foundDirIndex == -1)
                {
                    reason = "Base tekil hücreden devam eden bir kontur bulunamadı — Base'in dış sınırda olduğundan emin ol.";
                    return path;
                }

                var next = current + Dirs[foundDirIndex];

                if (next == start && path.Count > 1)
                    break; // kontur Base'e kapandı

                path.Add(next);
                current = next;
                backtrackDir = (foundDirIndex + 4) % 8;

                steps++;
                if (steps > maxSteps)
                {
                    reason = "Path beklenenden çok uzun sürdü (sonsuz döngü koruması tetiklendi).";
                    return path;
                }
            }

            if (path.Count < 4)
            {
                reason = "Kapanan kontur çok kısa.";
                return path;
            }

            // ARTIK "allVisited" kontrolü YOK — kalın şeritteki iç sıra
            // hücreleri kasıtlı olarak ziyaret edilmiyor, bu geçerli.
            valid = true;
            return path;
        }

        public static int FindExitIndex(LevelData data, List<Vector2Int> path)
        {
            if (data.exitRow < 0 || data.exitCol < 0) return -1;
            var exitCell = new Vector2Int(data.exitRow, data.exitCol);
            return path.IndexOf(exitCell);
        }
    }
}
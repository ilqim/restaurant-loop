using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public static class ConveyorPathBuilder
    {
        // Saat yönünde, Kuzey'den başlayan 8 komşu yön. Artık bu yönler
        // tek tek hücreler arasında değil, BLOK (2x2) merkezleri arasında
        // uygulanıyor — her adım bir sonraki bloğa geçiyor, bloğun içindeki
        // 4 hücreyi tek tek dolaşmıyor.
        private static readonly Vector2Int[] Dirs =
        {
            new(-1, 0), new(-1, 1), new(0, 1), new(1, 1),
            new(1, 0),  new(1, -1), new(0, -1), new(-1, -1),
        };

        /// Döndürülen liste, her elemanı bir 2x2 bloğun SOL-ÜST hücre
        /// koordinatı (origin) olan bir path'tir — Base'in ve Exit'in
        /// origin'iyle birebir aynı formatta, GridManager bunu blok
        /// MERKEZİNE çevirip world pozisyonu hesaplıyor.
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

            int step = LevelData.ConveyorBlockSize;

            // Conveyor hücrelerinden, ait oldukları blok origin'lerini
            // (her zaman 'step'in katı) çıkar — tekilleştirilmiş blok kümesi.
            var blockOrigins = new HashSet<Vector2Int>();
            for (int r = 0; r < data.rows; r++)
            {
                for (int c = 0; c < data.columns; c++)
                {
                    if (data.GetCell(r, c) != CellType.Conveyor) continue;
                    int originR = (r / step) * step;
                    int originC = (c / step) * step;
                    blockOrigins.Add(new Vector2Int(originR, originC));
                }
            }

            if (blockOrigins.Count < 2)
            {
                reason = "Conveyor bloğu sayısı çok az (en az 2 blok gerekli).";
                return new List<Vector2Int>();
            }

            var start = new Vector2Int(data.baseRow, data.baseCol);
            if (!blockOrigins.Contains(start))
            {
                reason = "Base bloğu Conveyor değil.";
                return new List<Vector2Int>();
            }

            int backtrackDir = 6; // "Batı'dan geldik" varsayımıyla başla

            var path = new List<Vector2Int> { start };
            var current = start;
            int steps = 0;
            int maxSteps = blockOrigins.Count * 6 + 16;

            while (true)
            {
                int foundDirIndex = -1;
                for (int k = 1; k <= 8; k++)
                {
                    // reverseDirection=false (varsayılan/işaretsiz) artık
                    // senin doğru bulduğun yönü veriyor — önceki turdaki
                    // "işaretlemem gerekiyor" kafa karışıklığı burada
                    // tersine çevrilerek çözüldü.
                    int idx = reverseDirection
                        ? (backtrackDir + k) % 8
                        : ((backtrackDir - k) % 8 + 8) % 8;

                    var candidateOrigin = current + Dirs[idx] * step;
                    if (blockOrigins.Contains(candidateOrigin)) { foundDirIndex = idx; break; }
                }

                if (foundDirIndex == -1)
                {
                    reason = "İzole/erişilemez bir conveyor bloğuna ulaşıldı — path devam edemiyor.";
                    return path;
                }

                var next = current + Dirs[foundDirIndex] * step;

                if (next == start && path.Count > 1)
                    break; // loop Base'e kapandı

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

            if (path.Count < 2)
            {
                reason = "Kapanan loop çok kısa.";
                return path;
            }

            valid = true;
            return path;
        }

        /// Path içindeki Exit bloğunun index'i (origin karşılaştırması,
        /// ekstra bölme işlemi gerekmiyor çünkü path zaten origin tutuyor).
        public static int FindExitIndex(LevelData data, List<Vector2Int> path)
        {
            if (data.exitRow < 0 || data.exitCol < 0) return -1;
            var exitOrigin = new Vector2Int(data.exitRow, data.exitCol);
            return path.IndexOf(exitOrigin);
        }
    }
}
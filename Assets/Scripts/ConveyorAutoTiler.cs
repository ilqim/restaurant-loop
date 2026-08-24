using UnityEngine;

namespace RestaurantLoop
{
    public enum ConveyorTileType
    {
        Straight,
        InnerCorner,
        OuterCorner,
        Start,
        Exit,
        BaseOpening,
        BaseCover
    }

    public struct ConveyorTileInfo
    {
        public ConveyorTileType Type;
        public Vector3 Forward; // world-space, Y=0, normalize edilmiş
    }

    /// <summary>
    /// Level verisindeki her Conveyor hücresinin görsel tipini ve yönünü
    /// TAMAMEN OTOMATİK olarak, sadece kardinal/diyagonal komşulara
    /// bakarak çıkarır. Elle işaretleme veya path sırası bilgisi GEREKMEZ.
    ///
    /// Köşe rotasyon kuralı (hem Inner hem Outer için aynı tablo):
    ///   Kuzey+Doğu  -> 0°   (varsayılan: yukarıdan sağa dönüş)
    ///   Batı+Kuzey  -> 90°  (soldan yukarı dönüş)
    ///   Güney+Batı  -> 180° (aşağıdan sola dönüş)
    ///   Doğu+Güney  -> 270° (sağdan aşağı dönüş)
    /// </summary>
    public static class ConveyorAutoTiler
    {
        private static readonly Vector2Int North = new(-1, 0);
        private static readonly Vector2Int South = new(1, 0);
        private static readonly Vector2Int East = new(0, 1);
        private static readonly Vector2Int West = new(0, -1);

        public static ConveyorTileInfo Classify(LevelData data, GameGrid grid, int row, int col)
        {
            if (data.IsCellInBaseBlock(row, col))
                return new ConveyorTileInfo { Type = ConveyorTileType.Start, Forward = BlockOutwardFacing(data, grid, row, col, data.baseRow, data.baseCol) };

            if (data.IsCellInExitBlock(row, col))
                return new ConveyorTileInfo { Type = ConveyorTileType.Exit, Forward = BlockOutwardFacing(data, grid, row, col, data.exitRow, data.exitCol) };

            bool n = IsConveyor(data, row + North.x, col + North.y);
            bool e = IsConveyor(data, row + East.x, col + East.y);
            bool s = IsConveyor(data, row + South.x, col + South.y);
            bool w = IsConveyor(data, row + West.x, col + West.y);

            int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);

            switch (count)
            {
                case 4:
                    return ClassifyInnerCorner(data, grid, row, col);

                case 3:
                {
                    Vector2Int missing = !n ? North : !e ? East : !s ? South : West;
                    return new ConveyorTileInfo
                    {
                        Type = ConveyorTileType.Straight,
                        Forward = WorldDir(grid, row, col, missing)
                    };
                }

                case 2:
                {
                    bool opposite = (n && s) || (e && w);
                    if (opposite)
                    {
                        Debug.LogWarning($"ConveyorAutoTiler: ({row},{col}) ince (1 hücre) şerit tespit edildi — beklenmiyordu.");
                        Vector2Int fallback = n ? East : North;
                        return new ConveyorTileInfo { Type = ConveyorTileType.Straight, Forward = WorldDir(grid, row, col, fallback) };
                    }

                    return new ConveyorTileInfo
                    {
                        Type = ConveyorTileType.OuterCorner,
                        Forward = CornerForward(grid, row, col, n, e, s, w)
                    };
                }

                default:
                    Debug.LogWarning($"ConveyorAutoTiler: ({row},{col}) beklenmedik komşu sayısı ({count}). Şerit 2 hücre genişliğinde değil olabilir.");
                    return new ConveyorTileInfo { Type = ConveyorTileType.Straight, Forward = Vector3.forward };
            }
        }

        private static ConveyorTileInfo ClassifyInnerCorner(LevelData data, GameGrid grid, int row, int col)
        {
            bool missingNW = !IsConveyor(data, row - 1, col - 1);
            bool missingNE = !IsConveyor(data, row - 1, col + 1);
            bool missingSE = !IsConveyor(data, row + 1, col + 1);
            bool missingSW = !IsConveyor(data, row + 1, col - 1);

            Vector3 forward;

            // Eksik diyagonal, kendisine komşu iki kardinal yöne çevrilip
            // AYNI tabloya (CornerForward) sokuluyor.
            if (missingNE) forward = CornerForward(grid, row, col, true, true, false, false);  // Kuzey+Doğu
            else if (missingNW) forward = CornerForward(grid, row, col, true, false, false, true); // Kuzey+Batı
            else if (missingSW) forward = CornerForward(grid, row, col, false, false, true, true); // Güney+Batı
            else if (missingSE) forward = CornerForward(grid, row, col, false, true, true, false); // Doğu+Güney
            else forward = Vector3.forward; // tüm diyagonaller dolu — çok nadir, belirsiz

            return new ConveyorTileInfo { Type = ConveyorTileType.InnerCorner, Forward = forward };
        }

        /// <summary>
        /// Ortak köşe-rotasyon tablosu — hem Outer hem Inner corner
        /// bunu kullanır: N+E->0°, W+N->90°, S+W->180°, E+S->270°.
        /// </summary>
        private static Vector3 CornerForward(GameGrid grid, int row, int col, bool n, bool e, bool s, bool w)
        {
            if (n && e) return WorldDir(grid, row, col, North);
            if (w && n) return WorldDir(grid, row, col, West);
            if (s && w) return WorldDir(grid, row, col, South);
            if (e && s) return WorldDir(grid, row, col, East);
            return Vector3.forward;
        }

        private static bool IsConveyor(LevelData data, int row, int col)
        {
            if (row < 0 || row >= data.rows || col < 0 || col >= data.columns) return false;
            return data.GetCell(row, col) == CellType.Conveyor;
        }

        private static Vector3 WorldDir(GameGrid grid, int row, int col, Vector2Int cellOffset)
        {
            Vector3 a = grid.GetCellCenterWorld(row, col);
            Vector3 b = grid.GetCellCenterWorld(row + cellOffset.x, col + cellOffset.y);
            Vector3 dir = b - a;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        private static Vector3 BlockOutwardFacing(LevelData data, GameGrid grid, int row, int col, int originRow, int originCol)
        {
            Vector2Int[] dirs = { North, South, East, West };
            foreach (var d in dirs)
            {
                int nr = row + d.x, nc = col + d.y;
                bool inBlock = nr >= originRow && nr < originRow + LevelData.ConveyorBlockSize &&
                               nc >= originCol && nc < originCol + LevelData.ConveyorBlockSize;
                if (inBlock) continue;

                if (IsConveyor(data, nr, nc))
                    return WorldDir(grid, row, col, d);
            }
            return Vector3.forward;
        }
    }
}
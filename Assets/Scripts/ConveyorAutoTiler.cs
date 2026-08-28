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
    /// bakarak çıkarır.
    ///
    /// START / EXIT KURALI:
    /// Start ve Exit artık 2x2 bloğun TAMAMI değil. Blok, yol ekseni
    /// boyunca ikiye ayrılır: "bağlantı tarafı" (path'in geri kalanına
    /// komşu olan yarı) her zaman normal Straight hücresi gibi davranır
    /// (standart sayım mantığına düşer) — "açık taraf" (path'in geri
    /// kalanına komşu OLMAYAN yarı) Start/Exit olur.
    ///
    /// Açık taraftaki 2 hücrenin yönü artık 180° KARŞILIKLI DEĞİL — biri
    /// referans (0°, kendi eksik komşusuna bakarak WidthAxisFacing ile
    /// hesaplanır), diğeri buna göre -90° bağıl farkla döner. Hangi
    /// hücrenin referans olduğu widthIndex==0 ile belirlenir (genişlik
    /// ekseni boyunca blok içindeki pozisyon, 0 ya da 1).
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
            if (TryClassifyEndpointBlock(data, grid, row, col, data.baseRow, data.baseCol, ConveyorTileType.Start, out var startInfo))
                return startInfo;

            if (TryClassifyEndpointBlock(data, grid, row, col, data.exitRow, data.exitCol, ConveyorTileType.Exit, out var exitInfo))
                return exitInfo;

            // Start/Exit bloğunun "bağlantı tarafı" hücreleri BURAYA
            // düşer ve normal hücreler gibi sınıflandırılır (Straight
            // olarak çıkarlar çünkü yapısal olarak zaten 3 komşuya
            // sahipler — Corner tablosuna hiç girmezler).

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

        /// <summary>
        /// (row,col) verilen Start/Exit bloğunun içindeyse ve bloğun
        /// "açık tarafına" düşüyorsa true döner ve info'yu doldurur.
        /// Bloğun "bağlantı tarafına" düşüyorsa false döner — çağıran
        /// bu durumda normal sayım mantığına devam eder.
        /// </summary>
        private static bool TryClassifyEndpointBlock(
            LevelData data, GameGrid grid, int row, int col,
            int originRow, int originCol, ConveyorTileType endpointType,
            out ConveyorTileInfo info)
        {
            info = default;
            if (originRow < 0 || originCol < 0) return false;

            bool inBlock = row >= originRow && row < originRow + LevelData.ConveyorBlockSize &&
                           col >= originCol && col < originCol + LevelData.ConveyorBlockSize;
            if (!inBlock) return false;

            bool hasNorth = RowHasConveyor(data, originRow - 1, originCol, originCol + 1);
            bool hasSouth = RowHasConveyor(data, originRow + LevelData.ConveyorBlockSize, originCol, originCol + 1);
            bool hasWest = ColHasConveyor(data, originCol - 1, originRow, originRow + 1);
            bool hasEast = ColHasConveyor(data, originCol + LevelData.ConveyorBlockSize, originRow, originRow + 1);

            bool splitByColumn;   // true: açık/bağlantı SÜTUNA göre ayrışıyor (yol yatay)
            int connectingIndex;  // 0 = origin satırı/sütunu, 1 = +1'lik satır/sütun

            if (hasEast) { splitByColumn = true; connectingIndex = 1; }
            else if (hasWest) { splitByColumn = true; connectingIndex = 0; }
            else if (hasNorth) { splitByColumn = false; connectingIndex = 0; }
            else if (hasSouth) { splitByColumn = false; connectingIndex = 1; }
            else
            {
                // Bağlantı bulunamadı (izole blok) — güvenli varsayım:
                // tüm blok açık taraf sayılsın.
                splitByColumn = true;
                connectingIndex = -1;
            }

            int localIndex = splitByColumn ? (col - originCol) : (row - originRow);
            bool isOpening = localIndex != connectingIndex;

            if (!isOpening)
                return false; // bağlantı tarafı — normal Straight mantığına düş

            // splitByColumn true  -> genişlik ekseni DİKEY  (N/S'e bak) -> widthIndex = row - originRow
            // splitByColumn false -> genişlik ekseni YATAY  (E/W'a bak) -> widthIndex = col - originCol
            int widthIndex = splitByColumn ? (row - originRow) : (col - originCol);

            // Referans hücrenin (widthIndex==0) GERÇEK koordinatları — hangi
            // hücre şu an sınıflandırılıyor olursa olsun, facing HER ZAMAN bu
            // referans hücreye göre hesaplanır, sonra ikinci hücreye -90°
            // bağıl fark uygulanır. Böylece iki paralel hücre artık 180°
            // KARŞILIKLI DEĞİL — biri 0° (referans), diğeri -90° dönük olur.
            int refRow = splitByColumn ? originRow : row;
            int refCol = splitByColumn ? col : originCol;

            Vector3 baseFacing = WidthAxisFacing(data, grid, refRow, refCol, widthIsVertical: splitByColumn);
            Vector3 facing = widthIndex == 0
                ? baseFacing
                : Quaternion.Euler(0f, -90f, 0f) * baseFacing;

            info = new ConveyorTileInfo { Type = endpointType, Forward = facing };
            return true;
        }

        /// <summary>
        /// Genişlik ekseni boyunca (yol eksenine dik) hangi komşunun eksik
        /// olduğuna bakarak facing üretir. Sadece REFERANS hücre (widthIndex==0)
        /// için çağrılır — ikinci hücrenin facing'i buna -90° eklenerek elde edilir.
        /// </summary>
        private static Vector3 WidthAxisFacing(LevelData data, GameGrid grid, int row, int col, bool widthIsVertical)
        {
            if (widthIsVertical)
            {
                if (!IsConveyor(data, row - 1, col)) return WorldDir(grid, row, col, North);
                if (!IsConveyor(data, row + 1, col)) return WorldDir(grid, row, col, South);
            }
            else
            {
                if (!IsConveyor(data, row, col + 1)) return WorldDir(grid, row, col, East);
                if (!IsConveyor(data, row, col - 1)) return WorldDir(grid, row, col, West);
            }

            return Vector3.forward; // beklenmeyen durum — iki taraf da dolu
        }

        private static bool RowHasConveyor(LevelData data, int row, int colStart, int colEndInclusive)
        {
            for (int c = colStart; c <= colEndInclusive; c++)
                if (IsConveyor(data, row, c)) return true;
            return false;
        }

        private static bool ColHasConveyor(LevelData data, int col, int rowStart, int rowEndInclusive)
        {
            for (int r = rowStart; r <= rowEndInclusive; r++)
                if (IsConveyor(data, r, col)) return true;
            return false;
        }

        private static ConveyorTileInfo ClassifyInnerCorner(LevelData data, GameGrid grid, int row, int col)
        {
            bool missingNW = !IsConveyor(data, row - 1, col - 1);
            bool missingNE = !IsConveyor(data, row - 1, col + 1);
            bool missingSE = !IsConveyor(data, row + 1, col + 1);
            bool missingSW = !IsConveyor(data, row + 1, col - 1);

            Vector3 forward;

            if (missingNE) forward = CornerForward(grid, row, col, true, true, false, false);
            else if (missingNW) forward = CornerForward(grid, row, col, true, false, false, true);
            else if (missingSW) forward = CornerForward(grid, row, col, false, false, true, true);
            else if (missingSE) forward = CornerForward(grid, row, col, false, true, true, false);
            else forward = Vector3.forward;

            return new ConveyorTileInfo { Type = ConveyorTileType.InnerCorner, Forward = forward };
        }

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
    }
}
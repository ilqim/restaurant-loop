using UnityEngine;

namespace RestaurantLoop
{
    public class GameGrid
    {
        private readonly Grid unityGrid;
        private readonly int rows;
        private readonly int columns;
        private readonly bool invertRow;
        private readonly bool invertCol;
        private readonly bool swapAxes;

        public GameGrid(Grid grid, int rows, int columns, bool invertRow = false, bool invertCol = false, bool swapAxes = false)
        {
            unityGrid = grid;
            this.rows = rows;
            this.columns = columns;
            this.invertRow = invertRow;
            this.invertCol = invertCol;
            this.swapAxes = swapAxes;
        }

        // ÖNEMLİ: invert artık "rows-1-row" ile yeniden numaralandırma
        // yapmıyor, bunun yerine NEGATİF yönde büyüyor. Bu sayede
        // (row=0, col=0) her zaman Vector3Int.zero'ya eşit kalır — ve
        // Unity Vector3Int.zero'yu her zaman GridManager'ın transform
        // pozisyonuna (yani objenin gizmo'sunun durduğu yere) yerleştirir.
        // Sonuç: hangi invert/swap kombinasyonunu seçersen seç, (0,0)
        // hücresi HER ZAMAN GridManager objesinin durduğu köşe olur —
        // etiket okumaya gerek kalmadan gözle doğrulanabilir.
        public Vector3Int RowColToCell(int row, int col)
        {
            int r = invertRow ? -row : row;
            int c = invertCol ? -col : col;
            return swapAxes ? new Vector3Int(r, 0, c) : new Vector3Int(c, 0, r);
        }

        public void CellToRowCol(Vector3Int cell, out int row, out int col)
        {
            int rawR, rawC;
            if (swapAxes) { rawR = cell.x; rawC = cell.z; }
            else { rawC = cell.x; rawR = cell.z; }

            row = invertRow ? -rawR : rawR;
            col = invertCol ? -rawC : rawC;
        }

        public Vector3 GetCellCenterWorld(int row, int col) =>
            unityGrid.GetCellCenterWorld(RowColToCell(row, col));

        public bool TryGetRowColFromWorld(Vector3 worldPos, out int row, out int col)
        {
            Vector3Int cell = unityGrid.WorldToCell(worldPos);
            CellToRowCol(cell, out row, out col);
            return row >= 0 && row < rows && col >= 0 && col < columns;
        }
    }
}
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RestaurantLoop.EditorTools
{
    [CustomEditor(typeof(LevelData))]
    public class LevelDataEditor : Editor
    {
        private enum PaintMode
        {
            Conveyor, Erase, SetBase, SetExit,
            Hamburger, Fries, Drink, Sushi, Steak, Dessert
        }

        private const float CellPixelSize = 22f;

        private static readonly Dictionary<FoodType, Color> FoodColors = new()
        {
            { FoodType.Hamburger, new Color(0.90f, 0.20f, 0.20f) },
            { FoodType.Fries,     new Color(0.95f, 0.80f, 0.10f) },
            { FoodType.Drink,     new Color(0.20f, 0.45f, 0.95f) },
            { FoodType.Sushi,     new Color(0.20f, 0.75f, 0.30f) },
            { FoodType.Steak,     new Color(0.45f, 0.28f, 0.15f) },
            { FoodType.Dessert,   new Color(0.65f, 0.25f, 0.85f) },
        };
        private static readonly Color ConveyorColor = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color BaseColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color ExitColor = new Color(0.2f, 0.9f, 0.85f);

        private PaintMode currentMode = PaintMode.Conveyor;
        private bool isPainting;
        private int lastPaintedRow = -1, lastPaintedCol = -1;
        private int undoGroupAtStroke;
        private Vector2 scrollPos;

        public override void OnInspectorGUI()
        {
            var levelData = (LevelData)target;

            EditorGUILayout.LabelField("Level Grid Boyutu", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int newRows = EditorGUILayout.IntField("Rows", levelData.rows);
            int newCols = EditorGUILayout.IntField("Columns", levelData.columns);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(levelData, "Change Grid Size");
                levelData.rows = Mathf.Clamp(newRows, 1, 40);
                levelData.columns = Mathf.Clamp(newCols, 1, 40);
                EditorUtility.SetDirty(levelData);
            }

            if (GUILayout.Button("Apply Grid"))
            {
                Undo.RecordObject(levelData, "Apply Grid");
                levelData.ResizeCells();
                EditorUtility.SetDirty(levelData);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Fırça", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            DrawToolButton(PaintMode.Conveyor, "Conveyor (2x2 boya)", ConveyorColor);
            DrawToolButton(PaintMode.Erase, "Erase (2x2)", Color.white);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawToolButton(PaintMode.SetBase, "Set Base (2x2 blok)", BaseColor);
            DrawToolButton(PaintMode.SetExit, "Set Exit (2x2 blok)", ExitColor);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawToolButton(PaintMode.Hamburger, "Hamburger", FoodColors[FoodType.Hamburger]);
            DrawToolButton(PaintMode.Fries, "Fries", FoodColors[FoodType.Fries]);
            DrawToolButton(PaintMode.Drink, "Drink", FoodColors[FoodType.Drink]);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawToolButton(PaintMode.Sushi, "Sushi", FoodColors[FoodType.Sushi]);
            DrawToolButton(PaintMode.Steak, "Steak", FoodColors[FoodType.Steak]);
            DrawToolButton(PaintMode.Dessert, "Dessert", FoodColors[FoodType.Dessert]);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Conveyor 2x2'lik bloklar halinde boyanır. Base ve Exit de ARTIK 2x2 blok — " +
                "tıkladığın hücrenin ait olduğu 2x2 blok otomatik Conveyor'a boyanır ve blok " +
                "origin'i (sol-üst köşe) Base/Exit olarak kaydedilir. Path her zaman blok " +
                "merkezinden başlar/biter ve dış konturun tamamını (en uzun turu) izler.",
                MessageType.Info);

            var path = ConveyorPathBuilder.BuildPath(levelData, out bool pathValid, out string pathReason);
            EditorGUILayout.HelpBox(
                pathValid ? $"✓ Kontur geçerli — {path.Count} hücre (tekil, dış sınır)." : $"⚠ {pathReason}",
                pathValid ? MessageType.Info : MessageType.Warning);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);

            if (levelData.cells == null || levelData.cells.Length != levelData.rows * levelData.columns)
            {
                EditorGUILayout.HelpBox("Cells boyutu rows×columns ile eşleşmiyor. Önce 'Apply Grid' butonuna bas.", MessageType.Warning);
                return;
            }

            float gridPixelWidth = levelData.columns * CellPixelSize;
            float gridPixelHeight = levelData.rows * CellPixelSize;

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(Mathf.Min(gridPixelHeight + 20, 420)));
            Rect gridRect = GUILayoutUtility.GetRect(gridPixelWidth, gridPixelHeight);

            HandleMouseInput(levelData, gridRect);
            DrawGrid(levelData, gridRect);

            EditorGUILayout.EndScrollView();

            if (isPainting) Repaint();
        }

        private void DrawToolButton(PaintMode mode, string label, Color tint)
        {
            bool isActive = currentMode == mode;
            GUI.backgroundColor = tint;
            if (GUILayout.Button(isActive ? "✓ " + label : label)) currentMode = mode;
            GUI.backgroundColor = Color.white;
        }

        private void DrawGrid(LevelData levelData, Rect gridRect)
        {
            for (int r = 0; r < levelData.rows; r++)
            {
                for (int c = 0; c < levelData.columns; c++)
                {
                    Rect cellRect = new Rect(
                        gridRect.x + c * CellPixelSize,
                        gridRect.y + r * CellPixelSize,
                        CellPixelSize - 1, CellPixelSize - 1);

                    CellType type = levelData.GetCell(r, c);
                    bool isBase = levelData.IsCellInBaseBlock(r, c);
                    bool isExit = levelData.IsCellInExitBlock(r, c);

                    Color color = type switch
                    {
                        CellType.Conveyor     => ConveyorColor,
                        CellType.CustomerSlot => levelData.TryGetCustomerFood(r, c, out var food)
                                                    ? FoodColors[food] : new Color(0.25f, 0.25f, 0.25f),
                        _                     => new Color(0.18f, 0.18f, 0.18f)
                    };
                    if (isBase) color = BaseColor;
                    else if (isExit) color = ExitColor;

                    EditorGUI.DrawRect(cellRect, color);

                    bool isBaseOrigin = levelData.baseRow == r && levelData.baseCol == c;
                    bool isExitOrigin = levelData.exitRow == r && levelData.exitCol == c;
                    if (isBaseOrigin)
                        EditorGUI.LabelField(cellRect, "B", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
                    if (isExitOrigin)
                        EditorGUI.LabelField(cellRect, "E", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
                }
            }

            Handles.color = new Color(1f, 1f, 1f, 0.08f);
            for (int r = 0; r <= levelData.rows; r++)
                Handles.DrawLine(new Vector3(gridRect.x, gridRect.y + r * CellPixelSize),
                                  new Vector3(gridRect.xMax, gridRect.y + r * CellPixelSize));
            for (int c = 0; c <= levelData.columns; c++)
                Handles.DrawLine(new Vector3(gridRect.x + c * CellPixelSize, gridRect.y),
                                  new Vector3(gridRect.x + c * CellPixelSize, gridRect.yMax));

            Handles.color = new Color(0f, 0f, 0f, 0.5f);
            for (int r = 0; r <= levelData.rows; r += LevelData.ConveyorBlockSize)
                Handles.DrawLine(new Vector3(gridRect.x, gridRect.y + r * CellPixelSize),
                                  new Vector3(gridRect.xMax, gridRect.y + r * CellPixelSize));
            for (int c = 0; c <= levelData.columns; c += LevelData.ConveyorBlockSize)
                Handles.DrawLine(new Vector3(gridRect.x + c * CellPixelSize, gridRect.y),
                                  new Vector3(gridRect.x + c * CellPixelSize, gridRect.yMax));
        }

        private void HandleMouseInput(LevelData levelData, Rect gridRect)
        {
            Event e = Event.current;
            if (!gridRect.Contains(e.mousePosition)) return;

            bool isMouseDown = e.type == EventType.MouseDown && e.button == 0;
            bool isMouseDrag  = e.type == EventType.MouseDrag && e.button == 0;
            bool isMouseUp    = e.type == EventType.MouseUp && e.button == 0;
            if (!isMouseDown && !isMouseDrag && !isMouseUp) return;

            int col = Mathf.FloorToInt((e.mousePosition.x - gridRect.x) / CellPixelSize);
            int row = Mathf.FloorToInt((e.mousePosition.y - gridRect.y) / CellPixelSize);
            if (row < 0 || row >= levelData.rows || col < 0 || col >= levelData.columns) return;

            if (isMouseDown)
            {
                isPainting = true;
                lastPaintedRow = lastPaintedCol = -1;
                undoGroupAtStroke = Undo.GetCurrentGroup();
                Undo.RecordObject(levelData, "Paint Level Grid");
                ApplyBrush(levelData, row, col);
                e.Use();
            }
            else if (isMouseDrag && isPainting)
            {
                if (row != lastPaintedRow || col != lastPaintedCol)
                    ApplyBrush(levelData, row, col);
                e.Use();
            }
            else if (isMouseUp && isPainting)
            {
                isPainting = false;
                Undo.CollapseUndoOperations(undoGroupAtStroke);
                EditorUtility.SetDirty(levelData);
                e.Use();
            }
        }

        private void ApplyBrush(LevelData levelData, int row, int col)
        {
            lastPaintedRow = row;
            lastPaintedCol = col;

            switch (currentMode)
            {
                case PaintMode.Conveyor:
                    PaintConveyorBlock(levelData, row, col);
                    break;

                case PaintMode.Erase:
                    EraseAt(levelData, row, col);
                    break;

                case PaintMode.SetBase:
                    SetBaseOrExitBlock(levelData, row, col, isBase: true);
                    break;

                case PaintMode.SetExit:
                    SetBaseOrExitBlock(levelData, row, col, isBase: false);
                    break;

                default:
                    if (levelData.GetCell(row, col) != CellType.Conveyor)
                        levelData.SetCustomerAt(row, col, PaintModeToFood(currentMode));
                    break;
            }
        }

        /// <summary>
        /// Base/Exit ARTIK 2x2 blok. Tıklanan hücrenin ait olduğu bloğun
        /// origin'i (sol-üst) hesaplanır, bloğun 4 hücresi otomatik olarak
        /// Conveyor'a boyanır (henüz değilse) ve varsa üzerlerindeki
        /// customer kaydı temizlenir — Conveyor fırçasıyla birebir aynı
        /// snap mantığı.
        /// </summary>
        private void SetBaseOrExitBlock(LevelData levelData, int row, int col, bool isBase)
        {
            int originRow = (row / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;
            int originCol = (col / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;

            if (originRow + LevelData.ConveyorBlockSize > levelData.rows ||
                originCol + LevelData.ConveyorBlockSize > levelData.columns)
            {
                Debug.LogWarning($"Set {(isBase ? "Base" : "Exit")}: 2x2 blok grid sınırlarının dışına taşıyor.");
                return;
            }

            PaintConveyorBlock(levelData, originRow, originCol);

            if (isBase) { levelData.baseRow = originRow; levelData.baseCol = originCol; }
            else { levelData.exitRow = originRow; levelData.exitCol = originCol; }
        }

        private void PaintConveyorBlock(LevelData levelData, int row, int col)
        {
            int originRow = (row / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;
            int originCol = (col / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;

            for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
            {
                for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                {
                    int rr = originRow + dr, cc = originCol + dc;
                    if (rr >= levelData.rows || cc >= levelData.columns) continue;
                    levelData.SetCell(rr, cc, CellType.Conveyor);
                    levelData.RemoveCustomerAt(rr, cc);
                }
            }
        }

        private void EraseAt(LevelData levelData, int row, int col)
        {
            if (levelData.GetCell(row, col) == CellType.Conveyor)
            {
                int originRow = (row / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;
                int originCol = (col / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;
                for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
                {
                    for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                    {
                        int rr = originRow + dr, cc = originCol + dc;
                        if (rr >= levelData.rows || cc >= levelData.columns) continue;
                        levelData.SetCell(rr, cc, CellType.Empty);
                        if (levelData.baseRow == originRow && levelData.baseCol == originCol)
                        { levelData.baseRow = -1; levelData.baseCol = -1; }
                        if (levelData.exitRow == originRow && levelData.exitCol == originCol)
                        { levelData.exitRow = -1; levelData.exitCol = -1; }
                    }
                }
            }
            else
            {
                levelData.SetCell(row, col, CellType.Empty);
                levelData.RemoveCustomerAt(row, col);
            }
        }

        private static FoodType PaintModeToFood(PaintMode mode) => mode switch
        {
            PaintMode.Hamburger => FoodType.Hamburger,
            PaintMode.Fries     => FoodType.Fries,
            PaintMode.Drink     => FoodType.Drink,
            PaintMode.Sushi     => FoodType.Sushi,
            PaintMode.Steak     => FoodType.Steak,
            PaintMode.Dessert   => FoodType.Dessert,
            _ => FoodType.Hamburger
        };
    }
}
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
            Conveyor, Erase, SetStart, SetExit, SetTrayBase,
            Hamburger, Fries, Drink, Sushi, Steak, Donut
        }

        private enum QueuePaintMode
        {
            Erase, Hamburger, Fries, Drink, Sushi, Steak, Donut
        }

        private const float CellPixelSize = 22f;
        private const int QueueExtraRowsBuffer = 3; // boyanmış son satırdan sonra kaç boş satır daha göster (genişleyebilsin diye)
        private const int QueueMinRows = 5;

        private static readonly Dictionary<FoodType, Color> FoodColors = new()
        {
            { FoodType.Hamburger, new Color(0.90f, 0.20f, 0.20f) },
            { FoodType.Fries,     new Color(0.95f, 0.80f, 0.10f) },
            { FoodType.Drink,     new Color(0.20f, 0.45f, 0.95f) },
            { FoodType.Sushi,     new Color(0.20f, 0.75f, 0.30f) },
            { FoodType.Steak,     new Color(0.45f, 0.28f, 0.15f) },
            { FoodType.Donut,   new Color(0.65f, 0.25f, 0.85f) },
        };
        private static readonly Color ConveyorColor = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color StartColor = new Color(1f, 0.85f, 0.2f);      // eskiden "Base" rengi — artık "Start"
        private static readonly Color ExitColor = new Color(0.2f, 0.9f, 0.85f);
        private static readonly Color TrayBaseColor = new Color(0.85f, 0.45f, 0.95f); // yeni — boş tepsi park yeri

        private PaintMode currentMode = PaintMode.Conveyor;
        private bool isPainting;
        private int lastPaintedRow = -1, lastPaintedCol = -1;
        private int undoGroupAtStroke;
        private Vector2 scrollPos;

        private QueuePaintMode currentQueueMode = QueuePaintMode.Hamburger;
        private int queuePaintCapacity = 10;
        private bool paintAsSurprise = false;
        private bool isPaintingQueue;
        private int lastPaintedQueueRow = -1, lastPaintedQueueCol = -1;
        private int queueUndoGroupAtStroke;
        private Vector2 queueScrollPos;

        public override void OnInspectorGUI()
        {
            var levelData = (LevelData)target;

            DrawGridSection(levelData);
            EditorGUILayout.Space(16);
            DrawQueueSection(levelData);
        }

        // =====================================================================
        // LEVEL GRID (conveyor / customer)
        // =====================================================================

        private void DrawGridSection(LevelData levelData)
        {
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
            DrawToolButton(PaintMode.SetStart, "Set Start (2x2 blok)", StartColor);
            DrawToolButton(PaintMode.SetExit, "Set Exit (2x2 blok)", ExitColor);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawToolButton(PaintMode.SetTrayBase, "Set Tray Base (2x2 blok)", TrayBaseColor);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawToolButton(PaintMode.Hamburger, "Hamburger", FoodColors[FoodType.Hamburger]);
            DrawToolButton(PaintMode.Fries, "Fries", FoodColors[FoodType.Fries]);
            DrawToolButton(PaintMode.Drink, "Drink", FoodColors[FoodType.Drink]);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawToolButton(PaintMode.Sushi, "Sushi", FoodColors[FoodType.Sushi]);
            DrawToolButton(PaintMode.Steak, "Steak", FoodColors[FoodType.Steak]);
            DrawToolButton(PaintMode.Donut, "Donut", FoodColors[FoodType.Donut]);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
    "Conveyor 2x2'lik bloklar halinde boyanır. Start ve Exit 2x2 Conveyor bloklarıdır. " +
    "Tray Base ise 2x2'lik ayrı bir alandır ve Conveyor'ın parçası değildir.\n\n" +
    "• Start: yemekler conveyor'a buradan girer (eskiden 'Base' diye adlandırılıyordu).\n" +
    "• Exit: conveyor'dan çıkış noktası.\n" +
    "• Tray Base: boş traylerin park ettiği/stackleneceği yer. " +
    "Sadece referans noktası olarak kullanılır ve conveyor yolunu etkilemez.",
    MessageType.Info);

            var path = ConveyorPathBuilder.BuildPath(levelData, out bool pathValid, out string pathReason);
            EditorGUILayout.HelpBox(
                pathValid ? $"✓ Kontur geçerli — {path.Count} hücre." : $"⚠ {pathReason}",
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
                    bool isStart = levelData.IsCellInBaseBlock(r, c);
                    bool isExit = levelData.IsCellInExitBlock(r, c);
                    bool isTrayBase = levelData.IsCellInTrayBaseBlock(r, c);

                    Color color = type switch
                    {
                        CellType.Conveyor => ConveyorColor,
                        CellType.CustomerSlot => levelData.TryGetCustomerFood(r, c, out var food)
                                                    ? FoodColors[food] : new Color(0.25f, 0.25f, 0.25f),
                        _ => new Color(0.18f, 0.18f, 0.18f)
                    };
                    if (isStart) color = StartColor;
                    else if (isExit) color = ExitColor;
                    else if (isTrayBase) color = TrayBaseColor;

                    EditorGUI.DrawRect(cellRect, color);

                    bool isStartOrigin = levelData.baseRow == r && levelData.baseCol == c;
                    bool isExitOrigin = levelData.exitRow == r && levelData.exitCol == c;
                    bool isTrayBaseOrigin = levelData.trayBaseRow == r && levelData.trayBaseCol == c;
                    if (isStartOrigin)
                        EditorGUI.LabelField(cellRect, "S", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
                    if (isExitOrigin)
                        EditorGUI.LabelField(cellRect, "E", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
                    if (isTrayBaseOrigin)
                        EditorGUI.LabelField(cellRect, "T", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
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
            bool isMouseDrag = e.type == EventType.MouseDrag && e.button == 0;
            bool isMouseUp = e.type == EventType.MouseUp && e.button == 0;
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

                case PaintMode.SetStart:
                    SetSpecialBlock(levelData, row, col, SpecialBlock.Start);
                    break;

                case PaintMode.SetExit:
                    SetSpecialBlock(levelData, row, col, SpecialBlock.Exit);
                    break;

                case PaintMode.SetTrayBase:
                    SetSpecialBlock(levelData, row, col, SpecialBlock.TrayBase);
                    break;

                default:
                    if (levelData.GetCell(row, col) != CellType.Conveyor)
                        levelData.SetCustomerAt(row, col, PaintModeToFood(currentMode));
                    break;
            }
        }

        private enum SpecialBlock { Start, Exit, TrayBase }

        private void SetSpecialBlock(LevelData levelData, int row, int col, SpecialBlock kind)
        {
            int originRow = (row / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;
            int originCol = (col / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;

            if (originRow + LevelData.ConveyorBlockSize > levelData.rows ||
                originCol + LevelData.ConveyorBlockSize > levelData.columns)
            {
                Debug.LogWarning($"Set {kind}: 2x2 blok grid sınırlarının dışına taşıyor.");
                return;
            }

            // ---------------------------------------------------------
            // START ve EXIT conveyor'ın parçasıdır.
            // TRAY BASE conveyor'ın parçası DEĞİLDİR.
            // ---------------------------------------------------------
            if (kind == SpecialBlock.Start || kind == SpecialBlock.Exit)
            {
                PaintConveyorBlock(levelData, originRow, originCol);
            }
            else if (kind == SpecialBlock.TrayBase)
            {
                // Tray Base'in bulunduğu 2x2 alan boş kalır.
                // Böylece conveyor konturuna dahil olmaz.
                for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
                {
                    for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                    {
                        int rr = originRow + dr;
                        int cc = originCol + dc;

                        if (rr >= levelData.rows || cc >= levelData.columns)
                            continue;

                        levelData.SetCell(rr, cc, CellType.Empty);
                        levelData.RemoveCustomerAt(rr, cc);
                    }
                }
            }

            // Özel bloğun koordinatını kaydet
            switch (kind)
            {
                case SpecialBlock.Start:
                    levelData.baseRow = originRow;
                    levelData.baseCol = originCol;
                    break;

                case SpecialBlock.Exit:
                    levelData.exitRow = originRow;
                    levelData.exitCol = originCol;
                    break;

                case SpecialBlock.TrayBase:
                    levelData.trayBaseRow = originRow;
                    levelData.trayBaseCol = originCol;
                    break;
            }
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
            int originRow = (row / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;
            int originCol = (col / LevelData.ConveyorBlockSize) * LevelData.ConveyorBlockSize;

            // ---------------------------------------------------------
            // Önce özel blokları kontrol et
            // ---------------------------------------------------------

            // START
            if (levelData.baseRow == originRow &&
                levelData.baseCol == originCol)
            {
                for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
                {
                    for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                    {
                        int rr = originRow + dr;
                        int cc = originCol + dc;

                        if (rr >= levelData.rows || cc >= levelData.columns)
                            continue;

                        levelData.SetCell(rr, cc, CellType.Empty);
                        levelData.RemoveCustomerAt(rr, cc);
                    }
                }

                levelData.baseRow = -1;
                levelData.baseCol = -1;
                return;
            }

            // EXIT
            if (levelData.exitRow == originRow &&
                levelData.exitCol == originCol)
            {
                for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
                {
                    for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                    {
                        int rr = originRow + dr;
                        int cc = originCol + dc;

                        if (rr >= levelData.rows || cc >= levelData.columns)
                            continue;

                        levelData.SetCell(rr, cc, CellType.Empty);
                        levelData.RemoveCustomerAt(rr, cc);
                    }
                }

                levelData.exitRow = -1;
                levelData.exitCol = -1;
                return;
            }

            // TRAY BASE
            if (levelData.trayBaseRow == originRow &&
                levelData.trayBaseCol == originCol)
            {
                for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
                {
                    for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                    {
                        int rr = originRow + dr;
                        int cc = originCol + dc;

                        if (rr >= levelData.rows || cc >= levelData.columns)
                            continue;

                        levelData.SetCell(rr, cc, CellType.Empty);
                        levelData.RemoveCustomerAt(rr, cc);
                    }
                }

                levelData.trayBaseRow = -1;
                levelData.trayBaseCol = -1;
                return;
            }

            // ---------------------------------------------------------
            // Normal Conveyor bloğu
            // ---------------------------------------------------------

            if (levelData.GetCell(row, col) == CellType.Conveyor)
            {
                for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
                {
                    for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                    {
                        int rr = originRow + dr;
                        int cc = originCol + dc;

                        if (rr >= levelData.rows || cc >= levelData.columns)
                            continue;

                        levelData.SetCell(rr, cc, CellType.Empty);
                        levelData.RemoveCustomerAt(rr, cc);
                    }
                }
            }
            else
            {
                // Normal boş/hücre silme
                levelData.SetCell(row, col, CellType.Empty);
                levelData.RemoveCustomerAt(row, col);
            }
        }

        private static FoodType PaintModeToFood(PaintMode mode) => mode switch
        {
            PaintMode.Hamburger => FoodType.Hamburger,
            PaintMode.Fries => FoodType.Fries,
            PaintMode.Drink => FoodType.Drink,
            PaintMode.Sushi => FoodType.Sushi,
            PaintMode.Steak => FoodType.Steak,
            PaintMode.Donut => FoodType.Donut,
            _ => FoodType.Hamburger
        };

        // =====================================================================
        // FOOD STACK QUEUE — değişmedi
        // =====================================================================

        private void DrawQueueSection(LevelData levelData)
        {
            EditorGUILayout.LabelField("Food Stack Queue", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            int newQueueColumns = EditorGUILayout.IntSlider("Queue Columns (üst sayısı)", levelData.queueColumns,
                LevelData.QueueColumnsMin, LevelData.QueueColumnsMax);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(levelData, "Change Queue Columns");
                levelData.queueColumns = newQueueColumns;
                EditorUtility.SetDirty(levelData);
            }

            EditorGUILayout.HelpBox(
                "row 0 = en üst (oyunda tıklanabilir ilk satır), col 0 = en sol. Derinlik (row) sınırsız — " +
                "runtime'da sadece ilk birkaç satır görünür/tıklanabilir olur, geri kalanı sıradan geldikçe açılır.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            queuePaintCapacity = EditorGUILayout.IntField("Yerleştirilecek Kapasite", Mathf.Max(1, queuePaintCapacity));
            paintAsSurprise = EditorGUILayout.ToggleLeft("Surprise Food?", paintAsSurprise, GUILayout.Width(130));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawQueueToolButton(QueuePaintMode.Erase, "Erase", Color.white);
            DrawQueueToolButton(QueuePaintMode.Hamburger, "Hamburger", FoodColors[FoodType.Hamburger]);
            DrawQueueToolButton(QueuePaintMode.Fries, "Fries", FoodColors[FoodType.Fries]);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawQueueToolButton(QueuePaintMode.Drink, "Drink", FoodColors[FoodType.Drink]);
            DrawQueueToolButton(QueuePaintMode.Sushi, "Sushi", FoodColors[FoodType.Sushi]);
            DrawQueueToolButton(QueuePaintMode.Steak, "Steak", FoodColors[FoodType.Steak]);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawQueueToolButton(QueuePaintMode.Donut, "Donut", FoodColors[FoodType.Donut]);
            EditorGUILayout.EndHorizontal();

            int maxUsedRow = -1;
            foreach (var e in levelData.queue) if (e.row > maxUsedRow) maxUsedRow = e.row;
            int totalRows = Mathf.Max(QueueMinRows, maxUsedRow + 1 + QueueExtraRowsBuffer);

            float gridPixelWidth = levelData.queueColumns * CellPixelSize;
            float gridPixelHeight = totalRows * CellPixelSize;

            queueScrollPos = EditorGUILayout.BeginScrollView(queueScrollPos, GUILayout.Height(Mathf.Min(gridPixelHeight + 20, 300)));
            Rect queueRect = GUILayoutUtility.GetRect(gridPixelWidth, gridPixelHeight);

            HandleQueueMouseInput(levelData, queueRect, totalRows);
            DrawQueueGrid(levelData, queueRect, totalRows);

            EditorGUILayout.EndScrollView();

            if (isPaintingQueue) Repaint();
        }

        private void DrawQueueToolButton(QueuePaintMode mode, string label, Color tint)
        {
            bool isActive = currentQueueMode == mode;
            GUI.backgroundColor = tint;
            if (GUILayout.Button(isActive ? "✓ " + label : label)) currentQueueMode = mode;
            GUI.backgroundColor = Color.white;
        }

        private void DrawQueueGrid(LevelData levelData, Rect gridRect, int totalRows)
        {
            for (int r = 0; r < totalRows; r++)
            {
                for (int c = 0; c < levelData.queueColumns; c++)
                {
                    Rect cellRect = new Rect(
                        gridRect.x + c * CellPixelSize,
                        gridRect.y + r * CellPixelSize,
                        CellPixelSize - 1, CellPixelSize - 1);

                    Color color = new Color(0.18f, 0.18f, 0.18f);
                    string label = null;
                    bool isSurprise = false;

                    if (levelData.TryGetQueueEntry(r, c, out var entry))
                    {
                        color = FoodColors[entry.food];
                        label = entry.capacity.ToString();
                        isSurprise = entry.isSurprise;
                    }

                    // row 0 hafif farklı arka plan tonu — "bu satır tıklanabilir olacak" ipucu
                    if (r == 0 && label == null) color = new Color(0.22f, 0.22f, 0.16f);

                    EditorGUI.DrawRect(cellRect, color);

                    if (isSurprise)
                    {
                        Rect badgeRect = new Rect(cellRect.x + 2, cellRect.y + 1, 10, 10);
                        EditorGUI.LabelField(badgeRect, "?", new GUIStyle(EditorStyles.boldLabel)
                        {
                            fontSize = 9,
                            normal = { textColor = Color.yellow }
                        });
                    }

                    if (label != null)
                        EditorGUI.LabelField(cellRect, label, new GUIStyle(EditorStyles.miniBoldLabel)
                        { alignment = TextAnchor.LowerRight, normal = { textColor = Color.black } });
                }
            }

            Handles.color = new Color(1f, 1f, 1f, 0.08f);
            for (int r = 0; r <= totalRows; r++)
                Handles.DrawLine(new Vector3(gridRect.x, gridRect.y + r * CellPixelSize),
                                  new Vector3(gridRect.xMax, gridRect.y + r * CellPixelSize));
            for (int c = 0; c <= levelData.queueColumns; c++)
                Handles.DrawLine(new Vector3(gridRect.x + c * CellPixelSize, gridRect.y),
                                  new Vector3(gridRect.x + c * CellPixelSize, gridRect.yMax));

            // row 0 sınırını kalın çizgiyle vurgula
            Handles.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Handles.DrawLine(new Vector3(gridRect.x, gridRect.y + CellPixelSize), new Vector3(gridRect.xMax, gridRect.y + CellPixelSize));
        }

        private void HandleQueueMouseInput(LevelData levelData, Rect gridRect, int totalRows)
        {
            Event e = Event.current;
            if (!gridRect.Contains(e.mousePosition)) return;

            bool isMouseDown = e.type == EventType.MouseDown && e.button == 0;
            bool isMouseDrag = e.type == EventType.MouseDrag && e.button == 0;
            bool isMouseUp = e.type == EventType.MouseUp && e.button == 0;
            if (!isMouseDown && !isMouseDrag && !isMouseUp) return;

            int col = Mathf.FloorToInt((e.mousePosition.x - gridRect.x) / CellPixelSize);
            int row = Mathf.FloorToInt((e.mousePosition.y - gridRect.y) / CellPixelSize);
            if (row < 0 || row >= totalRows || col < 0 || col >= levelData.queueColumns) return;

            if (isMouseDown)
            {
                isPaintingQueue = true;
                lastPaintedQueueRow = lastPaintedQueueCol = -1;
                queueUndoGroupAtStroke = Undo.GetCurrentGroup();
                Undo.RecordObject(levelData, "Paint Queue");
                ApplyQueueBrush(levelData, row, col);
                e.Use();
            }
            else if (isMouseDrag && isPaintingQueue)
            {
                if (row != lastPaintedQueueRow || col != lastPaintedQueueCol)
                    ApplyQueueBrush(levelData, row, col);
                e.Use();
            }
            else if (isMouseUp && isPaintingQueue)
            {
                isPaintingQueue = false;
                Undo.CollapseUndoOperations(queueUndoGroupAtStroke);
                EditorUtility.SetDirty(levelData);
                e.Use();
            }
        }

        private void ApplyQueueBrush(LevelData levelData, int row, int col)
        {
            lastPaintedQueueRow = row;
            lastPaintedQueueCol = col;

            if (currentQueueMode == QueuePaintMode.Erase)
            {
                levelData.RemoveQueueEntry(row, col);
                return;
            }

            FoodType food = currentQueueMode switch
            {
                QueuePaintMode.Hamburger => FoodType.Hamburger,
                QueuePaintMode.Fries => FoodType.Fries,
                QueuePaintMode.Drink => FoodType.Drink,
                QueuePaintMode.Sushi => FoodType.Sushi,
                QueuePaintMode.Steak => FoodType.Steak,
                QueuePaintMode.Donut => FoodType.Donut,
                _ => FoodType.Hamburger
            };

            levelData.SetQueueEntry(row, col, food, Mathf.Max(1, queuePaintCapacity), paintAsSurprise);
        }
    }
}
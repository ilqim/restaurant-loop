using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RestaurantLoop;
using UnityEditor;
using UnityEngine;

namespace RestaurantLoop.EditorTools
{
    /// <summary>
    /// JSON formatındaki level dosyalarını (level_1_unity.json gibi) gerçek
    /// LevelData ScriptableObject asset'ine dönüştürür.
    ///
    /// Eşleme (LevelData.cs, QueueSlot.cs vb. gerçek tanımlara göre doğrulandı):
    /// - cells: JSON'daki düz int (0/1/2) değerleri CellType enum'una DİREKT cast
    ///   ediliyor — CellType { Empty=0, Conveyor=1, CustomerSlot=2, BaseTray=3 }
    ///   sıralaması JSON'daki sayılarla birebir örtüşüyor.
    /// - customers/queue içindeki "food" string'i ("Hamburger", "Fries", "Drink")
    ///   FoodType enum isimleriyle birebir aynı, Enum.Parse ile direkt çevriliyor.
    /// - lastAppliedRows/lastAppliedColumns private+HideInInspector olduğu için
    ///   reflection ile set ediliyor (ResizeCells()'in ileride grid'i yanlışlıkla
    ///   sıfırlamaması için import edilen boyutla eşitleniyor).
    ///
    /// Kullanım: Bu dosyayı bir "Editor" klasörüne koy (ör. Assets/Editor/),
    /// sonra Unity'de üst menüden Tools > Restaurant Loop > Import Level From JSON.
    /// </summary>
    public static class LevelJsonImporter
    {
        [MenuItem("Tools/Restaurant Loop/Import Level From JSON")]
        public static void ImportLevelFromJson()
        {
            string jsonPath = EditorUtility.OpenFilePanel("Level JSON seç", Application.dataPath, "json");
            if (string.IsNullOrEmpty(jsonPath))
                return;

            string json = File.ReadAllText(jsonPath);
            LevelJson jsonData = JsonUtility.FromJson<LevelJson>(json);

            if (jsonData == null || jsonData.cells == null)
            {
                Debug.LogError("LevelJsonImporter: JSON parse edilemedi ya da 'cells' alanı boş.");
                return;
            }

            if (jsonData.cells.Length != jsonData.rows * jsonData.columns)
            {
                Debug.LogWarning($"LevelJsonImporter: cells uzunluğu ({jsonData.cells.Length}) rows*columns ({jsonData.rows * jsonData.columns}) ile eşleşmiyor, yine de devam ediliyor.");
            }

            string defaultName = Path.GetFileNameWithoutExtension(jsonPath);
            string savePath = EditorUtility.SaveFilePanelInProject(
                "LevelData olarak kaydet", defaultName, "asset", "Yeni LevelData asset'i nereye kaydedilsin?");
            if (string.IsNullOrEmpty(savePath))
                return;

            var level = ScriptableObject.CreateInstance<LevelData>();

            level.rows = jsonData.rows;
            level.columns = jsonData.columns;
            level.baseRow = jsonData.baseRow;
            level.baseCol = jsonData.baseCol;
            level.exitRow = jsonData.exitRow;
            level.exitCol = jsonData.exitCol;
            level.trayBaseRow = jsonData.trayBaseRow;
            level.trayBaseCol = jsonData.trayBaseCol;
            level.queueColumns = jsonData.queueColumns;

            level.cells = new CellType[jsonData.cells.Length];
            for (int i = 0; i < jsonData.cells.Length; i++)
                level.cells[i] = (CellType)jsonData.cells[i];

            level.customers = new List<CustomerEntry>();
            foreach (var c in jsonData.customers)
            {
                level.customers.Add(new CustomerEntry
                {
                    row = c.row,
                    col = c.col,
                    food = ParseFood(c.food)
                });
            }

            level.queue = new List<QueueEntry>();
            foreach (var q in jsonData.queue)
            {
                level.queue.Add(new QueueEntry
                {
                    row = q.row,
                    col = q.col,
                    food = ParseFood(q.food),
                    capacity = q.capacity
                });
            }

            SetPrivateInt(level, "lastAppliedRows", jsonData.rows);
            SetPrivateInt(level, "lastAppliedColumns", jsonData.columns);

            AssetDatabase.CreateAsset(level, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = level;

            Debug.Log($"LevelJsonImporter: '{jsonPath}' başarıyla '{savePath}' olarak içeri aktarıldı. " +
                      $"{level.customers.Count} müşteri, {level.queue.Count} queue hücresi.");
        }

        /// <summary>lastAppliedRows/lastAppliedColumns gibi private [SerializeField] alanları set eder.</summary>
        private static void SetPrivateInt(LevelData level, string fieldName, int value)
        {
            var field = typeof(LevelData).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                Debug.LogWarning($"LevelJsonImporter: LevelData içinde '{fieldName}' adında bir alan bulunamadı.");
            else
                field.SetValue(level, value);
        }

        private static FoodType ParseFood(string foodName)
        {
            if (Enum.TryParse(foodName, ignoreCase: true, out FoodType result))
                return result;

            Debug.LogWarning($"LevelJsonImporter: Bilinmeyen food adı '{foodName}' (FoodType enum'unda yok), varsayılan Hamburger kullanıldı.");
            return FoodType.Hamburger;
        }

        [Serializable]
        private class LevelJson
        {
            public int rows;
            public int columns;
            public int[] cells;
            public int baseRow;
            public int baseCol;
            public int exitRow;
            public int exitCol;
            public int trayBaseRow;
            public int trayBaseCol;
            public CustomerJson[] customers;
            public int queueColumns;
            public QueueJson[] queue;
        }

        [Serializable]
        private class CustomerJson
        {
            public int row;
            public int col;
            public string food;
        }

        [Serializable]
        private class QueueJson
        {
            public int row;
            public int col;
            public string food;
            public int capacity;
        }
    }
}
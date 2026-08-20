using System;
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public enum CellType { Empty, Conveyor, CustomerSlot }

    public enum FoodType { Hamburger, Fries, Drink, Sushi, Steak, Dessert }

    [CreateAssetMenu(fileName = "Level", menuName = "RestaurantLoop/LevelData")]
    public class LevelData : ScriptableObject
    {
        public const int ConveyorBlockSize = 1;

        [Header("Level Grid boyutu — level tasarımcısı buradan ayarlar")]
        public int rows = 8;
        public int columns = 8;

        [Header("Hücre içerikleri (row*columns + col index'iyle)")]
        public CellType[] cells = Array.Empty<CellType>();

        [SerializeField, HideInInspector] private int lastAppliedRows;
        [SerializeField, HideInInspector] private int lastAppliedColumns;

        [Header("Conveyor — Base (giriş)")]
        public int baseRow = -1;
        public int baseCol = -1;

        [Header("Conveyor — Exit (stack'in rafa/slota geçtiği nokta — loop üzerinde bir işaret)")]
        public int exitRow = -1;
        public int exitCol = -1;

        [Header("İçerik")]
        public List<CustomerEntry> customers = new();
        public List<QueueEntry> queue = new();

        public CellType GetCell(int row, int col)
        {
            int i = row * columns + col;
            return (i >= 0 && i < cells.Length) ? cells[i] : CellType.Empty;
        }

        public void SetCell(int row, int col, CellType type)
        {
            int i = row * columns + col;
            if (i >= 0 && i < cells.Length) cells[i] = type;
        }

        public bool IsCellInBaseBlock(int row, int col)
        {
            if (baseRow < 0 || baseCol < 0) return false;
            return row >= baseRow && row < baseRow + ConveyorBlockSize &&
                   col >= baseCol && col < baseCol + ConveyorBlockSize;
        }

        public bool IsCellInExitBlock(int row, int col)
        {
            if (exitRow < 0 || exitCol < 0) return false;
            return row >= exitRow && row < exitRow + ConveyorBlockSize &&
                   col >= exitCol && col < exitCol + ConveyorBlockSize;
        }

        public bool TryGetCustomerFood(int row, int col, out FoodType food)
        {
            foreach (var entry in customers)
            {
                if (entry.row == row && entry.col == col)
                {
                    food = entry.food;
                    return true;
                }
            }
            food = default;
            return false;
        }

        public void SetCustomerAt(int row, int col, FoodType food)
        {
            foreach (var entry in customers)
            {
                if (entry.row == row && entry.col == col)
                {
                    entry.food = food;
                    return;
                }
            }
            customers.Add(new CustomerEntry { row = row, col = col, food = food });
            SetCell(row, col, CellType.CustomerSlot);
        }

        public void RemoveCustomerAt(int row, int col)
        {
            customers.RemoveAll(e => e.row == row && e.col == col);
        }

        public void ResizeCells()
        {
            var oldCells = cells;
            int oldRows = lastAppliedRows;
            int oldColumns = lastAppliedColumns;

            var newCells = new CellType[rows * columns];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    CellType value = CellType.Empty;
                    if (r < oldRows && c < oldColumns && oldCells != null)
                    {
                        int oldIndex = r * oldColumns + c;
                        if (oldIndex < oldCells.Length) value = oldCells[oldIndex];
                    }
                    newCells[r * columns + c] = value;
                }
            }

            cells = newCells;
            lastAppliedRows = rows;
            lastAppliedColumns = columns;

            if (baseRow >= rows || baseCol >= columns) { baseRow = -1; baseCol = -1; }
            if (exitRow >= rows || exitCol >= columns) { exitRow = -1; exitCol = -1; }
            customers.RemoveAll(e => e.row >= rows || e.col >= columns);
        }
    }

    [Serializable]
    public class CustomerEntry
    {
        public int row;
        public int col;
        public FoodType food;
    }

    [Serializable]
    public class QueueEntry
    {
        public int position;
        public string foodType;
        public int capacity;
    }
}
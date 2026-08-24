using System;
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public enum CellType { Empty, Conveyor, CustomerSlot, BaseTray }

    public enum FoodType { Hamburger, Fries, Drink, Sushi, Steak, Donut }

    [CreateAssetMenu(fileName = "Level", menuName = "RestaurantLoop/LevelData")]
    public class LevelData : ScriptableObject
    {
        public const int ConveyorBlockSize = 2;
        public const int QueueColumnsMin = 3;
        public const int QueueColumnsMax = 5;

        [Header("Level Grid boyutu — level tasarımcısı buradan ayarlar")]
        public int rows = 8;
        public int columns = 8;

        [Header("Hücre içerikleri (row*columns + col index'iyle)")]
        public CellType[] cells = Array.Empty<CellType>();

        [SerializeField, HideInInspector] private int lastAppliedRows;
        [SerializeField, HideInInspector] private int lastAppliedColumns;

        [Header("Conveyor — Start (2x2 blok origin — ESKİDEN 'Base' diye adlandırılıyordu; " +
                "yemekler conveyor'a buradan girer. Alan adı kod içinde hâlâ baseRow/baseCol, " +
                "sadece Editor'de 'Start' olarak gösteriliyor — başka scriptleri kırmamak için.)")]
        public int baseRow = -1;
        public int baseCol = -1;

        [Header("Conveyor — Exit (2x2 blok origin)")]
        public int exitRow = -1;
        public int exitCol = -1;

        [Header("Tray Base — boş traylerin park ettiği/stackleneceği yer (2x2 blok origin). " +
                "Gameplay'i henüz implement edilmedi — sadece level tasarımında yer işaretleniyor.")]
        public int trayBaseRow = -1;
        public int trayBaseCol = -1;

        [Header("İçerik")]
        public List<CustomerEntry> customers = new();

        [Header("Food Stack Queue — üst (sütun) sayısı 3-5 arası, sonsuz derinlik")]
        [Range(QueueColumnsMin, QueueColumnsMax)]
        public int queueColumns = 4;
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

        public bool IsCellInTrayBaseBlock(int row, int col)
        {
            if (trayBaseRow < 0 || trayBaseCol < 0) return false;
            return row >= trayBaseRow && row < trayBaseRow + ConveyorBlockSize &&
                   col >= trayBaseCol && col < trayBaseCol + ConveyorBlockSize;
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

        // ---- Queue helper'ları — level grid'den TAMAMEN bağımsız, kendi (row,col) uzayı ----

        public bool TryGetQueueEntry(int row, int col, out QueueEntry entry)
        {
            foreach (var e in queue)
            {
                if (e.row == row && e.col == col)
                {
                    entry = e;
                    return true;
                }
            }
            entry = null;
            return false;
        }

        public void SetQueueEntry(int row, int col, FoodType food, int capacity)
        {
            foreach (var e in queue)
            {
                if (e.row == row && e.col == col)
                {
                    e.food = food;
                    e.capacity = capacity;
                    return;
                }
            }
            queue.Add(new QueueEntry { row = row, col = col, food = food, capacity = capacity });
        }

        public void RemoveQueueEntry(int row, int col)
        {
            queue.RemoveAll(e => e.row == row && e.col == col);
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
            if (trayBaseRow >= rows || trayBaseCol >= columns) { trayBaseRow = -1; trayBaseCol = -1; }
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
        public int row;
        public int col;
        public FoodType food;
        public int capacity;
    }
}
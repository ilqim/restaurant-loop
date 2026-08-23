using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RestaurantLoop
{
    public class Tray : MonoBehaviour
    {
        [Header("Kapasite Debug Etiketi")]
        [SerializeField] private bool showCapacityLabel = true;
        [SerializeField] private float labelMarginAboveStack = 0.4f;
        [SerializeField] private int labelFontSize = 48;
        [SerializeField] private float labelCharacterSize = 0.12f;
        [SerializeField] private Color labelColor = Color.white;

        [Header("Yönelim")]
        [SerializeField] private float rotationSmoothing = 10f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private static readonly HashSet<Customer> reservedCustomers = new();
        private readonly HashSet<Customer> customersReservedByThisTray = new();

        private class StackPieceInfo
        {
            public GameObject go;
            public int layerIndex;

            // Parçanın tepsiye göre LOCAL X/Z konumu.
            public Vector2 offsetXZ;
        }

        private const int PiecesPerLayer = 4;

        private readonly List<StackPieceInfo> stackPieceInfos = new();

        private int currentLayerCount;

        private TrayManager trayManager;
        private FoodType foodType;
        private int capacity;
        private TrayVisualConfig config;

        private int currentIndex;
        private Coroutine moveRoutine;
        private int deliveryTryCounter;
        private bool depleted;

        private List<(float t, Vector2Int cell)> deliveryCheckpoints;
        private int nextCheckpointIndex;

        private float[] cumulativeMovementDistance;
        private float totalMovementLength;

        private TextMesh capacityLabel;
        private Transform capacityLabelTransform;
        private Camera labelFacingCamera;


        // ============================================================
        // INIT
        // ============================================================

        public void Init(TrayManager manager, FoodType type, int startCapacity)
        {
            trayManager = manager;
            foodType = type;
            capacity = startCapacity;
            config = trayManager.GetVisualConfig(foodType);

            currentIndex = 0;
            deliveryTryCounter = 0;
            depleted = false;

            customersReservedByThisTray.Clear();

            var gridManager = trayManager.GridManagerRef;
            var waypoints = gridManager.WaypointWorldPositions;

            if (waypoints == null || waypoints.Count == 0)
            {
                Debug.LogWarning(
                    $"Tray [{gameObject.name}] waypoint bulunamadı."
                );
                return;
            }

            transform.position = trayManager.GetWaypointPosition(0);

            var facings = gridManager.WaypointFacingDirections;

            if (facings != null &&
                facings.Count > 0 &&
                facings[0].sqrMagnitude > 0.0001f)
            {
                transform.rotation =
                    Quaternion.LookRotation(
                        facings[0],
                        Vector3.up
                    );
            }

            cumulativeMovementDistance =
                new float[waypoints.Count];

            cumulativeMovementDistance[0] = 0f;

            for (int i = 1; i < waypoints.Count; i++)
            {
                cumulativeMovementDistance[i] =
                    cumulativeMovementDistance[i - 1] +
                    Vector3.Distance(
                        waypoints[i - 1],
                        waypoints[i]
                    );
            }

            totalMovementLength =
                waypoints.Count > 0
                    ? cumulativeMovementDistance[^1]
                    : 0f;

            deliveryCheckpoints =
                gridManager.DeliveryCheckpoints;

            nextCheckpointIndex = 1;

            BuildStackVisuals();

            CreateCapacityLabel();
            UpdateCapacityLabel();

            if (deliveryCheckpoints.Count > 0)
            {
                TryDeliverAtCell(
                    deliveryCheckpoints[0].cell
                );
            }

            if (!depleted)
            {
                moveRoutine =
                    StartCoroutine(
                        MoveOnConveyor()
                    );
            }
        }


        private void OnDisable()
        {
            ReleaseAllCustomerReservations();

            trayManager?.ReleaseTraySlot();
        }


        private void LateUpdate()
        {
            if (capacityLabel == null)
                return;

            if (labelFacingCamera == null)
                labelFacingCamera = Camera.main;

            if (labelFacingCamera == null)
                return;

            capacityLabel.transform.rotation =
                Quaternion.LookRotation(
                    capacityLabel.transform.position -
                    labelFacingCamera.transform.position
                );
        }


        // ============================================================
        // STACK GÖRSELİ
        // ============================================================

        private void BuildStackVisuals()
        {
            ClearStackVisuals();

            if (config.stackPiecePrefab == null)
            {
                currentLayerCount = 0;
                PositionLabelAboveStack();
                return;
            }

            int count =
                Mathf.Min(
                    capacity,
                    Mathf.Max(
                        0,
                        config.maxVisualPieces
                    )
                );

            currentLayerCount =
                Mathf.CeilToInt(
                    count / (float)PiecesPerLayer
                );

            for (int i = 0; i < count; i++)
            {
                SpawnStackPiece(i);
            }

            PositionLabelAboveStack();
        }


        private void SpawnStackPiece(int index)
        {
            int layer =
                index / PiecesPerLayer;

            int posInLayer =
                index % PiecesPerLayer;

            float half =
                config.pieceSpacing * 0.5f;

            /*
             * Layer içindeki dizilim:
             *
             *       +Z
             *
             *       0   1
             *
             *       2   3
             *
             *       -Z
             *
             * X:
             *       -     +
             *
             * Z:
             *       +     +
             *       -     -
             */

            float xOffset =
                (posInLayer == 0 ||
                 posInLayer == 2)
                    ? -half
                    : half;

            float zOffset =
                (posInLayer == 0 ||
                 posInLayer == 1)
                    ? half
                    : -half;

            GameObject piece =
                ObjectPool.Instance != null
                    ? ObjectPool.Instance.Get(
                        config.stackPiecePrefab,
                        transform.position,
                        config.stackPiecePrefab.transform.rotation,
                        transform
                    )
                    : Instantiate(
                        config.stackPiecePrefab,
                        transform.position,
                        config.stackPiecePrefab.transform.rotation,
                        transform
                    );

            float yOffset =
                config.foodBaseYOffset +
                layer * config.pieceHeightSpacing;

            piece.transform.localPosition =
                new Vector3(
                    xOffset,
                    yOffset,
                    zOffset
                );

            stackPieceInfos.Add(
                new StackPieceInfo
                {
                    go = piece,
                    layerIndex = layer,
                    offsetXZ = new Vector2(
                        xOffset,
                        zOffset
                    )
                }
            );
        }


        // ============================================================
        // YENİ PARÇA SEÇME SİSTEMİ
        // ============================================================

        /// <summary>
        /// Müşteriye gönderilecek yemek parçasını seçer.
        ///
        /// KURAL:
        ///
        /// Müşteri yukarıdaysa:
        ///
        ///      MÜŞTERİ
        ///         ↓
        ///      0   1    ← ÖNCE
        ///      2   3    ← SONRA
        ///
        /// Müşteri aşağıdaysa:
        ///
        ///      0   1
        ///      2   3    ← ÖNCE
        ///         ↑
        ///      MÜŞTERİ
        ///
        /// Müşteri sağdaysa:
        ///
        ///      0 | 1    ← ÖNCE
        ///      2 | 3
        ///        ↑
        ///
        /// Müşteri soldaysa:
        ///
        ///      0 | 1
        ///      2 | 3
        ///      ↑
        ///
        /// Yani seçim, parçanın müşteriye olan yönüne göre
        /// yapılır. Müşteriye yakın grup tamamen bitmeden
        /// uzak gruba geçilmez.
        /// </summary>
        private void RemoveStackPieceTowardCustomer(
            Vector3 dirToCustomerWorld
        )
        {
            if (stackPieceInfos.Count == 0)
                return;


            // --------------------------------------------------------
            // 1. Önce hangi layer'dan yiyecek çıkaracağımızı belirle.
            // --------------------------------------------------------

            int targetLayer;

            if (config.removeFromTopFirst)
            {
                targetLayer =
                    stackPieceInfos.Max(
                        p => p.layerIndex
                    );
            }
            else
            {
                targetLayer =
                    stackPieceInfos.Min(
                        p => p.layerIndex
                    );
            }


            // Sadece aktif layer'daki parçalarla ilgileniyoruz.

            List<StackPieceInfo> layerPieces =
                stackPieceInfos
                    .Where(
                        p => p.layerIndex == targetLayer
                    )
                    .ToList();

            if (layerPieces.Count == 0)
                return;


            // --------------------------------------------------------
            // 2. Müşterinin yönünü WORLD -> LOCAL çevir.
            // --------------------------------------------------------

            Vector3 localDir =
                transform.InverseTransformDirection(
                    dirToCustomerWorld
                );

            localDir.y = 0f;

            if (localDir.sqrMagnitude < 0.0001f)
            {
                localDir = Vector3.forward;
            }

            localDir.Normalize();


            Vector2 customerDirection =
                new Vector2(
                    localDir.x,
                    localDir.z
                );


            // --------------------------------------------------------
            // 3. Her parçanın müşteriye yakınlık skorunu hesapla.
            // --------------------------------------------------------
            //
            // Dot product sayesinde:
            //
            // Müşteriye bakan parçalar -> yüksek skor
            // Müşteriden uzak parçalar -> düşük skor
            //
            // Örneğin müşteri +Z tarafındaysa:
            //
            // 0 = +Z
            // 1 = +Z
            // 2 = -Z
            // 3 = -Z
            //
            // Sonuç:
            //
            // 0 / 1 önce
            // 2 / 3 sonra
            // --------------------------------------------------------

            StackPieceInfo chosen = null;

            float bestScore =
                float.NegativeInfinity;


            foreach (StackPieceInfo piece in layerPieces)
            {
                Vector2 piecePosition =
                    piece.offsetXZ;

                float score =
                    Vector2.Dot(
                        piecePosition,
                        customerDirection
                    );


                // Aynı mesafedeki parçalar için
                // deterministik bir seçim yap.
                //
                // Böylece her frame rastgele değişmez.

                if (chosen == null ||
                    score > bestScore)
                {
                    chosen = piece;
                    bestScore = score;
                }
                else if (
                    Mathf.Approximately(
                        score,
                        bestScore
                    ))
                {
                    // Eşit uzaklıktaki iki parçada
                    // önce küçük X, sonra küçük Z.

                    if (piece.offsetXZ.x <
                        chosen.offsetXZ.x)
                    {
                        chosen = piece;
                    }
                    else if (
                        Mathf.Approximately(
                            piece.offsetXZ.x,
                            chosen.offsetXZ.x
                        ) &&
                        piece.offsetXZ.y <
                        chosen.offsetXZ.y)
                    {
                        chosen = piece;
                    }
                }
            }


            if (chosen == null)
                return;


            // --------------------------------------------------------
            // 4. Parçayı listeden çıkar.
            // --------------------------------------------------------

            stackPieceInfos.Remove(chosen);


            // --------------------------------------------------------
            // 5. Görsel parçayı pool'a geri gönder.
            // --------------------------------------------------------

            if (chosen.go != null)
            {
                if (ObjectPool.Instance != null)
                {
                    ObjectPool.Instance.Return(
                        chosen.go
                    );
                }
                else
                {
                    Destroy(chosen.go);
                }
            }


            // --------------------------------------------------------
            // 6. Stack yüksekliğini güncelle.
            // --------------------------------------------------------

            currentLayerCount =
                stackPieceInfos.Count > 0
                    ? stackPieceInfos.Max(
                        p => p.layerIndex
                    ) + 1
                    : 0;

            PositionLabelAboveStack();


            if (verboseLogging)
            {
                Debug.Log(
                    $"Tray [{gameObject.name}] " +
                    $"stack piece removed. " +
                    $"Layer={targetLayer}, " +
                    $"LocalOffset={chosen.offsetXZ}, " +
                    $"CustomerDir={customerDirection}"
                );
            }
        }


        private void ClearStackVisuals()
        {
            foreach (var info in stackPieceInfos)
            {
                if (info.go == null)
                    continue;

                if (ObjectPool.Instance != null)
                {
                    ObjectPool.Instance.Return(
                        info.go
                    );
                }
                else
                {
                    Destroy(info.go);
                }
            }

            stackPieceInfos.Clear();
            currentLayerCount = 0;
        }


        // ============================================================
        // HAREKET
        // ============================================================

        private IEnumerator MoveOnConveyor()
        {
            var gridManager =
                trayManager.GridManagerRef;

            var waypoints =
                gridManager.WaypointWorldPositions;

            var facings =
                gridManager.WaypointFacingDirections;

            while (true)
            {
                int nextIndex =
                    currentIndex + 1;

                bool reachedExitEnd =
                    nextIndex >= waypoints.Count;


                if (reachedExitEnd)
                {
                    AdvanceDeliveryCheckpoints(1f);

                    if (depleted)
                    {
                        moveRoutine = null;
                        yield break;
                    }


                    if (capacity > 0)
                    {
                        if (TryMergeIntoSlot())
                        {
                            moveRoutine = null;
                            Despawn();
                            yield break;
                        }


                        if (trayManager.LoopIfSlotsFull)
                        {
                            nextIndex = 0;
                        }
                        else
                        {
                            if (verboseLogging)
                            {
                                Debug.Log(
                                    $"Tray [{gameObject.name}] " +
                                    $"Exit'te boş slot yok, parkediyor."
                                );
                            }

                            moveRoutine = null;
                            yield break;
                        }
                    }
                    else
                    {
                        moveRoutine = null;
                        Despawn();
                        yield break;
                    }
                }


                Vector3 targetPosition =
                    trayManager.GetWaypointPosition(
                        nextIndex
                    );

                Vector3 targetFacing =
                    nextIndex < facings.Count
                        ? facings[nextIndex]
                        : Vector3.zero;


                yield return StartCoroutine(
                    MoveTo(
                        currentIndex,
                        targetPosition,
                        targetFacing
                    )
                );


                currentIndex = nextIndex;


                if (depleted)
                {
                    moveRoutine = null;
                    yield break;
                }
            }
        }


        private IEnumerator MoveTo(
            int fromIndex,
            Vector3 target,
            Vector3 targetFacing
        )
        {
            Vector3 start =
                transform.position;

            float distance =
                Vector3.Distance(
                    start,
                    target
                );

            float speed =
                Mathf.Max(
                    0.01f,
                    config.conveyorSpeed
                );

            float duration =
                Mathf.Max(
                    0.01f,
                    distance / speed
                );


            float prefixDistance =
                cumulativeMovementDistance != null &&
                fromIndex <
                cumulativeMovementDistance.Length
                    ? cumulativeMovementDistance[fromIndex]
                    : 0f;


            Quaternion targetRotation =
                targetFacing.sqrMagnitude >
                0.0001f
                    ? Quaternion.LookRotation(
                        targetFacing,
                        Vector3.up
                    )
                    : transform.rotation;


            float elapsed = 0f;


            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed / duration
                    );


                transform.position =
                    Vector3.Lerp(
                        start,
                        target,
                        t
                    );


                transform.rotation =
                    rotationSmoothing > 0f
                        ? Quaternion.Slerp(
                            transform.rotation,
                            targetRotation,
                            Time.deltaTime *
                            rotationSmoothing
                        )
                        : targetRotation;


                if (totalMovementLength >
                    0.0001f)
                {
                    float globalT =
                        (
                            prefixDistance +
                            distance * t
                        ) /
                        totalMovementLength;


                    AdvanceDeliveryCheckpoints(
                        globalT
                    );


                    if (depleted)
                        yield break;
                }


                yield return null;
            }


            transform.position = target;
            transform.rotation = targetRotation;
        }


        private void AdvanceDeliveryCheckpoints(
            float globalT
        )
        {
            if (deliveryCheckpoints == null)
                return;


            while (
                nextCheckpointIndex <
                deliveryCheckpoints.Count &&
                deliveryCheckpoints[
                    nextCheckpointIndex
                ].t <= globalT
            )
            {
                var checkpoint =
                    deliveryCheckpoints[
                        nextCheckpointIndex
                    ];

                nextCheckpointIndex++;

                TryDeliverAtCell(
                    checkpoint.cell
                );


                if (depleted)
                    return;
            }
        }


        // ============================================================
        // TESLİMAT
        // ============================================================

        private void TryDeliverAtCell(
            Vector2Int cell
        )
        {
            var customerManager =
                trayManager.CustomerManagerRef;

            if (customerManager == null)
                return;

            if (capacity <= 0)
                return;


            deliveryTryCounter++;


            if (verboseLogging)
            {
                Debug.Log(
                    $"Tray [{gameObject.name}] " +
                    $"delivery try {deliveryTryCounter}, " +
                    $"cell ({cell.x},{cell.y})"
                );
            }


            if (!customerManager.TryFindDeliverableCustomer(
                    foodType,
                    cell,
                    1,
                    out Customer target))
            {
                return;
            }


            if (target == null)
                return;


            if (IsCustomerReservedByAnotherTray(target))
                return;


            ReserveCustomer(target);


            capacity =
                Mathf.Max(
                    0,
                    capacity - 1
                );


            Vector3 dirToCustomer =
                target.transform.position -
                transform.position;


            RemoveStackPieceTowardCustomer(
                dirToCustomer
            );


            UpdateCapacityLabel();


            LaunchDeliveryClone(
                target,
                transform.position
            );


            if (capacity <= 0 &&
                !depleted)
            {
                depleted = true;


                if (moveRoutine != null)
                {
                    StopCoroutine(
                        moveRoutine
                    );

                    moveRoutine = null;
                }


                Despawn();
            }
        }


        private void LaunchDeliveryClone(
            Customer target,
            Vector3 launchPosition
        )
        {
            if (config.stackPiecePrefab == null)
            {
                if (target != null)
                    target.ReceiveFood();

                ReleaseCustomerReservation(
                    target
                );

                return;
            }


            if (ObjectPool.Instance == null)
            {
                if (target != null)
                    target.ReceiveFood();

                ReleaseCustomerReservation(
                    target
                );

                return;
            }


            ObjectPool.Instance.StartCoroutine(
                DeliverCloneRoutine(
                    config.stackPiecePrefab,
                    launchPosition,
                    transform.rotation,
                    target,
                    config.deliverySpeed
                )
            );
        }


        private static IEnumerator DeliverCloneRoutine(
            GameObject prefab,
            Vector3 launchPosition,
            Quaternion launchRotation,
            Customer target,
            float speed
        )
        {
            GameObject clone =
                ObjectPool.Instance.Get(
                    prefab,
                    launchPosition,
                    launchRotation
                );


            if (clone == null)
            {
                if (target != null)
                    target.ReceiveFood();

                yield break;
            }


            Vector3 targetPos =
                target != null
                    ? target.transform.position
                    : launchPosition;


            float distance =
                Vector3.Distance(
                    launchPosition,
                    targetPos
                );


            float duration =
                Mathf.Max(
                    0.01f,
                    distance /
                    Mathf.Max(
                        0.01f,
                        speed
                    )
                );


            float elapsed = 0f;


            while (elapsed < duration)
            {
                if (clone == null)
                    yield break;


                elapsed += Time.deltaTime;


                float t =
                    Mathf.Clamp01(
                        elapsed / duration
                    );


                clone.transform.position =
                    Vector3.Lerp(
                        launchPosition,
                        targetPos,
                        t
                    );


                yield return null;
            }


            if (clone != null)
            {
                clone.transform.position =
                    targetPos;

                ObjectPool.Instance.Return(
                    clone
                );
            }


            if (target != null)
                target.ReceiveFood();
        }


        // ============================================================
        // CUSTOMER RESERVATION
        // ============================================================

        private bool IsCustomerReservedByAnotherTray(
            Customer customer
        )
        {
            if (customer == null)
                return false;

            if (customersReservedByThisTray.Contains(
                    customer))
            {
                return true;
            }

            return reservedCustomers.Contains(
                customer
            );
        }


        private void ReserveCustomer(
            Customer customer
        )
        {
            if (customer == null)
                return;

            reservedCustomers.Add(
                customer
            );

            customersReservedByThisTray.Add(
                customer
            );
        }


        private void ReleaseCustomerReservation(
            Customer customer
        )
        {
            if (customer == null)
                return;

            customersReservedByThisTray.Remove(
                customer
            );

            reservedCustomers.Remove(
                customer
            );
        }


        private void ReleaseAllCustomerReservations()
        {
            if (customersReservedByThisTray.Count == 0)
                return;


            foreach (
                var customer
                in customersReservedByThisTray)
            {
                if (customer != null)
                    reservedCustomers.Remove(
                        customer
                    );
            }


            customersReservedByThisTray.Clear();
        }


        // ============================================================
        // MERGE
        // ============================================================

        private bool TryMergeIntoSlot()
        {
            GameObject prefab =
                trayManager.GetFoodPrefab(
                    foodType
                );


            if (prefab == null)
            {
                Debug.LogWarning(
                    $"Tray: '{foodType}' " +
                    $"için merge-back Food prefabı yok."
                );

                return false;
            }


            GameObject foodGo =
                Instantiate(
                    prefab,
                    transform.position,
                    prefab.transform.rotation
                );


            Food food =
                foodGo.GetComponent<Food>();


            if (food == null)
            {
                Destroy(foodGo);
                return false;
            }


            food.PresetCapacity(
                capacity
            );


            bool placed =
                trayManager.SlotManagerRef != null &&
                trayManager.SlotManagerRef.TryPlaceFood(
                    food
                );


            if (!placed)
            {
                Destroy(foodGo);
                return false;
            }


            capacity = 0;

            return true;
        }


        // ============================================================
        // DESPAWN
        // ============================================================

        private void Despawn()
        {
            if (verboseLogging)
            {
                Debug.Log(
                    $"Tray [{gameObject.name}] despawn."
                );
            }


            ClearStackVisuals();


            if (ObjectPool.Instance != null)
            {
                ObjectPool.Instance.Return(
                    gameObject
                );
            }
            else
            {
                gameObject.SetActive(false);
            }
        }


        // ============================================================
        // CAPACITY LABEL
        // ============================================================

        private void CreateCapacityLabel()
        {
            if (!showCapacityLabel)
                return;


            if (capacityLabel != null)
            {
                UpdateCapacityLabel();
                PositionLabelAboveStack();
                return;
            }


            GameObject labelGO =
                new GameObject(
                    "CapacityLabel"
                );


            labelGO.transform.SetParent(
                transform,
                false
            );


            capacityLabelTransform =
                labelGO.transform;


            capacityLabel =
                labelGO.AddComponent<TextMesh>();


            capacityLabel.anchor =
                TextAnchor.MiddleCenter;

            capacityLabel.alignment =
                TextAlignment.Center;

            capacityLabel.fontSize =
                labelFontSize;

            capacityLabel.characterSize =
                labelCharacterSize;

            capacityLabel.color =
                labelColor;


            PositionLabelAboveStack();
        }


        private void PositionLabelAboveStack()
        {
            if (capacityLabelTransform == null)
                return;


            float stackTopHeight =
                config.foodBaseYOffset +
                Mathf.Max(
                    0,
                    currentLayerCount - 1
                ) *
                config.pieceHeightSpacing;


            capacityLabelTransform.localPosition =
                new Vector3(
                    0,
                    stackTopHeight +
                    labelMarginAboveStack,
                    0
                );
        }


        private void UpdateCapacityLabel()
        {
            if (capacityLabel != null)
            {
                capacityLabel.text =
                    capacity.ToString();
            }
        }
    }
}
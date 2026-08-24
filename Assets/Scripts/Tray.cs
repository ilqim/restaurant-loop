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
        [SerializeField] private float rotationSmoothing = 15f;

        private readonly HashSet<Customer> customersReservedByThisTray = new();
        private readonly List<Vector2Int> pendingCheckCells = new();

        private class StackPieceInfo
        {
            public GameObject go;
            public int layerIndex;
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

        public void ParkAtBase(TrayManager manager, Vector3 pos)
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            trayManager = manager;
            transform.position = pos;
            transform.rotation = manager != null
                ? manager.BaseStackRotation
                : Quaternion.identity;

            ClearStackVisuals();

            if (capacityLabel != null)
                capacityLabel.text = "";

            enabled = false;
        }

        public void Init(
            TrayManager manager,
            FoodType type,
            int startCapacity)
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            enabled = true;
            trayManager = manager;
            foodType = type;
            capacity = startCapacity;

            config = trayManager.GetVisualConfig(foodType);

            currentIndex = 0;
            deliveryTryCounter = 0;
            depleted = false;

            customersReservedByThisTray.Clear();
            pendingCheckCells.Clear();

            TrayDeliveryQueue.Register(this, foodType);

            var gridManager = trayManager.GridManagerRef;
            var waypoints = gridManager.WaypointWorldPositions;

            if (waypoints == null || waypoints.Count == 0)
                return;

            Vector3 startPos = trayManager.GetWaypointPosition(0);
            transform.position = startPos;

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

            cumulativeMovementDistance = new float[waypoints.Count];
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

            deliveryCheckpoints = gridManager.DeliveryCheckpoints;
            nextCheckpointIndex = 1;

            BuildStackVisuals();
            CreateCapacityLabel();
            UpdateCapacityLabel();

            if (deliveryCheckpoints != null &&
                deliveryCheckpoints.Count > 0)
            {
                QueueDeliveryCheck(
                    deliveryCheckpoints[0].cell
                );
            }

            moveRoutine = StartCoroutine(
                MoveOnConveyor()
            );
        }

        private void OnDisable()
        {
            ReleaseAllCustomerReservations();
            pendingCheckCells.Clear();

            TrayDeliveryQueue.Unregister(
                this,
                foodType
            );

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
        }

        private void LateUpdate()
        {
            if (capacityLabel == null)
                return;

            if (labelFacingCamera == null)
                labelFacingCamera = Camera.main;

            if (labelFacingCamera != null)
            {
                capacityLabel.transform.rotation =
                    Quaternion.LookRotation(
                        capacityLabel.transform.position -
                        labelFacingCamera.transform.position
                    );
            }
        }

        public void ProcessCheckedDeliveryPlans()
        {
            if (pendingCheckCells.Count == 0)
                return;

            var cellsSnapshot =
                new List<Vector2Int>(
                    pendingCheckCells
                );

            pendingCheckCells.Clear();

            var customerManager =
                trayManager != null
                    ? trayManager.CustomerManagerRef
                    : null;

            if (customerManager == null)
                return;

            foreach (Vector2Int cell in cellsSnapshot)
            {
                if (depleted || capacity <= 0)
                    break;

                deliveryTryCounter++;

                if (!customerManager.TryFindDeliverableCustomer(
                        foodType,
                        cell,
                        1,
                        out Customer target)
                    || target == null)
                {
                    continue;
                }

                if (!target.TryReserveForDelivery(
                        this,
                        foodType))
                {
                    continue;
                }

                customersReservedByThisTray.Add(target);
                FireDeliveryAt(target);
            }
        }

        private void FireDeliveryAt(Customer target)
        {
            capacity = Mathf.Max(
                0,
                capacity - 1
            );

            RemoveStackPieceTowardCustomer(
                transform.forward
            );

            UpdateCapacityLabel();

            LaunchDeliveryClone(
                target,
                transform.position
            );

            customersReservedByThisTray.Remove(target);

            if (capacity <= 0 && !depleted)
            {
                depleted = true;

                if (moveRoutine != null)
                {
                    StopCoroutine(moveRoutine);
                    moveRoutine = null;
                }

                Despawn();
            }
        }

        private void BuildStackVisuals()
        {
            ClearStackVisuals();

            if (config.stackPiecePrefab == null)
            {
                currentLayerCount = 0;
                PositionLabelAboveStack();
                return;
            }

            int count = Mathf.Min(
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
            int layer = index / PiecesPerLayer;
            int posInLayer = index % PiecesPerLayer;

            float half =
                config.pieceSpacing * 0.5f;

            float xOffset =
                (posInLayer == 0 || posInLayer == 2)
                    ? -half
                    : half;

            float zOffset =
                (posInLayer == 0 || posInLayer == 1)
                    ? half
                    : -half;

            GameObject piece =
                ObjectPool.Instance != null
                    ? ObjectPool.Instance.Get(
                        config.stackPiecePrefab,
                        transform.position,
                        config.stackPiecePrefab.transform.rotation,
                        transform)
                    : Instantiate(
                        config.stackPiecePrefab,
                        transform.position,
                        config.stackPiecePrefab.transform.rotation,
                        transform);

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

        private void RemoveStackPieceTowardCustomer(
            Vector3 dirToCustomerWorld)
        {
            if (stackPieceInfos.Count == 0)
                return;

            int targetLayer =
                config.removeFromTopFirst
                    ? stackPieceInfos.Max(
                        p => p.layerIndex)
                    : stackPieceInfos.Min(
                        p => p.layerIndex);

            List<StackPieceInfo> layerPieces =
                stackPieceInfos
                    .Where(
                        p => p.layerIndex == targetLayer)
                    .ToList();

            if (layerPieces.Count == 0)
                return;

            Vector3 localDir =
                transform.InverseTransformDirection(
                    dirToCustomerWorld
                );

            localDir.y = 0f;

            if (localDir.sqrMagnitude < 0.0001f)
                localDir = Vector3.forward;

            localDir.Normalize();

            Vector2 customerDirection =
                new Vector2(
                    localDir.x,
                    localDir.z
                );

            StackPieceInfo chosen = null;
            float bestScore =
                float.NegativeInfinity;

            foreach (StackPieceInfo piece in layerPieces)
            {
                float score =
                    Vector2.Dot(
                        piece.offsetXZ,
                        customerDirection
                    );

                if (chosen == null ||
                    score > bestScore)
                {
                    chosen = piece;
                    bestScore = score;
                }
            }

            if (chosen == null)
                return;

            stackPieceInfos.Remove(chosen);

            if (chosen.go != null)
            {
                if (ObjectPool.Instance != null)
                    ObjectPool.Instance.Return(chosen.go);
                else
                    Destroy(chosen.go);
            }

            currentLayerCount =
                stackPieceInfos.Count > 0
                    ? stackPieceInfos.Max(
                        p => p.layerIndex) + 1
                    : 0;

            PositionLabelAboveStack();
        }

        private void ClearStackVisuals()
        {
            foreach (var info in stackPieceInfos)
            {
                if (info.go == null)
                    continue;

                if (ObjectPool.Instance != null)
                    ObjectPool.Instance.Return(info.go);
                else
                    Destroy(info.go);
            }

            stackPieceInfos.Clear();
            currentLayerCount = 0;
        }

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

                if (nextIndex >= waypoints.Count)
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
                            moveRoutine = null;
                            Despawn();
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

                Vector3 startPos =
                    trayManager.GetWaypointPosition(
                        currentIndex
                    );

                Vector3 targetPosition =
                    trayManager.GetWaypointPosition(
                        nextIndex
                    );

                Vector3 targetFacing =
                    nextIndex < facings.Count
                        ? facings[nextIndex]
                        : Vector3.zero;

                yield return StartCoroutine(
                    MoveSegment(
                        currentIndex,
                        startPos,
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

        private IEnumerator MoveSegment(
            int fromIndex,
            Vector3 start,
            Vector3 target,
            Vector3 targetFacing)
        {
            float distance =
                Vector3.Distance(
                    start,
                    target
                );

            if (distance < 0.001f)
            {
                transform.position = target;
                yield break;
            }

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
                fromIndex < cumulativeMovementDistance.Length
                    ? cumulativeMovementDistance[fromIndex]
                    : 0f;

            Quaternion targetRotation =
                targetFacing.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(
                        targetFacing,
                        Vector3.up)
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
                            rotationSmoothing)
                        : targetRotation;

                if (totalMovementLength > 0.0001f)
                {
                    float globalT =
                        (prefixDistance +
                         distance * t) /
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
            float globalT)
        {
            if (deliveryCheckpoints == null)
                return;

            while (
                nextCheckpointIndex <
                    deliveryCheckpoints.Count &&
                deliveryCheckpoints[
                    nextCheckpointIndex].t <= globalT)
            {
                var checkpoint =
                    deliveryCheckpoints[
                        nextCheckpointIndex];

                nextCheckpointIndex++;

                QueueDeliveryCheck(
                    checkpoint.cell
                );

                if (depleted)
                    return;
            }
        }

        private void QueueDeliveryCheck(
            Vector2Int cell)
        {
            if (capacity <= 0 || depleted)
                return;

            pendingCheckCells.Add(cell);
        }

        private void LaunchDeliveryClone(
            Customer target,
            Vector3 launchPosition)
        {
            if (config.stackPiecePrefab == null ||
                ObjectPool.Instance == null)
            {
                if (target != null)
                {
                    target.ReceiveFood();
                    customersReservedByThisTray.Remove(target);
                }

                return;
            }

            ObjectPool.Instance.StartCoroutine(
                DeliverCloneRoutine(
                    this,
                    config.stackPiecePrefab,
                    launchPosition,
                    transform.rotation,
                    target,
                    config.deliverySpeed,
                    config.deliverySpinSpeed,
                    config.deliverySpinAxis
                )
            );
        }

        private static IEnumerator DeliverCloneRoutine(
            Tray sourceTray,
            GameObject prefab,
            Vector3 launchPosition,
            Quaternion launchRotation,
            Customer target,
            float speed,
            float spinSpeed,
            Vector3 spinAxis)
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
                {
                    target.ReceiveFood();
                    sourceTray?.customersReservedByThisTray.Remove(
                        target
                    );
                }

                yield break;
            }

            // Prefabdaki Trail Renderer normalde kapalı.
            // Fırlatma sırasında aç.
            TrailRenderer trail =
                clone.GetComponentInChildren<TrailRenderer>(true);

            if (trail != null)
            {
                trail.Clear();
                trail.enabled = true;
                trail.emitting = true;
            }

            Vector3 spinAxisNormalized =
                spinAxis.sqrMagnitude > 0.0001f
                    ? spinAxis.normalized
                    : Vector3.up;

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
                        speed)
                );

            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (clone == null)
                {
                    if (target != null)
                    {
                        target.ReceiveFood();
                        sourceTray?.customersReservedByThisTray.Remove(
                            target
                        );
                    }

                    yield break;
                }

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

                if (spinSpeed != 0f)
                {
                    clone.transform.Rotate(
                        spinAxisNormalized,
                        spinSpeed * Time.deltaTime,
                        Space.Self
                    );
                }

                yield return null;
            }

            if (clone != null)
            {
                clone.transform.position = targetPos;

                if (trail != null)
                {
                    trail.emitting = false;
                    trail.enabled = false;
                    trail.Clear();
                }

                ObjectPool.Instance.Return(clone);
            }

            if (target != null)
            {
                target.ReceiveFood();
                sourceTray?.customersReservedByThisTray.Remove(
                    target
                );
            }
        }

        private void ReleaseAllCustomerReservations()
        {
            if (customersReservedByThisTray.Count == 0)
                return;

            foreach (var customer in customersReservedByThisTray)
            {
                if (customer != null)
                    customer.ReleaseDeliveryReservation(
                        this
                    );
            }

            customersReservedByThisTray.Clear();
        }

        private bool TryMergeIntoSlot()
        {
            GameObject prefab =
                trayManager.GetFoodPrefab(
                    foodType
                );

            if (prefab == null)
                return false;

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

            food.PresetCapacity(capacity);

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

        private void Despawn()
        {
            ReleaseAllCustomerReservations();
            pendingCheckCells.Clear();

            TrayDeliveryQueue.Unregister(
                this,
                foodType
            );

            if (trayManager != null)
                trayManager.ReturnTrayToBase(this);
            else
                gameObject.SetActive(false);
        }

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
                capacityLabel.text =
                    capacity > 0
                        ? capacity.ToString()
                        : "";
        }
    }
}
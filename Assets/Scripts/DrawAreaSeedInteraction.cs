using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DrawAreaSeedInteraction : MonoBehaviour
{
    public const float PickRadius = 0.2f;
    public const float DragPickRadius = 0.5f;
    const float DragPreviewInterval = 0.05f;

    CreatePolygon createPolygon;
    RawImage drawAreaRawImage;
    List<Vector2> seedPoints;
    Func<CursorToolMode> getCursorMode;
    Func<bool> isCutAnimationEnabled;
    Func<bool> canDragSeeds;
    Action onSeedPointsChanged;

    int dragPointIndex = -1;
    bool isDraggingSeed;
    float nextDragPreviewTime;
    Camera dragEventCamera;

    public bool IsDragging => isDraggingSeed;
    public CursorToolMode CursorMode =>
        getCursorMode != null ? getCursorMode() : CursorToolMode.View;

    public void Init(
        List<Vector2> seedPointList,
        CreatePolygon polygon,
        RawImage rawImage,
        Func<CursorToolMode> cursorModeProvider,
        Func<bool> cutAnimationEnabledProvider,
        Func<bool> dragAllowedProvider,
        Action seedPointsChangedHandler)
    {
        seedPoints = seedPointList;
        createPolygon = polygon;
        drawAreaRawImage = rawImage;
        getCursorMode = cursorModeProvider;
        isCutAnimationEnabled = cutAnimationEnabledProvider;
        canDragSeeds = dragAllowedProvider;
        onSeedPointsChanged = seedPointsChangedHandler;
    }

    bool IsDragAllowed() => canDragSeeds == null || canDragSeeds();

    bool UsesStepPlayback() =>
        isCutAnimationEnabled == null || isCutAnimationEnabled();

    public void CancelActiveDrag()
    {
        if (!isDraggingSeed) return;
        EndSeedDrag();
    }

    public void HandlePointerDown(PointerEventData eventData)
    {
        if (createPolygon == null || drawAreaRawImage == null || seedPoints == null) return;

        Vector2 mapPoint = ScreenToMapPoint(eventData.position, eventData.pressEventCamera);
        if (!IsValidMapPoint(mapPoint)) return;

        if (CursorMode == CursorToolMode.Edit
            && eventData.button == PointerEventData.InputButton.Right)
        {
            TryDeleteSeedAt(mapPoint);
            return;
        }

        if (CursorMode == CursorToolMode.Drag
            && eventData.button == PointerEventData.InputButton.Left)
        {
            if (!IsDragAllowed()) return;
            EnsureSeedPointsSyncedFromBackend();
            dragEventCamera = eventData.pressEventCamera;
            dragPointIndex = FindNearestSeedIndex(mapPoint, DragPickRadius);
            isDraggingSeed = dragPointIndex >= 0;
            if (isDraggingSeed)
            {
                nextDragPreviewTime = 0f;
                ApplyDragPosition(dragPointIndex, ClampToMap(mapPoint), forcePreview: true);
            }
        }
    }

    public void HandleDrag(PointerEventData eventData)
    {
        if (!IsDragAllowed())
        {
            CancelActiveDrag();
            return;
        }
        if (!isDraggingSeed || dragPointIndex < 0 || createPolygon == null) return;

        dragEventCamera = eventData.pressEventCamera;
        Vector2 mapPoint = ScreenToMapPoint(eventData.position, dragEventCamera);
        if (!IsValidMapPoint(mapPoint)) return;

        ApplyDragPosition(dragPointIndex, ClampToMap(mapPoint), forcePreview: false);
    }

    public void HandleEndDrag(PointerEventData eventData)
    {
        if (CursorMode == CursorToolMode.Drag && eventData.button == PointerEventData.InputButton.Left)
            EndSeedDrag();
    }

    public void HandleLeftClick(PointerEventData eventData)
    {
        if (CursorMode != CursorToolMode.Edit) return;
        if (createPolygon == null || drawAreaRawImage == null) return;

        Vector2 mapPoint = ScreenToMapPoint(eventData.position, eventData.pressEventCamera);
        TryAddSeedAt(mapPoint);
    }

    public void HandlePointerUp(PointerEventData eventData)
    {
        if (CursorMode == CursorToolMode.Drag
            && eventData.button == PointerEventData.InputButton.Left
            && isDraggingSeed)
        {
            EndSeedDrag();
        }
    }

    public bool HasPointNear(Vector2 point)
    {
        if (createPolygon != null && createPolygon.FindNearestPointIndex(point, PickRadius) >= 0)
            return true;
        float r2 = PickRadius * PickRadius;
        foreach (Vector2 p in seedPoints)
        {
            if ((p - point).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    public bool IsInsideMapBounds(float x, float y)
    {
        if (createPolygon == null) return false;
        return x >= -createPolygon.mapWidth && x <= createPolygon.mapWidth
            && y >= -createPolygon.mapHeight && y <= createPolygon.mapHeight;
    }

    void TryAddSeedAt(Vector2 mapPoint)
    {
        if (!IsValidMapPoint(mapPoint)) return;
        if (!IsInsideMapBounds(mapPoint.x, mapPoint.y)) return;
        if (HasPointNear(mapPoint)) return;

        seedPoints.Add(mapPoint);
        onSeedPointsChanged?.Invoke();
    }

    void TryDeleteSeedAt(Vector2 mapPoint)
    {
        int idx = FindNearestSeedIndex(mapPoint);
        if (idx < 0) return;

        seedPoints.RemoveAt(idx);
        onSeedPointsChanged?.Invoke();
    }

    void ApplyDragPosition(int index, Vector2 mapPoint, bool forcePreview)
    {
        createPolygon.MoveSeedPoint(index, mapPoint);
        if (index >= 0 && index < seedPoints.Count)
            seedPoints[index] = mapPoint;

        if (!forcePreview && Time.unscaledTime < nextDragPreviewTime) return;
        nextDragPreviewTime = Time.unscaledTime + DragPreviewInterval;

        if (UsesStepPlayback())
            createPolygon.RebuildVoronoiFastPreviewAtProgress();
        else
            createPolygon.RebuildVoronoiFastPreview();
    }

    void RefreshDisplayAfterSeedMove()
    {
        if (UsesStepPlayback())
            createPolygon.RebuildVoronoiPreservePlaybackProgress();
        else
            createPolygon.ShowFinalVoronoiState();
    }

    void EndSeedDrag()
    {
        if (!isDraggingSeed) return;

        isDraggingSeed = false;
        dragEventCamera = null;
        if (dragPointIndex >= 0 && createPolygon != null)
            RefreshDisplayAfterSeedMove();
        dragPointIndex = -1;
    }

    void EnsureSeedPointsSyncedFromBackend()
    {
        if (createPolygon == null || seedPoints == null) return;
        if (seedPoints.Count == createPolygon.SeedPoints.Count) return;
        seedPoints.Clear();
        foreach (Vector2 p in createPolygon.SeedPoints)
            seedPoints.Add(p);
    }

    int FindNearestSeedIndex(Vector2 mapPoint, float radius = PickRadius)
    {
        int best = -1;
        float bestDist = radius * radius;
        for (int i = 0; i < seedPoints.Count; i++)
        {
            float d = (seedPoints[i] - mapPoint).sqrMagnitude;
            if (d <= bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        if (best >= 0) return best;
        return createPolygon != null
            ? createPolygon.FindNearestPointIndex(mapPoint, radius)
            : -1;
    }

    Vector2 ClampToMap(Vector2 mapPoint)
    {
        mapPoint.x = Mathf.Clamp(mapPoint.x, -createPolygon.mapWidth, createPolygon.mapWidth);
        mapPoint.y = Mathf.Clamp(mapPoint.y, -createPolygon.mapHeight, createPolygon.mapHeight);
        return mapPoint;
    }

    Vector2 ScreenToMapPoint(Vector2 screenPos, Camera eventCamera)
    {
        RectTransform rt = drawAreaRawImage.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, eventCamera, out Vector2 local))
            return new Vector2(float.NaN, float.NaN);

        Rect rect = rt.rect;
        float u = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
        float v = Mathf.InverseLerp(rect.yMin, rect.yMax, local.y);
        float x = Mathf.Lerp(-createPolygon.mapWidth, createPolygon.mapWidth, u);
        float y = Mathf.Lerp(-createPolygon.mapHeight, createPolygon.mapHeight, v);
        return new Vector2(x, y);
    }

    static bool IsValidMapPoint(Vector2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y);
}

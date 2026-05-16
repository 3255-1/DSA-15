using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public enum SeedPointMode { Random, Manual, Mixed }
public enum CursorToolMode { View, Edit, Drag }
public enum CellFillMode { Wireframe, Solid, Dynamic }
public class Voronoi_Configuration : MonoBehaviour
{
    const int RandomCountMin = 2;
    const int RandomCountMax = 30;
    const float PickRadius = 0.2f;
    const float DragPickRadius = 0.5f;
    const float DragPreviewInterval = 0.05f;

    [Header("References")]
    public CreatePolygon createPolygon;
    public RectTransform configPanelRoot;
    public UnityEngine.UI.RawImage drawAreaRawImage;

    [Header("UI Colors")]
    public Color modeButtonSelectedColor = new Color(0.55f, 0.85f, 1f, 1f);
    public Color modeButtonNormalColor = Color.white;

    SeedPointMode seedMode = SeedPointMode.Random;
    CursorToolMode cursorMode = CursorToolMode.View;
    CellFillMode cellFillMode = CellFillMode.Solid;
    bool playActive;
    bool stepByStepActive;

    Button randomModeButton;
    Button manualModeButton;
    Button mixedModeButton;
    TMP_InputField randomCountInput;
    Button randomGenerateButton;
    TMP_InputField manualXInput;
    TMP_InputField manualYInput;
    Button manualGenerateButton;
    Button viewToolButton;
    Button editToolButton;
    Button dragToolButton;
    Button wireframeFillButton;
    Button solidFillButton;
    Button dynamicFillButton;
    Button playPlaybackButton;
    Button stepPlaybackButton;
    Toggle enableImpactParticlesToggle;
    Toggle enableCutAnimationToggle;
    Slider speedSlider;
    TextMeshProUGUI speedDisplayText;

    readonly List<Vector2> seedPoints = new List<Vector2>();

    int dragPointIndex = -1;
    bool isDraggingSeed;
    float nextDragPreviewTime;
    Camera dragEventCamera;

    public IReadOnlyList<Vector2> SeedPoints => seedPoints;
    public CursorToolMode CursorMode => cursorMode;

    void Awake()
    {
        if (configPanelRoot == null) configPanelRoot = transform as RectTransform;
        if (createPolygon == null) createPolygon = FindFirstObjectByType<CreatePolygon>();
        if (drawAreaRawImage == null)
        {
            Transform drawArea = FindDeep(configPanelRoot.root, "DrawArea");
            if (drawArea != null)
                drawAreaRawImage = drawArea.Find("RawImage")?.GetComponent<UnityEngine.UI.RawImage>();
        }
        BindUI();
    }

    void Start()
    {
        if (drawAreaRawImage != null)
        {
            DrawAreaInput input = drawAreaRawImage.gameObject.GetComponent<DrawAreaInput>();
            if (input == null) input = drawAreaRawImage.gameObject.AddComponent<DrawAreaInput>();
            input.Init(this);
        }

        WireListeners();
        SelectSeedMode(SeedPointMode.Random);
        SelectCursorTool(CursorToolMode.View);
        InitializeAnimationUI();
        SyncSeedPointsToBackend(clearOnly: true);
    }

    void Update()
    {
        if (!stepByStepActive || createPolygon == null || isDraggingSeed) return;
        if (!createPolygon.AllowsStepKeyboard) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            createPolygon.StepBackward();
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            createPolygon.StepForward();
    }

    void BindUI()
    {
        Transform root = configPanelRoot != null ? configPanelRoot : transform;
        randomModeButton = FindButton(root, "RandomButton");
        manualModeButton = FindButton(root, "ManualButton");
        mixedModeButton = FindButton(root, "MixedButton");

        Transform randomGroup = FindDeep(root, "RandomSeedPointCount");
        randomCountInput = randomGroup != null ? randomGroup.GetComponentInChildren<TMP_InputField>(true) : null;
        randomGenerateButton = FindButton(randomGroup, "Generate");

        Transform manualGroup = FindDeep(root, "ManualSeedPointInput");
        TMP_InputField[] manualInputs = manualGroup != null
            ? manualGroup.GetComponentsInChildren<TMP_InputField>(true)
            : new TMP_InputField[0];
        if (manualInputs.Length > 0) manualXInput = manualInputs[0];
        if (manualInputs.Length > 1) manualYInput = manualInputs[1];
        manualGenerateButton = FindButton(manualGroup, "Generate");

        viewToolButton = FindButton(root, "ViewModeButton");
        editToolButton = FindButton(root, "EditModeButton");
        dragToolButton = FindButton(root, "DragModeButton");

        wireframeFillButton = FindButton(root, "WireframeButton");
        solidFillButton = FindButton(root, "SolidButton");

        dynamicFillButton = FindButton(root, "DynamicButton");
        playPlaybackButton = FindButton(root, "Play");
        stepPlaybackButton = FindButton(root, "StepByStep");

        enableImpactParticlesToggle = FindComponent<Toggle>(root, "EnableImpactParticleSystem");
        enableCutAnimationToggle = FindComponent<Toggle>(root, "EnableCutAnimation");

        Transform speedRow = FindSpeedRow(root);
        if (speedRow != null)
        {
            speedSlider = speedRow.Find("Slider")?.GetComponent<Slider>();
            Transform speedDisplay = speedRow.Find("SpeedDisplay");
            if (speedDisplay != null)
                speedDisplayText = speedDisplay.GetComponent<TextMeshProUGUI>();
        }

        if (speedDisplayText == null)
        {
            Transform display = FindDeep(root, "SpeedDisplay");
            if (display != null)
                speedDisplayText = display.GetComponent<TextMeshProUGUI>();
        }
    }

    Transform FindSpeedRow(Transform root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != "Speed") continue;
            if (child.Find("Slider") != null) return child;
        }
        return null;
    }

    void WireListeners()
    {
        randomModeButton?.onClick.AddListener(() => SelectSeedMode(SeedPointMode.Random));
        manualModeButton?.onClick.AddListener(() => SelectSeedMode(SeedPointMode.Manual));
        mixedModeButton?.onClick.AddListener(() => SelectSeedMode(SeedPointMode.Mixed));

        randomGenerateButton?.onClick.AddListener(OnRandomGenerateClicked);
        manualGenerateButton?.onClick.AddListener(OnManualGenerateClicked);

        viewToolButton?.onClick.AddListener(() => SelectCursorTool(CursorToolMode.View));
        editToolButton?.onClick.AddListener(() => SelectCursorTool(CursorToolMode.Edit));
        dragToolButton?.onClick.AddListener(() => SelectCursorTool(CursorToolMode.Drag));

        wireframeFillButton?.onClick.AddListener(() => SelectCellFill(CellFillMode.Wireframe));
        solidFillButton?.onClick.AddListener(() => SelectCellFill(CellFillMode.Solid));

        dynamicFillButton?.onClick.AddListener(() => SelectCellFill(CellFillMode.Dynamic));
        playPlaybackButton?.onClick.AddListener(OnPlayClicked);
        stepPlaybackButton?.onClick.AddListener(OnStepByStepClicked);

        speedSlider?.onValueChanged.AddListener(OnSpeedSliderChanged);
    }

    void InitializeAnimationUI()
    {
        if (enableImpactParticlesToggle != null)
            enableImpactParticlesToggle.isOn = true;
        if (enableCutAnimationToggle != null)
            enableCutAnimationToggle.isOn = true;

        SelectCellFill(CellFillMode.Solid);
        SetPlayActive(false);
        SetStepByStepActive(false);

        if (speedSlider != null)
            UpdateSpeedDisplay(speedSlider.value);
    }

    void OnPlayClicked()
    {
        SetPlayActive(!playActive);
    }

    void OnStepByStepClicked()
    {
        SetStepByStepActive(!stepByStepActive);
    }

    void SetPlayActive(bool active)
    {
        playActive = active;
        SetSelected(playPlaybackButton, playActive);
        if (active)
            SetStepByStepActive(false);
    }

    void SetStepByStepActive(bool active)
    {
        stepByStepActive = active;
        SetSelected(stepPlaybackButton, stepByStepActive);
        if (active)
            SetPlayActive(false);
    }

    void OnSpeedSliderChanged(float value)
    {
        UpdateSpeedDisplay(value);
    }

    void UpdateSpeedDisplay(float value)
    {
        if (speedDisplayText != null)
            speedDisplayText.text = value.ToString("0.##");
    }

    void SelectSeedMode(SeedPointMode mode)
    {
        seedMode = mode;
        SetSelected(randomModeButton, mode == SeedPointMode.Random);
        SetSelected(manualModeButton, mode == SeedPointMode.Manual);
        SetSelected(mixedModeButton, mode == SeedPointMode.Mixed);
        ApplySeedModeInteractivity();
    }

    void ApplySeedModeInteractivity()
    {
        bool randomOn = seedMode == SeedPointMode.Random || seedMode == SeedPointMode.Mixed;
        bool manualOn = seedMode == SeedPointMode.Manual || seedMode == SeedPointMode.Mixed;

        SetUIEnabled(randomCountInput, randomOn);
        SetUIEnabled(randomGenerateButton, randomOn);
        SetUIEnabled(manualXInput, manualOn);
        SetUIEnabled(manualYInput, manualOn);
        SetUIEnabled(manualGenerateButton, manualOn);

        bool editOn = seedMode == SeedPointMode.Manual || seedMode == SeedPointMode.Mixed;
        SetUIEnabled(viewToolButton, true);
        SetUIEnabled(editToolButton, editOn);

        if (seedMode == SeedPointMode.Random && cursorMode == CursorToolMode.Edit)
            SelectCursorTool(CursorToolMode.View);
    }

    void SelectCursorTool(CursorToolMode mode)
    {
        cursorMode = mode;
        SetSelected(viewToolButton, mode == CursorToolMode.View);
        SetSelected(editToolButton, mode == CursorToolMode.Edit);
        SetSelected(dragToolButton, mode == CursorToolMode.Drag);
    }

    void SelectCellFill(CellFillMode mode)
    {
        cellFillMode = mode;
        SetSelected(wireframeFillButton, mode == CellFillMode.Wireframe);
        SetSelected(solidFillButton, mode == CellFillMode.Solid);
        SetSelected(dynamicFillButton, mode == CellFillMode.Dynamic);
    }

    void OnRandomGenerateClicked()
    {
        if (randomCountInput == null || !int.TryParse(randomCountInput.text, out int n)) return;
        if (n < RandomCountMin || n > RandomCountMax) return;

        if (seedMode == SeedPointMode.Random || seedMode == SeedPointMode.Mixed)
        {
            seedPoints.Clear();
            for (int i = 0; i < n; i++)
                seedPoints.Add(geofunc.random_point(createPolygon.mapWidth, createPolygon.mapHeight));
        }
        SyncSeedPointsToBackend();
    }

    void OnManualGenerateClicked()
    {
        if (manualXInput == null || manualYInput == null) return;
        if (!float.TryParse(manualXInput.text, out float x)) return;
        if (!float.TryParse(manualYInput.text, out float y)) return;
        if (!IsInsideMapBounds(x, y)) return;
        Vector2 candidate = new Vector2(x, y);
        if (HasPointNear(candidate)) return;

        seedPoints.Add(candidate);
        SyncSeedPointsToBackend();
    }

    void SyncSeedPointsToBackend(bool clearOnly = false)
    {
        if (createPolygon == null) return;
        if (clearOnly || seedPoints.Count == 0)
        {
            createPolygon.ClearSeedPoints();
            return;
        }
        createPolygon.ClearSeedPoints();
        foreach (Vector2 p in seedPoints)
            createPolygon.TryAddSeedPoint(p, PickRadius);
        if (cursorMode == CursorToolMode.Drag)
            createPolygon.RebuildVoronoiFastPreview();
        else
            createPolygon.RebuildVoronoiStepMode();
    }

    public void HandleDrawAreaPointerDown(PointerEventData eventData)
    {
        if (createPolygon == null || drawAreaRawImage == null) return;

        Vector2 mapPoint = ScreenToMapPoint(eventData.position, eventData.pressEventCamera);
        if (!IsValidMapPoint(mapPoint)) return;

        if (cursorMode == CursorToolMode.Edit
            && eventData.button == PointerEventData.InputButton.Right)
        {
            TryDeleteSeedAt(mapPoint);
            return;
        }

        if (cursorMode == CursorToolMode.Drag
            && eventData.button == PointerEventData.InputButton.Left)
        {
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

    public void HandleDrawAreaDrag(PointerEventData eventData)
    {
        if (!isDraggingSeed || dragPointIndex < 0 || createPolygon == null) return;

        dragEventCamera = eventData.pressEventCamera;
        Vector2 mapPoint = ScreenToMapPoint(eventData.position, dragEventCamera);
        if (!IsValidMapPoint(mapPoint)) return;

        ApplyDragPosition(dragPointIndex, ClampToMap(mapPoint), forcePreview: false);
    }

    public void HandleDrawAreaEndDrag(PointerEventData eventData)
    {
        if (cursorMode == CursorToolMode.Drag && eventData.button == PointerEventData.InputButton.Left)
            EndSeedDrag();
    }

    public void HandleDrawAreaLeftClick(PointerEventData eventData)
    {
        if (cursorMode != CursorToolMode.Edit) return;
        if (createPolygon == null || drawAreaRawImage == null) return;

        Vector2 mapPoint = ScreenToMapPoint(eventData.position, eventData.pressEventCamera);
        TryAddSeedAt(mapPoint);
    }

    public void HandleDrawAreaPointerUp(PointerEventData eventData)
    {
        if (cursorMode == CursorToolMode.Drag
            && eventData.button == PointerEventData.InputButton.Left
            && isDraggingSeed)
        {
            EndSeedDrag();
        }
    }

    void TryAddSeedAt(Vector2 mapPoint)
    {
        if (!IsValidMapPoint(mapPoint)) return;
        if (!IsInsideMapBounds(mapPoint.x, mapPoint.y)) return;
        if (HasPointNear(mapPoint)) return;

        seedPoints.Add(mapPoint);
        SyncSeedPointsToBackend();
    }

    void TryDeleteSeedAt(Vector2 mapPoint)
    {
        int idx = FindNearestSeedIndex(mapPoint);
        if (idx < 0) return;

        seedPoints.RemoveAt(idx);
        createPolygon.TryRemoveSeedPointNear(mapPoint, PickRadius);
        if (seedPoints.Count == 0)
            createPolygon.ClearSeedPoints();
        else
            createPolygon.RebuildVoronoiStepMode();
    }

    void ApplyDragPosition(int index, Vector2 mapPoint, bool forcePreview)
    {
        createPolygon.MoveSeedPoint(index, mapPoint);
        if (index >= 0 && index < seedPoints.Count)
            seedPoints[index] = mapPoint;

        if (!forcePreview && Time.unscaledTime < nextDragPreviewTime) return;
        nextDragPreviewTime = Time.unscaledTime + DragPreviewInterval;
        createPolygon.RebuildVoronoiFastPreview();
    }

    void EndSeedDrag()
    {
        if (!isDraggingSeed) return;

        isDraggingSeed = false;
        dragEventCamera = null;
        if (dragPointIndex >= 0 && createPolygon != null)
        {
            createPolygon.RebuildVoronoiFastPreview();
            createPolygon.FinishDragDisplay();
        }
        dragPointIndex = -1;
    }

    void EnsureSeedPointsSyncedFromBackend()
    {
        if (createPolygon == null) return;
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

    Camera GetDrawAreaEventCamera()
    {
        if (drawAreaRawImage == null) return null;
        Canvas canvas = drawAreaRawImage.canvas;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            return canvas.worldCamera;
        return null;
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

    bool IsInsideMapBounds(float x, float y)
    {
        if (createPolygon == null) return false;
        return x >= -createPolygon.mapWidth && x <= createPolygon.mapWidth
            && y >= -createPolygon.mapHeight && y <= createPolygon.mapHeight;
    }

    bool HasPointNear(Vector2 point)
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

    static void SetUIEnabled(Component c, bool enabled)
    {
        if (c == null) return;
        if (c is Selectable selectable) selectable.interactable = enabled;
    }

    void SetSelected(Button button, bool selected)
    {
        if (button == null) return;
        ColorBlock colors = button.colors;
        colors.normalColor = selected ? modeButtonSelectedColor : modeButtonNormalColor;
        colors.highlightedColor = selected ? modeButtonSelectedColor : modeButtonNormalColor;
        colors.selectedColor = selected ? modeButtonSelectedColor : modeButtonNormalColor;
        button.colors = colors;
    }

    static Button FindButton(Transform root, string childName)
    {
        Transform t = FindDeep(root, childName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    static T FindComponent<T>(Transform root, string childName) where T : Component
    {
        Transform t = FindDeep(root, childName);
        return t != null ? t.GetComponent<T>() : null;
    }

    static Transform FindDeep(Transform root, string path)
    {
        if (root == null) return null;
        if (path.Contains("/"))
        {
            Transform t = root.Find(path);
            if (t != null) return t;
        }
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == path) return child;
        }
        return null;
    }
}

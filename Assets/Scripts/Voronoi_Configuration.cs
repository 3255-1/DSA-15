using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public enum SeedPointMode { Random, Manual, Mixed }
public enum CursorToolMode { View, Edit, Drag }
public enum CellFillMode { Wireframe, Solid, Radial }
public class Voronoi_Configuration : MonoBehaviour
{
    const int RandomCountMin = 2;
    const int RandomCountMax = 100;
    const int MaxSeedPoints = 100;

    [Header("References")]
    public CreatePolygon createPolygon;
    public RectTransform configPanelRoot;
    public UnityEngine.UI.RawImage drawAreaRawImage;
    public GameObject invalidToastPrefab;
    public RectTransform invalidToastParent;

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
    Button manualAddButton;
    Button viewToolButton;
    Button editToolButton;
    Button dragToolButton;
    Button wireframeFillButton;
    Button solidFillButton;
    Button radialFillButton;
    Button playPlaybackButton;
    Button stepPlaybackButton;
    Button lrButton;
    Button clearAllDiagramButton;
    Button clearAllSeedPointButton;
    TextMeshProUGUI totalSeedPointText;
    Toggle enableImpactParticlesToggle;
    Toggle enableCutAnimationToggle;
    Toggle enableBisectorLineToggle;
    Slider speedSlider;
    TextMeshProUGUI speedDisplayText;

    DrawAreaSeedInteraction drawAreaInteraction;

    readonly List<Vector2> seedPoints = new List<Vector2>();

    float nextAutoPlayStepTime;

    public IReadOnlyList<Vector2> SeedPoints => seedPoints;
    public CursorToolMode CursorMode => cursorMode;

    bool IsCutAnimationEnabled() =>
        enableCutAnimationToggle == null || enableCutAnimationToggle.isOn;

    void Awake()
    {
        if (configPanelRoot == null) configPanelRoot = transform as RectTransform;
        if (createPolygon == null) createPolygon = FindFirstObjectByType<CreatePolygon>();
        EnsureInvalidToastPrefab();
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
        SetupDrawAreaInput();
        WireListeners();
        SelectSeedMode(SeedPointMode.Random);
        SelectCursorTool(CursorToolMode.View);
        InitializeAnimationUI();
        SyncSeedPointsToBackend(clearOnly: true);
        UpdateTotalSeedPointDisplay();
    }

    void SetupDrawAreaInput()
    {
        if (drawAreaRawImage == null) return;

        drawAreaInteraction = drawAreaRawImage.GetComponent<DrawAreaSeedInteraction>();
        if (drawAreaInteraction == null)
            drawAreaInteraction = drawAreaRawImage.gameObject.AddComponent<DrawAreaSeedInteraction>();
        drawAreaInteraction.Init(
            seedPoints,
            createPolygon,
            drawAreaRawImage,
            () => cursorMode,
            IsCutAnimationEnabled,
            () => !playActive,
            () => SyncSeedPointsToBackend(),
            IsAtSeedLimit,
            ShowInvalidToast);

        DrawAreaInput input = drawAreaRawImage.GetComponent<DrawAreaInput>();
        if (input == null) input = drawAreaRawImage.gameObject.AddComponent<DrawAreaInput>();
        input.Init(drawAreaInteraction);
    }

    void Update()
    {
        UpdateStepByStepInput();
        UpdateAutoPlay();
    }

    void UpdateStepByStepInput()
    {
        if (!IsCutAnimationEnabled() || createPolygon == null || IsDraggingSeed()) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            TryStepBackward();
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            TryStepForward();
    }

    void UpdateAutoPlay()
    {
        if (!IsCutAnimationEnabled()) return;
        if (!playActive || createPolygon == null || IsDraggingSeed()) return;

        if (seedPoints.Count == 0)
        {
            SetPlayActive(false);
            ShowInvalidToast();
            return;
        }

        if (!createPolygon.AllowsStepPlayback)
            createPolygon.EnsureStepPlaybackReady();

        if (createPolygon.HasCompletedPlayback)
        {
            SetPlayActive(false);
            return;
        }

        if (Time.unscaledTime < nextAutoPlayStepTime) return;

        nextAutoPlayStepTime = Time.unscaledTime + GetPlayStepInterval();
        if (createPolygon.StepForward())
            SetPlayActive(false);
    }

    bool IsDraggingSeed() => drawAreaInteraction != null && drawAreaInteraction.IsDragging;

    float GetPlayStepInterval()
    {
        float speed = speedSlider != null ? speedSlider.value : 1f;
        return 1f / Mathf.Max(speed, 0.1f);
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
        manualAddButton = FindButton(manualGroup, "Add");

        viewToolButton = FindButton(root, "ViewModeButton");
        editToolButton = FindButton(root, "EditModeButton");
        dragToolButton = FindButton(root, "DragModeButton");

        wireframeFillButton = FindButton(root, "WireframeButton");
        solidFillButton = FindButton(root, "SolidButton");

        radialFillButton = FindButton(root, "RadialButton");
        playPlaybackButton = FindButton(root, "Play");
        stepPlaybackButton = FindButton(root, "StepByStep");
        lrButton = FindButton(root, "LR");
        if (lrButton == null)
            lrButton = FindButton(root, "LR_Button");

        clearAllDiagramButton = FindButton(root, "ClearAllDiagramButton");
        if (clearAllDiagramButton == null)
            clearAllDiagramButton = FindButton(root, "ClearAllDiagram");
        if (clearAllDiagramButton == null)
            clearAllDiagramButton = FindButton(root, "ClearDiagramButton");

        clearAllSeedPointButton = FindButton(root, "ClearAllSeedPoint");
        Transform totalSeed = FindDeep(root, "TotalSeedPoint");
        if (totalSeed != null)
            totalSeedPointText = totalSeed.GetComponent<TextMeshProUGUI>();

        enableImpactParticlesToggle = FindComponent<Toggle>(root, "EnableImpactParticleSystem");
        enableCutAnimationToggle = FindComponent<Toggle>(root, "EnableCutAnimation");
        enableBisectorLineToggle = FindComponent<Toggle>(root, "EnableBisectorLine");

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

        SetupSpeedSliderUI(speedRow);
    }

    void SetupSpeedSliderUI(Transform speedRow)
    {
        if (speedRow == null) return;

        LayoutElement layoutElement = speedRow.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = speedRow.gameObject.AddComponent<LayoutElement>();
        layoutElement.minHeight = 36f;

        TextMeshProUGUI rowLabel = speedRow.GetComponent<TextMeshProUGUI>();
        if (rowLabel != null)
            rowLabel.raycastTarget = false;
        if (speedDisplayText != null)
            speedDisplayText.raycastTarget = false;
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
        manualAddButton?.onClick.AddListener(OnManualAddClicked);

        viewToolButton?.onClick.AddListener(() => SelectCursorTool(CursorToolMode.View));
        editToolButton?.onClick.AddListener(() => SelectCursorTool(CursorToolMode.Edit));
        dragToolButton?.onClick.AddListener(() => SelectCursorTool(CursorToolMode.Drag));

        wireframeFillButton?.onClick.AddListener(() => SelectCellFill(CellFillMode.Wireframe));
        solidFillButton?.onClick.AddListener(() => SelectCellFill(CellFillMode.Solid));

        radialFillButton?.onClick.AddListener(() => SelectCellFill(CellFillMode.Radial));
        playPlaybackButton?.onClick.AddListener(OnPlayClicked);
        stepPlaybackButton?.onClick.AddListener(OnStepByStepClicked);
        lrButton?.onClick.AddListener(OnLRClicked);
        clearAllDiagramButton?.onClick.AddListener(OnClearAllDiagramClicked);
        clearAllSeedPointButton?.onClick.AddListener(OnClearAllSeedPointClicked);

        speedSlider?.onValueChanged.AddListener(OnSpeedSliderChanged);
        enableCutAnimationToggle?.onValueChanged.AddListener(OnCutAnimationToggleChanged);
        enableBisectorLineToggle?.onValueChanged.AddListener(OnBisectorLineToggleChanged);
    }

    void InitializeAnimationUI()
    {
        if (enableImpactParticlesToggle != null)
            enableImpactParticlesToggle.isOn = true;
        if (enableCutAnimationToggle != null)
            enableCutAnimationToggle.isOn = true;
        if (enableBisectorLineToggle != null)
            createPolygon?.SetGuideLinesVisible(enableBisectorLineToggle.isOn);

        SelectCellFill(CellFillMode.Solid);
        SetPlayActive(false);
        SetStepByStepActive(false);

        if (speedSlider != null)
            UpdateSpeedDisplay(speedSlider.value);

        ApplyAnimationControlsInteractable();
        SetUIEnabled(dragToolButton, !playActive);
    }

    void OnCutAnimationToggleChanged(bool _)
    {
        ApplyAnimationControlsInteractable();
        RedrawPreserveProgressOrFinal();
    }

    void OnBisectorLineToggleChanged(bool enabled)
    {
        createPolygon?.SetGuideLinesVisible(enabled);
    }

    void RefreshVoronoiDisplay()
    {
        if (createPolygon == null || seedPoints.Count == 0) return;
        if (!IsCutAnimationEnabled())
        {
            createPolygon.ShowFinalVoronoiState();
            return;
        }
        if (createPolygon.HasDiagramStarted)
            createPolygon.RefreshCellFillAtCurrentProgress();
    }

    void RedrawPreserveProgressOrFinal()
    {
        if (createPolygon == null || seedPoints.Count == 0) return;
        if (!createPolygon.HasDiagramStarted)
        {
            createPolygon.ClearDiagramVisuals();
            return;
        }
        if (!IsCutAnimationEnabled())
            createPolygon.ShowFinalVoronoiState();
        else
            createPolygon.RefreshCellFillAtCurrentProgress();
    }

    void ApplyAnimationControlsInteractable()
    {
        bool animOn = IsCutAnimationEnabled();
        SetUIEnabled(enableImpactParticlesToggle, animOn);
        SetUIEnabled(playPlaybackButton, animOn);
        SetUIEnabled(stepPlaybackButton, animOn);
        SetUIEnabled(lrButton, animOn);

        if (!animOn)
        {
            SetPlayActive(false);
            SetStepByStepActive(false);
        }
    }

    void OnPlayClicked()
    {
        if (!IsCutAnimationEnabled()) return;
        if (!playActive && seedPoints.Count == 0)
        {
            ShowInvalidToast();
            return;
        }
        SetPlayActive(!playActive);
    }

    void OnLRClicked() => OnStepByStepClicked();

    void OnStepByStepClicked()
    {
        if (!IsCutAnimationEnabled()) return;
        if (!stepByStepActive && seedPoints.Count == 0)
        {
            ShowInvalidToast();
            return;
        }
        SetStepByStepActive(!stepByStepActive);
    }

    void SetPlayActive(bool active)
    {
        if (!IsCutAnimationEnabled())
            active = false;

        if (active)
        {
            if (createPolygon == null || seedPoints.Count == 0)
            {
                playActive = false;
                SetSelected(playPlaybackButton, false);
                ShowInvalidToast();
                return;
            }

            createPolygon.EnsureStepPlaybackReady();
            if (createPolygon.HasCompletedPlayback)
                createPolygon.RestartStepPlayback();
            nextAutoPlayStepTime = Time.unscaledTime;
            SetStepByStepActive(false);
        }

        playActive = active;
        SetSelected(playPlaybackButton, playActive);
        SetUIEnabled(dragToolButton, !playActive);
        if (playActive)
            drawAreaInteraction?.CancelActiveDrag();
    }

    void SetStepByStepActive(bool active)
    {
        stepByStepActive = active;
        SetSelected(stepPlaybackButton, stepByStepActive);
        SetSelected(lrButton, stepByStepActive);
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
        SetUIEnabled(manualAddButton, manualOn);

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
        SetSelected(radialFillButton, mode == CellFillMode.Radial);

        if (createPolygon == null) return;
        createPolygon.SetCellFillMode(mode);
        if (seedPoints.Count > 0)
            RedrawPreserveProgressOrFinal();
    }

    void OnRandomGenerateClicked()
    {
        if (randomCountInput == null || !int.TryParse(randomCountInput.text, out int n))
        {
            ShowInvalidToast();
            return;
        }
        if (n < RandomCountMin || n > RandomCountMax)
        {
            ShowInvalidToast();
            return;
        }

        if (seedMode == SeedPointMode.Random || seedMode == SeedPointMode.Mixed)
        {
            seedPoints.Clear();
            for (int i = 0; i < n; i++)
                seedPoints.Add(geofunc.random_point(createPolygon.mapWidth, createPolygon.mapHeight));
        }
        SyncSeedPointsToBackend();
    }

    void OnManualAddClicked()
    {
        if (manualXInput == null || manualYInput == null || drawAreaInteraction == null) return;
        if (!float.TryParse(manualXInput.text, out float x))
        {
            ShowInvalidToast();
            return;
        }
        if (!float.TryParse(manualYInput.text, out float y))
        {
            ShowInvalidToast();
            return;
        }
        if (IsAtSeedLimit())
        {
            ShowInvalidToast();
            return;
        }
        if (!drawAreaInteraction.IsInsideMapBounds(x, y))
        {
            ShowInvalidToast();
            return;
        }
        Vector2 candidate = new Vector2(x, y);
        if (drawAreaInteraction.HasPointNear(candidate))
        {
            ShowInvalidToast();
            return;
        }

        seedPoints.Add(candidate);
        SyncSeedPointsToBackend();
    }

    void TryStepBackward()
    {
        if (!IsCutAnimationEnabled()) return;
        if (seedPoints.Count == 0)
        {
            ShowInvalidToast();
            return;
        }
        if (!stepByStepActive || createPolygon == null) return;
        if (!createPolygon.AllowsStepPlayback) return;
        createPolygon.StepBackward();
    }

    void TryStepForward()
    {
        if (!IsCutAnimationEnabled()) return;
        if (seedPoints.Count == 0)
        {
            ShowInvalidToast();
            return;
        }
        if (!stepByStepActive || createPolygon == null) return;
        if (!createPolygon.AllowsStepPlayback) return;
        createPolygon.StepForward();
    }

    void OnClearAllDiagramClicked()
    {
        if (seedPoints.Count == 0)
        {
            ShowInvalidToast();
            return;
        }

        drawAreaInteraction?.CancelActiveDrag();
        SetPlayActive(false);
        SetStepByStepActive(false);
        if (createPolygon != null)
            createPolygon.ClearDiagramVisuals();
    }

    void OnClearAllSeedPointClicked()
    {
        if (seedPoints.Count == 0)
        {
            ShowInvalidToast();
            return;
        }

        drawAreaInteraction?.CancelActiveDrag();
        SetPlayActive(false);
        SetStepByStepActive(false);
        seedPoints.Clear();
        SyncSeedPointsToBackend(clearOnly: true);
    }

    bool IsAtSeedLimit() => seedPoints.Count >= MaxSeedPoints;

    void UpdateTotalSeedPointDisplay()
    {
        if (totalSeedPointText != null)
            totalSeedPointText.text = $"Total Seed Point: {seedPoints.Count}/{MaxSeedPoints}";
    }

    void EnsureInvalidToastPrefab()
    {
        if (invalidToastPrefab != null) return;
        invalidToastPrefab = Resources.Load<GameObject>("Invalid");
#if UNITY_EDITOR
        if (invalidToastPrefab == null)
            invalidToastPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Objects/Invalid.prefab");
#endif
    }

    void ShowInvalidToast()
    {
        EnsureInvalidToastPrefab();
        if (invalidToastPrefab == null) return;

        Transform parent = invalidToastParent;
        if (parent == null)
        {
            Canvas canvas = configPanelRoot != null
                ? configPanelRoot.GetComponentInParent<Canvas>()
                : null;
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            parent = canvas != null ? canvas.transform : transform;
        }

        GameObject instance = Instantiate(invalidToastPrefab, parent);
        InvalidToast toast = instance.GetComponent<InvalidToast>();
        if (toast == null)
            toast = instance.AddComponent<InvalidToast>();
        toast.Play();
    }

    void SyncSeedPointsToBackend(bool clearOnly = false)
    {
        if (createPolygon == null) return;
        if (clearOnly || seedPoints.Count == 0)
        {
            createPolygon.ClearSeedPoints();
            UpdateTotalSeedPointDisplay();
            return;
        }
        createPolygon.ClearSeedPoints();
        foreach (Vector2 p in seedPoints)
            createPolygon.TryAddSeedPoint(p, DrawAreaSeedInteraction.PickRadius);

        RefreshVoronoiDisplay();
        UpdateTotalSeedPointDisplay();
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
        if (t == null) return null;
        Button button = t.GetComponent<Button>();
        if (button == null)
            button = t.GetComponentInChildren<Button>(true);
        return button;
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

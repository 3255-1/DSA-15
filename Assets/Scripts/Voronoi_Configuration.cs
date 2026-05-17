using System.Collections.Generic;
using UnityEngine;
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

    DrawAreaSeedInteraction drawAreaInteraction;

    readonly List<Vector2> seedPoints = new List<Vector2>();

    float nextAutoPlayStepTime;

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
        SetupDrawAreaInput();
        WireListeners();
        SelectSeedMode(SeedPointMode.Random);
        SelectCursorTool(CursorToolMode.View);
        InitializeAnimationUI();
        SyncSeedPointsToBackend(clearOnly: true);
    }

    void SetupDrawAreaInput()
    {
        if (drawAreaRawImage == null) return;

        drawAreaInteraction = drawAreaRawImage.GetComponent<DrawAreaSeedInteraction>();
        if (drawAreaInteraction == null)
            drawAreaInteraction = drawAreaRawImage.gameObject.AddComponent<DrawAreaSeedInteraction>();
        drawAreaInteraction.Init(this, seedPoints, createPolygon, drawAreaRawImage);

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
        if (!stepByStepActive || createPolygon == null || IsDraggingSeed()) return;
        if (!createPolygon.AllowsStepPlayback) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            createPolygon.StepBackward();
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            createPolygon.StepForward();
    }

    void UpdateAutoPlay()
    {
        if (!playActive || createPolygon == null || IsDraggingSeed()) return;

        if (seedPoints.Count == 0)
        {
            SetPlayActive(false);
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
        if (active)
        {
            if (createPolygon == null || seedPoints.Count == 0)
            {
                playActive = false;
                SetSelected(playPlaybackButton, false);
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
        if (manualXInput == null || manualYInput == null || drawAreaInteraction == null) return;
        if (!float.TryParse(manualXInput.text, out float x)) return;
        if (!float.TryParse(manualYInput.text, out float y)) return;
        if (!drawAreaInteraction.IsInsideMapBounds(x, y)) return;
        Vector2 candidate = new Vector2(x, y);
        if (drawAreaInteraction.HasPointNear(candidate)) return;

        seedPoints.Add(candidate);
        SyncSeedPointsToBackend();
    }

    public void SyncSeedPointsToBackend(bool clearOnly = false)
    {
        if (createPolygon == null) return;
        if (clearOnly || seedPoints.Count == 0)
        {
            createPolygon.ClearSeedPoints();
            return;
        }
        createPolygon.ClearSeedPoints();
        foreach (Vector2 p in seedPoints)
            createPolygon.TryAddSeedPoint(p, DrawAreaSeedInteraction.PickRadius);
        if (cursorMode == CursorToolMode.Drag)
            createPolygon.RebuildVoronoiFastPreview();
        else
            createPolygon.RebuildVoronoiStepMode();
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

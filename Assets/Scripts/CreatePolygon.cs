using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using Unity.VisualScripting;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(LineRenderer))]
public class CreatePolygon : MonoBehaviour
{
    [Header("地圖設定")]
    [Tooltip("邊界半寬：有效範圍 X 為 ±mapWidth")]
    public float mapWidth = 14f;
    [Tooltip("邊界半高：有效範圍 Y 為 ±mapHeight")]
    public float mapHeight = 10.8f;
    public int pointCount = 5;
    public bool autoGenerateOnStart = false;

    [Header("顏色與材質設定")]
    public Material meshMaterial;
    public Color polygonColor = new Color(0f, 0.8f, 1f, 0.8f);
    public Color seedPointColor = Color.white;
    public float seedPointRadius = 0.25f;

    [Header("邊界線設定")]
    public Color borderlineColor = Color.black;
    public Color currentCellBorderColor = Color.black;
    public float borderWidth = 0.05f;
    public float currentBorderWidth = 0.2f;
    public Material borderMaterial;

    private LineRenderer bisectorLineRenderer;
    private LineRenderer connectionLineRenderer;
    private LineRenderer lineRenderer;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private Voronoi voronoiResult;
    private List<Vector2> randomPoints = new List<Vector2>();
    private List<GameObject> seedPointMarkers = new List<GameObject>();
    private List<GameObject> meshs = new List<GameObject>();
    private readonly List<Color> cellColors = new List<Color>();
    private readonly List<int> cellPaletteIndices = new List<int>();
    private readonly List<LineRenderer> borderlines = new List<LineRenderer>();

    private int currentCellIdx = 0;
    private int currentStepIdx = -1;
    private GameObject seedpointParent;
    private bool restNextStep = false;
    private bool fastPreviewMode = false;
    private bool showGuideLines = true;
    private CellFillMode cellFillMode = CellFillMode.Radial;
    static readonly Color WireframeFillColor = new Color(0xDB / 255f, 0xDB / 255f, 0xDB / 255f, .95f);
    const float CellMeshZBase = 0.5f;
    const float CellMeshZStep = 0.01f;
    const float CellBorderZOffset = 0.005f;
    const float CellPreviewZOffset = 0.003f;
    const float CellGuideLineZOffset = 0.008f;
    const float SeedPointZOffset = 0.05f;
    const int SeedPointSortingOrderBase = 10000;

    static float GetCellMeshZ(int cellIndex) => CellMeshZBase - CellMeshZStep * cellIndex;
    static float GetSeedPointZ(int seedCount) =>
        seedCount <= 0 ? CellMeshZBase - SeedPointZOffset : GetCellMeshZ(seedCount - 1) - SeedPointZOffset;
    static float GetCellBorderZ(int cellIndex) => GetCellMeshZ(cellIndex) - CellBorderZOffset;
    static float GetCellPreviewZ(int cellIndex) => GetCellMeshZ(cellIndex) - CellPreviewZOffset;
    static float GetCellGuideLineZ(int cellIndex) => GetCellMeshZ(cellIndex) - CellGuideLineZOffset;

    public IReadOnlyList<Vector2> SeedPoints => randomPoints;

    public void SetCellFillMode(CellFillMode mode) => cellFillMode = mode;
    public bool seedpointVisibility=true;

    public void SetGuideLinesVisible(bool visible)
    {
        if (showGuideLines == visible) return;
        showGuideLines = visible;
        ClearGuideLines();
        if (visible && voronoiResult != null && !fastPreviewMode)
            UpdateVisualization();
    }

    public bool AllowsStepPlayback => !fastPreviewMode;
    public bool HasSeedPoints => randomPoints.Count > 0;
    public bool HasDiagramStarted => voronoiResult != null;

    public void EnsureStepPlaybackReady()
    {
        if (randomPoints.Count == 0) return;
        if (fastPreviewMode || voronoiResult == null)
            RebuildVoronoiStepMode();
    }

    public void RestartStepPlayback()
    {
        if (randomPoints.Count == 0) return;
        if (voronoiResult == null)
        {
            RebuildVoronoiStepMode();
            return;
        }
        fastPreviewMode = false;
        restNextStep = false;
        currentCellIdx = 0;
        currentStepIdx = -1;
        ClearAllCellMeshes();
        ClearBorderlineFrom(0);
        UpdateVisualization();
    }

    public void ShowFinalVoronoiState()
    {
        fastPreviewMode = false;
        bisectorLineRenderer.positionCount = 0;
        connectionLineRenderer.positionCount = 0;
        meshFilter.mesh = new Mesh();
        if (lineRenderer != null) lineRenderer.positionCount = 0;

        if (randomPoints.Count == 0)
        {
            ClearVoronoiMeshes();
            return;
        }

        EnsureCellMeshes(randomPoints.Count);
        CreateBorderlines(randomPoints.Count);
        ClearAllCellMeshes();
        ClearAllBorderlines();

        voronoiResult = new Voronoi(new List<Vector2>(randomPoints), mapWidth, mapHeight);
        AssignGraphColors(voronoiResult.neighborLists);

        for (int i = 0; i < meshs.Count; i++)
        {
            MeshFilter mf = meshs[i].GetComponent<MeshFilter>();
            Color color = GetCellColor(i);
            Polygon cell = i < voronoiResult.cells.Count ? voronoiResult.cells[i] : null;
            if (cell != null && cell.vertices != null && cell.vertices.Count >= 3)
            {
                mf.mesh = CreatePolygonMesh(cell.vertices, color, randomPoints[i], i);
                DrawBorderline(cell.vertices, i);
                CopyBorderline(i);
            }
            else
            {
                mf.mesh = new Mesh();
                if (i < borderlines.Count) borderlines[i].positionCount = 0;
            }
        }

        if (lineRenderer != null) lineRenderer.positionCount = 0;
        currentCellIdx = randomPoints.Count;
        currentStepIdx = -2;
        restNextStep = true;
    }

    void Start()
    {
        EnsureCellMeshes(pointCount);
        CreateBorderlines(pointCount);
        SetupComponents();
        RefreshSeedPointMarkers();
        if (autoGenerateOnStart) GenerateNewVoronoi();
    }

    
    void Update()
    {
        if (Keyboard.current == null) return;
        if (Keyboard.current.rKey.wasPressedThisFrame) GenerateNewVoronoi();
        if (Keyboard.current.spaceKey.wasPressedThisFrame) ShowFinalVoronoiState();
        if(Keyboard.current.hKey.wasPressedThisFrame){
            seedpointVisibility=!seedpointVisibility;
            SeedpointVisibilityToggle(seedpointVisibility);
        }
    }

    public bool StepForward() => NextStep();
    public void StepBackward() => PreviousStep();

    public bool HasCompletedPlayback =>
        voronoiResult != null && restNextStep && currentCellIdx >= voronoiResult.pts.Count;

    public void ClearSeedPoints()
    {
        randomPoints.Clear();
        ClearDiagramVisuals();
        RefreshSeedPointMarkers();
    }

    public void ClearDiagramVisuals()
    {
        fastPreviewMode = false;
        restNextStep = false;
        currentCellIdx = 0;
        currentStepIdx = -1;
        voronoiResult = null;
        cellColors.Clear();
        cellPaletteIndices.Clear();

        ClearAllCellMeshes();
        ClearAllBorderlines();
        meshFilter.mesh = new Mesh();
        if (lineRenderer != null) lineRenderer.positionCount = 0;
        if (bisectorLineRenderer != null) bisectorLineRenderer.positionCount = 0;
        if (connectionLineRenderer != null) connectionLineRenderer.positionCount = 0;
    }

    public void SetRandomSeedPoints(int count)
    {
        randomPoints.Clear();
        for (int i = 0; i < count; i++)
            randomPoints.Add(geofunc.random_point(mapWidth, mapHeight));
        EnsureCellMeshes(Mathf.Max(randomPoints.Count, 1));
        CreateBorderlines(Mathf.Max(randomPoints.Count, 1));
        RefreshSeedPointMarkers();
    }

    public bool TryAddSeedPoint(Vector2 point, float minSeparation = 0.5f)
    {
        if (FindNearestPointIndex(point, minSeparation) >= 0) return false;
        randomPoints.Add(point);
        EnsureCellMeshes(Mathf.Max(randomPoints.Count, meshs.Count));
        CreateBorderlines(Mathf.Max(randomPoints.Count, borderlines.Count));
        RefreshSeedPointMarkers();
        return true;
    }

    public bool TryRemoveSeedPointNear(Vector2 point, float pickRadius)
    {
        int idx = FindNearestPointIndex(point, pickRadius);
        if (idx < 0) return false;
        randomPoints.RemoveAt(idx);
        RefreshSeedPointMarkers();
        return true;
    }

    public int FindNearestPointIndex(Vector2 point, float pickRadius)
    {
        int best = -1;
        float bestDist = pickRadius * pickRadius;
        for (int i = 0; i < randomPoints.Count; i++)
        {
            float d = (randomPoints[i] - point).sqrMagnitude;
            if (d <= bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    public void UpdateSeedPoint(int index, Vector2 point)
    {
        if (index < 0 || index >= randomPoints.Count) return;
        randomPoints[index] = point;
        RefreshSeedPointMarkers();
    }

    public void MoveSeedPoint(int index, Vector2 point)
    {
        if (index < 0 || index >= randomPoints.Count) return;
        randomPoints[index] = point;
        if (index >= seedPointMarkers.Count || seedPointMarkers[index] == null) return;
        Transform t = seedPointMarkers[index].transform;
        t.localPosition = new Vector3(point.x, point.y, GetSeedPointZ(randomPoints.Count));
    }

    public void RebuildVoronoiStepMode()
    {
        fastPreviewMode = false;
        if (randomPoints.Count == 0)
        {
            ClearVoronoiMeshes();
            return;
        }
        EnsureCellMeshes(randomPoints.Count);
        CreateBorderlines(randomPoints.Count);
        ClearAllCellMeshes();
        ClearAllBorderlines();
        voronoiResult = new Voronoi(new List<Vector2>(randomPoints), mapWidth, mapHeight);
        AssignGraphColors(voronoiResult.neighborLists);
        currentCellIdx = 0;
        currentStepIdx = -1;
        restNextStep = false;
        UpdateVisualization();
    }

    public void RebuildVoronoiFastPreview()
    {
        fastPreviewMode = true;
        bisectorLineRenderer.positionCount = 0;
        connectionLineRenderer.positionCount = 0;
        meshFilter.mesh = new Mesh();
        if (lineRenderer != null) lineRenderer.positionCount = 0;

        if (randomPoints.Count == 0)
        {
            ClearVoronoiMeshes();
            return;
        }

        EnsureCellMeshes(randomPoints.Count);
        CreateBorderlines(randomPoints.Count);
        List<Polygon> cells = Voronoi.ComputeCells(randomPoints, mapWidth, mapHeight);
        
        AssignGraphColors(cells);

        for (int i = 0; i < meshs.Count; i++)
        {
            if (i < cells.Count)
                DrawCellFromComputePreview(i, cells[i]);
            else
            {
                ClearCellMesh(i);
                ClearBorderline(i);
            }
        }
    }

    public void RebuildVoronoiFastPreviewAtProgress()
    {
        fastPreviewMode = true;
        bisectorLineRenderer.positionCount = 0;
        connectionLineRenderer.positionCount = 0;

        if (randomPoints.Count == 0)
        {
            ClearVoronoiMeshes();
            return;
        }

        int savedCell = voronoiResult != null ? currentCellIdx : 0;
        int savedStep = voronoiResult != null ? currentStepIdx : -1;
        bool savedRest = voronoiResult != null && restNextStep;

        EnsureCellMeshes(randomPoints.Count);
        CreateBorderlines(randomPoints.Count);
        ClearAllCellMeshes();
        ClearAllBorderlines();
        meshFilter.mesh = new Mesh();
        if (lineRenderer != null) lineRenderer.positionCount = 0;

        List<Polygon> cells = Voronoi.ComputeCells(randomPoints, mapWidth, mapHeight);
        AssignGraphColors(cells);

        int n = randomPoints.Count;

        if (savedRest && savedCell >= n)
        {
            for (int i = 0; i < n; i++)
            {
                if (i < cells.Count)
                    DrawCellFromComputePreview(i, cells[i]);
            }
            currentCellIdx = savedCell;
            currentStepIdx = savedStep;
            restNextStep = true;
            return;
        }

        savedCell = Mathf.Clamp(savedCell, 0, n - 1);
        currentCellIdx = savedCell;
        currentStepIdx = savedStep;
        restNextStep = savedRest;

        for (int c = 0; c < savedCell; c++)
        {
            if (c < cells.Count)
                DrawCellFromComputePreview(c, cells[c]);
        }

        if (savedStep == -2)
            return;

        if (savedStep == -1)
        {
            DrawBoundingBoxOnMeshFilter(savedCell);
            return;
        }

        // 進行中的 cell：用既有 step 多邊形預覽，避免 ComputeCells 直接顯示最終外形
        if (!TryDrawCurrentCellStepOnMeshFilter(savedCell, savedStep))
            meshFilter.mesh = new Mesh();
    }

    bool TryDrawCurrentCellStepOnMeshFilter(int cellIndex, int stepIndex)
    {
        if (voronoiResult == null || cellIndex < 0 || cellIndex >= voronoiResult.stepLists.Count)
            return false;
        List<CutStep> steps = voronoiResult.stepLists[cellIndex];
        if (steps == null || stepIndex < 0 || stepIndex >= steps.Count)
            return false;
        Polygon poly = steps[stepIndex].currentPolygon;
        if (poly == null || poly.vertices == null || poly.vertices.Count < 3)
            return false;

        Color color = GetCellColor(cellIndex);
        meshFilter.mesh = CreatePolygonMesh(poly.vertices, color, randomPoints[cellIndex], cellIndex, previewOverlay: true);
        return true;
    }

    void DrawCellFromComputePreview(int cellIndex, Polygon cell)
    {
        if (cellIndex < 0 || cellIndex >= meshs.Count) return;
        if (cell == null || cell.vertices == null || cell.vertices.Count < 3)
        {
            ClearCellMesh(cellIndex);
            ClearBorderline(cellIndex);
            return;
        }

        Color color = GetCellColor(cellIndex);
        meshs[cellIndex].GetComponent<MeshFilter>().mesh =
            CreatePolygonMesh(cell.vertices, color, randomPoints[cellIndex], cellIndex);
        DrawBorderline(cell.vertices, cellIndex);
        CopyBorderline(cellIndex);
        if (lineRenderer != null) lineRenderer.positionCount = 0;
    }

    void DrawBoundingBoxOnMeshFilter(int cellIndex)
    {
        Vector2 b1 = new Vector2(-mapWidth, -mapHeight);
        Vector2 b2 = new Vector2(mapWidth, -mapHeight);
        Vector2 b3 = new Vector2(mapWidth, mapHeight);
        Vector2 b4 = new Vector2(-mapWidth, mapHeight);
        List<Vector2> bbox = new List<Vector2> { b1, b2, b3, b4 };
        Color color = GetCellColor(cellIndex);
        meshFilter.mesh = CreatePolygonMesh(bbox, color, randomPoints[cellIndex], cellIndex, previewOverlay: true);
    }

    void DrawCurrentCellOnMeshFilter(int cellIndex, Polygon cell)
    {
        if (cell == null || cell.vertices == null || cell.vertices.Count < 3)
        {
            meshFilter.mesh = new Mesh();
            return;
        }

        Color color = GetCellColor(cellIndex);
        meshFilter.mesh = CreatePolygonMesh(cell.vertices, color, randomPoints[cellIndex], cellIndex, previewOverlay: true);
    }

    public void RebuildVoronoiPreservePlaybackProgress()
    {
        fastPreviewMode = false;
        bisectorLineRenderer.positionCount = 0;
        connectionLineRenderer.positionCount = 0;

        if (randomPoints.Count == 0)
        {
            ClearVoronoiMeshes();
            return;
        }

        int savedCell = voronoiResult != null ? currentCellIdx : 0;
        int savedStep = voronoiResult != null ? currentStepIdx : -1;
        bool savedRest = voronoiResult != null && restNextStep;

        EnsureCellMeshes(randomPoints.Count);
        CreateBorderlines(randomPoints.Count);
        ClearAllCellMeshes();
        ClearAllBorderlines();
        meshFilter.mesh = new Mesh();
        if (lineRenderer != null) lineRenderer.positionCount = 0;

        voronoiResult = new Voronoi(new List<Vector2>(randomPoints), mapWidth, mapHeight);
        AssignGraphColors(voronoiResult.neighborLists);

        int n = voronoiResult.pts.Count;
        if (n == 0) return;

        if (savedRest && savedCell >= n)
        {
            for (int i = 0; i < n; i++)
                FinalizeCellMesh(i);
            currentCellIdx = savedCell;
            currentStepIdx = savedStep;
            restNextStep = true;
            return;
        }

        savedCell = Mathf.Clamp(savedCell, 0, n - 1);
        restNextStep = savedRest;

        for (int c = 0; c < savedCell; c++)
            FinalizeCellMesh(c);

        currentCellIdx = savedCell;
        currentStepIdx = savedStep;
        UpdateVisualization();
    }

    public void RefreshCellFillAtCurrentProgress()
    {
        if (randomPoints.Count == 0) return;

        if (voronoiResult == null)
        {
            RebuildVoronoiStepMode();
            return;
        }

        if (fastPreviewMode)
        {
            RebuildVoronoiFastPreviewAtProgress();
            return;
        }

        bisectorLineRenderer.positionCount = 0;
        connectionLineRenderer.positionCount = 0;

        int n = voronoiResult.pts.Count;
        if (n == 0) return;

        if (restNextStep && currentCellIdx >= n)
        {
            ClearAllCellMeshes();
            ClearAllBorderlines();
            meshFilter.mesh = new Mesh();
            if (lineRenderer != null) lineRenderer.positionCount = 0;
            for (int i = 0; i < n; i++)
                FinalizeCellMesh(i);
            return;
        }

        int cell = Mathf.Clamp(currentCellIdx, 0, n - 1);
        ClearCellMeshesFrom(0);
        ClearBorderlineFrom(0);
        for (int c = 0; c < cell; c++)
            FinalizeCellMesh(c);

        UpdateVisualization();
    }

    void FinalizeCellMesh(int cellIndex)
    {
        if (voronoiResult == null || cellIndex < 0 || cellIndex >= meshs.Count) return;

        Polygon poly = null;
        List<CutStep> steps = voronoiResult.stepLists[cellIndex];
        if (steps != null && steps.Count > 0)
            poly = steps[steps.Count - 1].currentPolygon;
        else if (cellIndex < voronoiResult.cells.Count)
            poly = voronoiResult.cells[cellIndex];

        if (poly == null || poly.vertices == null || poly.vertices.Count < 3)
        {
            ClearCellMesh(cellIndex);
            ClearBorderline(cellIndex);
            return;
        }

        Color color = GetCellColor(cellIndex);
        meshs[cellIndex].GetComponent<MeshFilter>().mesh =
            CreatePolygonMesh(poly.vertices, color, randomPoints[cellIndex], cellIndex);
        DrawBorderline(poly.vertices, cellIndex);
        CopyBorderline(cellIndex);
        if (lineRenderer != null) lineRenderer.positionCount = 0;
    }

    void GenerateNewVoronoi()
    {
        seedpointVisibility=true;
        SeedpointVisibilityToggle(true);
        RebuildVoronoiStepMode();
    }

    void EnsureCellMeshes(int count)
    {
        pointCount = Mathf.Max(count, 1);
        while (meshs.Count < pointCount)
        {
            int i = meshs.Count;
            GameObject newmesh = new GameObject($"CellMesh_{i}");
            newmesh.transform.SetParent(transform);
            newmesh.layer = gameObject.layer;
            newmesh.AddComponent<MeshFilter>();
            MeshRenderer mr = newmesh.AddComponent<MeshRenderer>();
            mr.material = meshMaterial != null ? meshMaterial : new Material(Shader.Find("Sprites/Default"));
            mr.sortingOrder = i;
            meshs.Add(newmesh);
        }
    }

    void CreateBorderlines(int count)
    {
        pointCount = Mathf.Max(count, 1);
        while (borderlines.Count < pointCount)
        {
            GameObject newBorder = new GameObject($"CellBorder_{borderlines.Count}");
            newBorder.layer = gameObject.layer;
            newBorder.transform.SetParent(transform);
            int borderIndex = borderlines.Count;
            LineRenderer l = newBorder.AddComponent<LineRenderer>();
            l.material = borderMaterial != null ? borderMaterial : new Material(Shader.Find("Sprites/Default"));
            l.startColor = l.endColor = borderlineColor;
            l.startWidth = l.endWidth = borderWidth;
            l.sortingOrder = borderIndex;
            borderlines.Add(l);
        }
    }

    void ClearAllBorderlines()
    {
        if (lineRenderer != null) lineRenderer.positionCount = 0;
        foreach (LineRenderer l in borderlines)
            if (l != null) l.positionCount = 0;
    }

    void ClearBorderline(int index)
    {
        if (index < 0 || index >= borderlines.Count || borderlines[index] == null) return;
        borderlines[index].positionCount = 0;
    }

    void ClearBorderlineFrom(int fromIndex)
    {
        for (int i = fromIndex; i < borderlines.Count; i++)
            ClearBorderline(i);
    }

    void DrawBorderline(List<Vector2> vertices, int cellIndex)
    {
        if (lineRenderer == null || vertices == null || vertices.Count < 3) return;
        float z = GetCellBorderZ(cellIndex);
        lineRenderer.positionCount = vertices.Count + 1;
        for (int i = 0; i < vertices.Count; i++)
            lineRenderer.SetPosition(i, new Vector3(vertices[i].x, vertices[i].y, z));
        lineRenderer.SetPosition(vertices.Count, new Vector3(vertices[0].x, vertices[0].y, z));
    }

    void CopyBorderline(int index)
    {
        if (lineRenderer == null || index < 0 || index >= borderlines.Count || borderlines[index] == null) return;
        borderlines[index].positionCount = lineRenderer.positionCount;
        for (int i = 0; i < lineRenderer.positionCount; i++)
        {
            Vector3 pos = lineRenderer.GetPosition(i);
            borderlines[index].SetPosition(i, new Vector3(pos.x, pos.y, -0.005f));
        }
    }

    void ClearVoronoiMeshes()
    {
        voronoiResult = null;
        cellColors.Clear();
        cellPaletteIndices.Clear();
        ClearAllCellMeshes();
        ClearAllBorderlines();
        if (bisectorLineRenderer != null) bisectorLineRenderer.positionCount = 0;
        if (connectionLineRenderer != null) connectionLineRenderer.positionCount = 0;
    }

    void ClearAllCellMeshes()
    {
        meshFilter.mesh = new Mesh();
        foreach (GameObject go in meshs)
        {
            if (go != null) go.GetComponent<MeshFilter>().mesh = new Mesh();
        }
    }

    void AssignGraphColors(List<Polygon> cells)
    {
        List<List<int>> adj = new List<List<int>>();
        for (int i = 0; i < randomPoints.Count; i++)
        {
            List<int> neighbors = new List<int>();
            if (i < cells.Count && cells[i] != null && cells[i].edges != null)
            {
                foreach (Line l in cells[i].edges)
                {
                    if (l.id != -1) neighbors.Add(l.id);
                }
            }
            adj.Add(neighbors);
        }
        AssignGraphColors(adj);
    }

    void AssignGraphColors(List<List<int>> adj)
    {
        while (cellColors.Count < randomPoints.Count) cellColors.Add(Color.clear);
        if (cellColors.Count > randomPoints.Count) cellColors.RemoveRange(randomPoints.Count, cellColors.Count - randomPoints.Count);

        while (cellPaletteIndices.Count < randomPoints.Count) cellPaletteIndices.Add(-1);
        if (cellPaletteIndices.Count > randomPoints.Count) cellPaletteIndices.RemoveRange(randomPoints.Count, cellPaletteIndices.Count - randomPoints.Count);

        Color[] Palette = new Color[] {
            new Color(1f, 0.2f, 0.3f, 0.8f),   // Neon Red
            new Color(0.2f, 1f, 0.4f, 0.8f),   // Neon Green
            new Color(0.2f, 0.6f, 1f, 0.8f),   // Neon Blue
            new Color(1f, 0.9f, 0.2f, 0.8f),   // Neon Yellow
            new Color(0.8f, 0.2f, 1f, 0.8f),   // Neon Purple
            new Color(0.6f, 0.6f, 0.6f, 0.8f)   // Neon Gray
        };

        int[] globalUsage = new int[Palette.Length];

        // 計算目前已被保留顏色的全域使用率
        for (int i = 0; i < randomPoints.Count; i++)
        {
            if (cellPaletteIndices[i] != -1)
            {
                globalUsage[cellPaletteIndices[i]]++;
            }
        }

        for (int i = 0; i < randomPoints.Count; i++)
        {
            List<int> neighbors = i < adj.Count ? adj[i] : new List<int>();
            bool[] used = new bool[Palette.Length];
            
            foreach (int n in neighbors)
            {
                if (n != i && n < cellPaletteIndices.Count && cellPaletteIndices[n] != -1)
                {
                    used[cellPaletteIndices[n]] = true;
                }
            }

            bool keptExisting = false;
            if (cellPaletteIndices[i] != -1)
            {
                int currentP = cellPaletteIndices[i];
                if (!used[currentP]) 
                {
                    keptExisting = true;
                }
            }

            if (keptExisting) continue;

            // 如果舊顏色衝突了，需要將原本的 globalUsage 減掉，因為它即將換顏色
            if (cellPaletteIndices[i] != -1)
            {
                globalUsage[cellPaletteIndices[i]] = Mathf.Max(0, globalUsage[cellPaletteIndices[i]] - 1);
            }

            int chosenP = -1;
            int minUsage = int.MaxValue;
            int startIdx = UnityEngine.Random.Range(0, Palette.Length);
            
            for (int offset = 0; offset < Palette.Length; offset++)
            {
                int p = (startIdx + offset) % Palette.Length;
                if (!used[p]) 
                { 
                    if (globalUsage[p] < minUsage)
                    {
                        minUsage = globalUsage[p];
                        chosenP = p;
                    }
                }
            }

            // 平面圖四色定理保證通常不會全滿，但萬一真的被包圍，強制選一個最少用的，無視 used
            if (chosenP == -1) 
            {
                minUsage = int.MaxValue;
                for (int p = 0; p < Palette.Length; p++)
                {
                    if (globalUsage[p] < minUsage)
                    {
                        minUsage = globalUsage[p];
                        chosenP = p;
                    }
                }
            }

            globalUsage[chosenP]++;
            cellPaletteIndices[i] = chosenP;

            float h, s, v;
            Color.RGBToHSV(Palette[chosenP], out h, out s, out v);
            h = Mathf.Repeat(h + UnityEngine.Random.Range(-0.03f, 0.03f), 1f);
            Color finalColor = Color.HSVToRGB(h, s, v);
            finalColor.a = 0.8f;
            cellColors[i] = finalColor;
        }
    }

    Color GetCellColor(int cellIndex)
    {
        return cellIndex < cellColors.Count ? cellColors[cellIndex] : Color.white;
    }

    void RefreshSeedPointMarkers()
    {
        if(seedpointParent==null){
            seedpointParent=new GameObject("SeedpointParent");
            seedpointParent.transform.SetParent(transform);
        }
        while (seedPointMarkers.Count < randomPoints.Count)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"SeedPoint_{seedPointMarkers.Count}";
            marker.transform.SetParent(seedpointParent.transform);
            marker.layer = gameObject.layer;
            Destroy(marker.GetComponent<Collider>());
            MeshRenderer mr = marker.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default")) { color = seedPointColor };
            mr.sortingOrder = SeedPointSortingOrderBase;
            seedPointMarkers.Add(marker);
        }
        while (seedPointMarkers.Count > randomPoints.Count)
        {
            int last = seedPointMarkers.Count - 1;
            Destroy(seedPointMarkers[last]);
            seedPointMarkers.RemoveAt(last);
        }
        float seedZ = GetSeedPointZ(randomPoints.Count);
        for (int i = 0; i < randomPoints.Count; i++)
        {
            Vector2 p = randomPoints[i];
            Transform t = seedPointMarkers[i].transform;
            t.localPosition = new Vector3(p.x, p.y, seedZ);
            t.localScale = Vector3.one * seedPointRadius * 2f;
            MeshRenderer mr = seedPointMarkers[i].GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = SeedPointSortingOrderBase + i;
        }
    }

    void SeedpointVisibilityToggle(bool on){
        if(on)seedpointParent.SetActive(true);
        else seedpointParent.SetActive(false);
    }

    void SetupComponents()
    {
        meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

        if (meshMaterial == null)
            meshMaterial = new Material(Shader.Find("Sprites/Default"));
        meshRenderer.material = meshMaterial;

        lineRenderer = gameObject.GetComponent<LineRenderer>();
        if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
        lineRenderer.sortingOrder=150;
        lineRenderer.startColor = lineRenderer.endColor = currentCellBorderColor;
        lineRenderer.startWidth = lineRenderer.endWidth = currentBorderWidth;
        if (borderMaterial == null) borderMaterial = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material = borderMaterial;

        int layer = gameObject.layer;
        GameObject bObj = new GameObject("BisectorLine");
        bObj.transform.SetParent(transform);
        bObj.layer = layer;
        bisectorLineRenderer = bObj.AddComponent<LineRenderer>();
        bisectorLineRenderer.sortingOrder=151;
        ConfigureLineRenderer(bisectorLineRenderer, Color.red, 0.08f);

        GameObject cObj = new GameObject("ConnectionLine");
        cObj.transform.SetParent(transform);
        cObj.layer = layer;
        connectionLineRenderer = cObj.AddComponent<LineRenderer>();
        connectionLineRenderer.sortingOrder=152;
        ConfigureLineRenderer(connectionLineRenderer, Color.yellow, 0.04f);
    }

    void ConfigureLineRenderer(LineRenderer lr, Color color, float width)
    {
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.positionCount = 0;
        lr.material = new Material(Shader.Find("Sprites/Default"));
    }

    bool NextStep()
    {
        if (voronoiResult == null)
        {
            RebuildVoronoiStepMode();
            return false;
        }
        currentStepIdx++;
        if (restNextStep)
        {
            restNextStep = false;
            currentCellIdx = 0;
            currentStepIdx = -1;
            ClearCellMeshesFrom(0);
            ClearBorderlineFrom(0);
        }
        if (currentStepIdx >= voronoiResult.stepLists[currentCellIdx].Count)
        {
            currentStepIdx = -2;
            currentCellIdx++;
            if (currentCellIdx >= voronoiResult.pts.Count)
            {
                bisectorLineRenderer.positionCount = 0;
                connectionLineRenderer.positionCount = 0;
                ShowFinalVoronoiState();
                restNextStep = true;
                return true;
            }
        }
        UpdateVisualization();
        return false;
    }

    void PreviousStep()
    {
        if (voronoiResult == null)
        {
            RebuildVoronoiStepMode();
            return;
        }

        if (restNextStep)
        {
            restNextStep = false;
            currentCellIdx = voronoiResult.pts.Count - 1;
            currentStepIdx = LastCutStepIndex(currentCellIdx);
            UpdateVisualization();
            return;
        }

        if (currentStepIdx == -1)
        {
            if (currentCellIdx == 0){
                ShowFinalVoronoiState();
                return;
            }
            currentCellIdx--;
            currentStepIdx = LastCutStepIndex(currentCellIdx);
            ClearCellMeshesFrom(currentCellIdx + 1);
            ClearBorderlineFrom(currentCellIdx + 1);
            UpdateVisualization();
            return;
        }

        if (currentStepIdx == -2)
        {
            currentCellIdx = Mathf.Max(0, currentCellIdx - 1);
            currentStepIdx = LastCutStepIndex(currentCellIdx);
            ClearCellMeshesFrom(currentCellIdx + 1);
            ClearBorderlineFrom(currentCellIdx + 1);
            UpdateVisualization();
            return;
        }

        if (currentStepIdx == 0)
        {
            currentStepIdx = -1;
            ClearCellMesh(currentCellIdx);
            ClearBorderline(currentCellIdx);
            UpdateVisualization();
            return;
        }

        if (currentStepIdx == voronoiResult.stepLists[currentCellIdx].Count - 1)
        {
            ClearCellMesh(currentCellIdx);
            ClearBorderline(currentCellIdx);
        }
        currentStepIdx--;
        UpdateVisualization();
    }

    int LastCutStepIndex(int cellIndex)
    {
        int count = voronoiResult.stepLists[cellIndex].Count;
        return count > 0 ? count - 1 : -1;
    }

    void ClearCellMesh(int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= meshs.Count || meshs[cellIndex] == null) return;
        meshs[cellIndex].GetComponent<MeshFilter>().mesh = new Mesh();
    }

    void ClearCellMeshesFrom(int fromCellIndex)
    {
        for (int i = fromCellIndex; i < meshs.Count; i++)
            ClearCellMesh(i);
    }

    void UpdateVisualization()
    {
        if (voronoiResult == null || voronoiResult.pts.Count == 0) return;
        Polygon polyToDraw = null;
        CutStep currentStep = null;
        List<CutStep> steps = voronoiResult.stepLists[currentCellIdx];
        if (currentStepIdx == -2)
        {
            Debug.Log("Just finished cell " + (currentCellIdx - 1) + ". Let me rest a lil bit.");
        }
        else if (currentStepIdx == -1)
        {
            Vector2 b1 = new Vector2(-mapWidth, -mapHeight);
            Vector2 b2 = new Vector2(mapWidth, -mapHeight);
            Vector2 b3 = new Vector2(mapWidth, mapHeight);
            Vector2 b4 = new Vector2(-mapWidth, mapHeight);
            polyToDraw = new Polygon(new List<Line> {
                new Line(b1, b2), new Line(b2, b3), new Line(b3, b4), new Line(b4, b1)
            });
        }
        else if (steps != null && currentStepIdx < steps.Count)
        {
            currentStep = steps[currentStepIdx];
            polyToDraw = currentStep.currentPolygon;
        }

        if (polyToDraw != null && polyToDraw.vertices != null && polyToDraw.vertices.Count >= 3)
        {
            Color stepColor = GetCellColor(currentCellIdx);
            Vector2 seedPt = voronoiResult.pts[currentCellIdx];
            bool commitFinalStep = currentStepIdx >= 0 && steps != null && currentStepIdx == steps.Count - 1;
            Mesh previewMesh = CreatePolygonMesh(
                polyToDraw.vertices, stepColor, seedPt, currentCellIdx, previewOverlay: !commitFinalStep);
            DrawBorderline(polyToDraw.vertices, currentCellIdx);
            if (commitFinalStep)
            {
                meshs[currentCellIdx].GetComponent<MeshFilter>().mesh = previewMesh;
                meshFilter.mesh = new Mesh();
                
                CopyBorderline(currentCellIdx);
                if (lineRenderer != null) lineRenderer.positionCount = 0;
            }
            else
            {
                meshFilter.mesh = previewMesh;
                meshRenderer.sortingOrder = currentCellIdx;
            }
        }
        else
        {
            meshFilter.mesh = new Mesh();
            if (lineRenderer != null) lineRenderer.positionCount = 0;
        }

        UpdateGuideLines(currentStep);
    }

    void ClearGuideLines()
    {
        if (bisectorLineRenderer != null) bisectorLineRenderer.positionCount = 0;
        if (connectionLineRenderer != null) connectionLineRenderer.positionCount = 0;
    }

    void UpdateGuideLines(CutStep currentStep)
    {
        if (!showGuideLines || currentStep == null || currentStepIdx == -2)
        {
            ClearGuideLines();
            return;
        }

        Vector2 pI = voronoiResult.pts[currentCellIdx];
        Vector2 pJ = voronoiResult.pts[currentStep.cellIndex];
        float guideZ = GetCellGuideLineZ(currentCellIdx);
        connectionLineRenderer.positionCount = 2;
        connectionLineRenderer.SetPosition(0, new Vector3(pI.x, pI.y, guideZ));
        connectionLineRenderer.SetPosition(1, new Vector3(pJ.x, pJ.y, guideZ));
        Line cut = currentStep.cutterLine;
        Vector2 dir = (cut.t - cut.s).normalized;
        Vector2 p1 = cut.s - dir * 50, p2 = cut.s + dir * 50;
        bisectorLineRenderer.positionCount = 2;
        bisectorLineRenderer.SetPosition(0, new Vector3(p1.x, p1.y, guideZ));
        bisectorLineRenderer.SetPosition(1, new Vector3(p2.x, p2.y, guideZ));
    }

    Mesh CreatePolygonMesh(
        List<Vector2> vertices,
        Color fillColor,
        Vector2 seedPoint,
        int cellIndex,
        bool previewOverlay = false)
    {
        if (vertices == null || vertices.Count < 3)
            return new Mesh();

        float z = previewOverlay ? GetCellPreviewZ(cellIndex) : GetCellMeshZ(cellIndex);
        switch (cellFillMode)
        {
            case CellFillMode.Wireframe:
                return CreateSolidMesh(vertices, WireframeFillColor, z);
            case CellFillMode.Solid:
                return CreateSolidMesh(vertices, fillColor, z);
            default:
                return CreateRadialMesh(vertices, fillColor, seedPoint, z);
        }
    }

    static Mesh CreateSolidMesh(List<Vector2> vertices, Color fillColor, float z)
    {
        Mesh mesh = new Mesh();
        Vector3[] v3 = new Vector3[vertices.Count];
        Color[] colors = new Color[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            v3[i] = new Vector3(vertices[i].x, vertices[i].y, z);
            colors[i] = fillColor;
        }

        List<int> triangles = new List<int>();
        for (int i = 1; i < vertices.Count - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }

        mesh.vertices = v3;
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors;
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh CreateRadialMesh(List<Vector2> vertices, Color centerColor, Vector2 seedPoint, float z)
    {
        Mesh mesh = new Mesh();
        Vector3[] v3 = new Vector3[vertices.Count + 1];
        Color[] colors = new Color[vertices.Count + 1];

        v3[0] = new Vector3(seedPoint.x, seedPoint.y, z);
        colors[0] = centerColor;

        for (int i = 0; i < vertices.Count; i++)
        {
            v3[i + 1] = new Vector3(vertices[i].x, vertices[i].y, z);
            colors[i + 1] = Color.black;
        }

        List<int> triangles = new List<int>();
        for (int i = 0; i < vertices.Count; i++)
        {
            triangles.Add(0);
            triangles.Add(i + 1);
            int nextIdx = (i + 1) % vertices.Count + 1;
            triangles.Add(nextIdx);
        }

        mesh.vertices = v3;
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors;
        mesh.RecalculateBounds();
        return mesh;
    }
}

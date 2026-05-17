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
    public Color currentCellBorderColor=Color.black;
    public float borderWidth = 0.05f;
    public Material borderMaterial;

    private LineRenderer bisectorLineRenderer;
    private LineRenderer connectionLineRenderer;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private LineRenderer lineRenderer;

    private Voronoi voronoiResult;
    private List<Vector2> randomPoints = new List<Vector2>();
    private List<GameObject> seedPointMarkers = new List<GameObject>();
    private List<GameObject> meshs = new List<GameObject>();
    private readonly List<Color> cellColors = new List<Color>();
    private List<LineRenderer> borderlines = new();

    private int currentCellIdx = 0;
    private int currentStepIdx = -1;
    private bool restNextStep = false;
    private bool fastPreviewMode = false;

    public IReadOnlyList<Vector2> SeedPoints => randomPoints;
    public bool AllowsStepKeyboard => !fastPreviewMode;

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
    }

    public void StepForward() => NextStep();
    public void StepBackward() => PreviousStep();

    public void ClearSeedPoints()
    {
        randomPoints.Clear();
        ClearVoronoiMeshes();
        RefreshSeedPointMarkers();
    }

    public void SetRandomSeedPoints(int count)
    {
        randomPoints.Clear();
        for (int i = 0; i < count; i++)
            randomPoints.Add(geofunc.random_point(mapWidth, mapHeight));
        EnsureCellMeshes(Mathf.Max(randomPoints.Count, 1));
        CreateBorderlines(Mathf.Max(randomPoints.Count,1));
        RefreshSeedPointMarkers();
    }

    public bool TryAddSeedPoint(Vector2 point, float minSeparation = 0.5f)
    {
        if (FindNearestPointIndex(point, minSeparation) >= 0) return false;
        randomPoints.Add(point);
        EnsureCellMeshes(Mathf.Max(randomPoints.Count, meshs.Count));
        CreateBorderlines(Mathf.Max(randomPoints.Count,borderlines.Count));
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
        t.localPosition = new Vector3(point.x, point.y, -0.05f);
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
        EnsureCellColors(randomPoints.Count);
        ClearAllCellMeshes();
        ClearAllBorderlines();
        voronoiResult = new Voronoi(new List<Vector2>(randomPoints), mapWidth, mapHeight);
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
        lineRenderer.positionCount=0;

        if (randomPoints.Count == 0)
        {
            ClearVoronoiMeshes();
            return;
        }

        EnsureCellMeshes(randomPoints.Count);
        EnsureCellColors(randomPoints.Count);
        CreateBorderlines(randomPoints.Count);
        List<Polygon> cells = Voronoi.ComputeCells(randomPoints, mapWidth, mapHeight);
        for (int i = 0; i < meshs.Count; i++)
        {
            MeshFilter mf = meshs[i].GetComponent<MeshFilter>();
            Color color = i < cellColors.Count ? cellColors[i] : polygonColor;
            if (i < cells.Count && cells[i] != null && cells[i].vertices.Count >= 3)
            {
                mf.mesh = CreatePolygonMesh(cells[i].vertices, color, randomPoints[i]);
                DrawBorderline(cells[i].vertices);
                CopyBorderline(i);
                lineRenderer.positionCount=0;
            }
            else{
                mf.mesh = new Mesh();
                borderlines[i].positionCount=0;
            }
                
        }
    }

    /// <summary>拖曳結束：沿用預覽 mesh，不重算 Voronoi（避免卡頓）。</summary>
    public void FinishDragDisplay()
    {
        fastPreviewMode = false;
        bisectorLineRenderer.positionCount = 0;
        connectionLineRenderer.positionCount = 0;
        meshFilter.mesh = new Mesh();
        lineRenderer.positionCount=0;
        voronoiResult = null;

        if (randomPoints.Count == 0) return;

        currentCellIdx = randomPoints.Count;
        currentStepIdx = -2;
        restNextStep = true;
    }

    void GenerateNewVoronoi()
    {
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
            meshs.Add(newmesh);
        }
    }

    void CreateBorderlines(int count){
        pointCount=Mathf.Max(count,1);
        while(borderlines.Count<pointCount){
            int i=borderlines.Count;
            GameObject newborder=new GameObject($"CellBorder_{i}");
            newborder.layer=gameObject.layer;
            newborder.transform.SetParent(transform);
            LineRenderer l=newborder.AddComponent<LineRenderer>();
            l.material=borderMaterial!=null?borderMaterial:new Material(Shader.Find("Sprites/Default"));
            l.startColor=l.endColor=borderlineColor;
            l.startWidth=l.endWidth=borderWidth;
            borderlines.Add(l);
        }
    }

    void ClearAllBorderlines(){
        lineRenderer.positionCount=0;
        foreach(LineRenderer l in borderlines) l.positionCount=0;
    }

    void ClearBorderline(int whichone){
        borderlines[whichone].positionCount=0;
    }

    void ClearBorderlineFrom(int from){
        for(int i=from;i<borderlines.Count;i++){
            borderlines[i].positionCount=0;
        }
    }

    void ClearVoronoiMeshes()
    {
        voronoiResult = null;
        cellColors.Clear();
        ClearAllCellMeshes();
        ClearAllBorderlines();
    }

    void ClearAllCellMeshes()
    {
        meshFilter.mesh = new Mesh();
        foreach (GameObject go in meshs)
        {
            if (go != null) go.GetComponent<MeshFilter>().mesh = new Mesh();
        }
    }

    void EnsureCellColors(int count)
    {
        while (cellColors.Count < count)
            cellColors.Add(NewCellColor());
        if (cellColors.Count > count)
            cellColors.RemoveRange(count, cellColors.Count - count);
    }

    Color GetCellColor(int cellIndex)
    {
        EnsureCellColors(cellIndex + 1);
        return cellColors[cellIndex];
    }

    static Color NewCellColor()
    {
        return new Color(
            UnityEngine.Random.Range(0.3f, 1f),
            UnityEngine.Random.Range(0.3f, 1f),
            UnityEngine.Random.Range(0.3f, 1f),
            0.8f);
    }

    void RefreshSeedPointMarkers()
    {
        while (seedPointMarkers.Count < randomPoints.Count)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"SeedPoint_{seedPointMarkers.Count}";
            marker.transform.SetParent(transform);
            marker.layer = gameObject.layer;
            Destroy(marker.GetComponent<Collider>());
            MeshRenderer mr = marker.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default")) { color = seedPointColor };
            seedPointMarkers.Add(marker);
        }
        while (seedPointMarkers.Count > randomPoints.Count)
        {
            int last = seedPointMarkers.Count - 1;
            Destroy(seedPointMarkers[last]);
            seedPointMarkers.RemoveAt(last);
        }
        for (int i = 0; i < randomPoints.Count; i++)
        {
            Vector2 p = randomPoints[i];
            Transform t = seedPointMarkers[i].transform;
            t.localPosition = new Vector3(p.x, p.y, -0.05f);
            t.localScale = Vector3.one * seedPointRadius * 2f;
        }
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

        lineRenderer=gameObject.GetComponent<LineRenderer>();
        if(lineRenderer==null) lineRenderer=gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount=0;
        lineRenderer.startColor=lineRenderer.endColor=currentCellBorderColor;
        lineRenderer.startWidth=lineRenderer.endWidth=borderWidth;
        if(borderMaterial==null) borderMaterial=new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material=borderMaterial;

        int layer = gameObject.layer;
        GameObject bObj = new GameObject("BisectorLine");
        bObj.transform.SetParent(transform);
        bObj.layer = layer;
        bisectorLineRenderer = bObj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(bisectorLineRenderer, Color.red, 0.08f);

        GameObject cObj = new GameObject("ConnectionLine");
        cObj.transform.SetParent(transform);
        cObj.layer = layer;
        connectionLineRenderer = cObj.AddComponent<LineRenderer>();
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

    void NextStep()
    {
        if (voronoiResult == null)
        {
            RebuildVoronoiStepMode();
            return;
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
                restNextStep = true;
                bisectorLineRenderer.positionCount = 0;
                connectionLineRenderer.positionCount = 0;
                return;
            }
        }
        UpdateVisualization();
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
            if (currentCellIdx == 0) return;
            currentCellIdx--;
            currentStepIdx = LastCutStepIndex(currentCellIdx);
            ClearCellMeshesFrom(currentCellIdx + 1);
            ClearBorderlineFrom(currentCellIdx+1);
            UpdateVisualization();
            return;
        }

        if (currentStepIdx == -2)
        {
            currentCellIdx = Mathf.Max(0, currentCellIdx - 1);
            currentStepIdx = LastCutStepIndex(currentCellIdx);
            ClearCellMeshesFrom(currentCellIdx + 1);
            ClearBorderlineFrom(currentCellIdx+1);
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

        if (currentStepIdx == voronoiResult.stepLists[currentCellIdx].Count - 1){
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
            Mesh previewMesh = CreatePolygonMesh(polyToDraw.vertices, stepColor, seedPt);
            if (currentStepIdx >= 0 && steps != null && currentStepIdx == steps.Count - 1)
            {
                meshs[currentCellIdx].GetComponent<MeshFilter>().mesh = previewMesh;
                meshFilter.mesh = new Mesh();
                CopyBorderline(currentCellIdx);
                lineRenderer.positionCount=0;
            }
            else
            {
                meshFilter.mesh = previewMesh;
            }
        }
        else
        {
            meshFilter.mesh = new Mesh();
            lineRenderer.positionCount=0;
        }

        if (currentStepIdx == -2)
        {
            bisectorLineRenderer.positionCount = 0;
            connectionLineRenderer.positionCount = 0;
        }
        if (currentStep != null)
        {
            Vector2 pI = voronoiResult.pts[currentCellIdx];
            Vector2 pJ = voronoiResult.pts[currentStep.cellIndex];
            connectionLineRenderer.positionCount = 2;
            connectionLineRenderer.SetPosition(0, new Vector3(pI.x, pI.y, -0.01f));
            connectionLineRenderer.SetPosition(1, new Vector3(pJ.x, pJ.y, -0.01f));
            Line cut = currentStep.cutterLine;
            Vector2 dir = (cut.t - cut.s).normalized;
            Vector2 p1 = cut.s - dir * 50, p2 = cut.s + dir * 50;
            bisectorLineRenderer.positionCount = 2;
            bisectorLineRenderer.SetPosition(0, new Vector3(p1.x, p1.y, -0.02f));
            bisectorLineRenderer.SetPosition(1, new Vector3(p2.x, p2.y, -0.02f));
        }
        else
        {
            bisectorLineRenderer.positionCount = 0;
            connectionLineRenderer.positionCount = 0;
        }
    }

    Mesh CreatePolygonMesh(List<Vector2> vertices, Color centerColor, Vector2 seedPoint)
    {
        Mesh mesh = new Mesh();
        Vector3[] v3 = new Vector3[vertices.Count + 1];
        Color[] colors = new Color[vertices.Count + 1];
        
        v3[0] = new Vector3(seedPoint.x, seedPoint.y, 0);
        colors[0] = centerColor;

        for (int i = 0; i < vertices.Count; i++)
        {
            v3[i + 1] = new Vector3(vertices[i].x, vertices[i].y, 0);
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

    void DrawBorderline(List<Vector2> vertices){
        lineRenderer.positionCount=vertices.Count+1;
        for(int i=0;i<vertices.Count;i++){
            lineRenderer.SetPosition(i,new Vector3(vertices[i].x,vertices[i].y,-0.01f));
        }
        lineRenderer.SetPosition(vertices.Count,new Vector3(vertices[0].x,vertices[0].y,-0.01f));
    }
    void CopyBorderline(int idx){
        borderlines[idx].positionCount=lineRenderer.positionCount;
        for(int i=0;i<lineRenderer.positionCount;i++){
            Vector3 pos=lineRenderer.GetPosition(i);
            borderlines[idx].SetPosition(i,new Vector3(pos.x,pos.y,-0.005f));
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using Unity.VisualScripting;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CreatePolygon : MonoBehaviour
{
    [Header("地圖設定")]
    public float mapWidth = 15f;
    public float mapHeight = 10.8f;
    public int pointCount = 5;

    [Header("顏色與材質設定")]
    public Material meshMaterial;
    public Color polygonColor = new Color(0f, 0.8f, 1f, 0.8f);

    private LineRenderer bisectorLineRenderer;
    private LineRenderer connectionLineRenderer;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private Voronoi voronoiResult;
    private List<Vector2> randomPoints = new List<Vector2>();

    private int currentCellIdx = 0;
    private int currentStepIdx = -1;
    private List<GameObject> meshs=new();

    void Start()
    {
        createMeshs();
        SetupComponents();
        GenerateNewVoronoi();
        
        
    }

    void createMeshs(){
        int layer = gameObject.layer;
        for(int i=0;i<pointCount;i++){
            GameObject newmesh=new GameObject($"CellMesh_{i}");
            newmesh.transform.SetParent(transform);
            newmesh.layer = layer;
            newmesh.AddComponent<MeshFilter>();
            newmesh.AddComponent<MeshRenderer>();
            newmesh.GetComponent<MeshRenderer>().material=new Material(Shader.Find("Sprites/Default"));
            meshs.Add(newmesh);
        }
    }

    void Update()
    {
       if (Keyboard.current.rightArrowKey.wasPressedThisFrame){
           NextStep();
       }
       if (Keyboard.current.rKey.wasPressedThisFrame){
           GenerateNewVoronoi();
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

        int layer = gameObject.layer;
        GameObject bObj = new GameObject("BisectorLine");
        bObj.transform.SetParent(this.transform);
        bObj.layer = layer;
        bisectorLineRenderer = bObj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(bisectorLineRenderer, Color.red, 0.08f);

        GameObject cObj = new GameObject("ConnectionLine");
        cObj.transform.SetParent(this.transform);
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

    void GenerateNewVoronoi(){
        randomPoints.Clear();
        for(int i=0;i<pointCount;i++){
            randomPoints.Add(geofunc.random_point(mapWidth,mapHeight));
            meshs[i].GetComponent<MeshFilter>().mesh=new Mesh();
        }
        voronoiResult = new Voronoi(randomPoints, mapWidth, mapHeight);
        currentCellIdx=0;
        currentStepIdx=-1;
        UpdateVisualization();
    }

    bool restNextStep=false;
    void NextStep(){
        if(voronoiResult==null)return;
        currentStepIdx++;
        if(restNextStep){
            restNextStep=false;
            currentCellIdx=0;
            currentStepIdx=-1;
            for(int i=0;i<voronoiResult.pts.Count;i++){
                meshs[i].GetComponent<MeshFilter>().mesh=new Mesh();
            }
        }
        if(currentStepIdx>=voronoiResult.stepLists[currentCellIdx].Count){
            currentStepIdx=-2;
            currentCellIdx++;
            if(currentCellIdx>=voronoiResult.pts.Count){
                restNextStep=true;
                bisectorLineRenderer.positionCount=0;
                connectionLineRenderer.positionCount=0;
                return;
            }
        }
        UpdateVisualization();
    }

    void UpdateVisualization(){
        if (voronoiResult==null||voronoiResult.pts.Count==0) return;
        Polygon polyToDraw=null;
        CutStep currentStep=null;
        List<CutStep> steps=voronoiResult.stepLists[currentCellIdx];
        if(currentStepIdx==-2){
            Debug.Log("Just finished cell "+(currentCellIdx-1)+". Let me rest a lil bit.");
        }
        else if(currentStepIdx==-1){
            polygonColor = new Color(UnityEngine.Random.Range(.3f,1f), UnityEngine.Random.Range(.3f,1f), UnityEngine.Random.Range(.3f,1f), 0.8f);
            Vector2 b1 = new Vector2(-mapWidth, -mapHeight);
            Vector2 b2 = new Vector2(mapWidth, -mapHeight);
            Vector2 b3 = new Vector2(mapWidth, mapHeight);
            Vector2 b4 = new Vector2(-mapWidth, mapHeight);
            polyToDraw = new Polygon(new List<Line>{new Line(b1,b2),new Line(b2,b3),new Line(b3,b4),new Line(b4,b1)});
        }
        else if (steps!=null&&currentStepIdx<steps.Count){
            currentStep=steps[currentStepIdx];
            polyToDraw=currentStep.currentPolygon;
        }
        if (polyToDraw!=null&&polyToDraw.vertices!=null&&polyToDraw.vertices.Count>=3){
            if(currentStepIdx==steps.Count-1){
                meshs[currentCellIdx].GetComponent<MeshFilter>().mesh=CreatePolygonMesh(polyToDraw.vertices);
                meshFilter.mesh=new Mesh();
            }
            else meshFilter.mesh=CreatePolygonMesh(polyToDraw.vertices);
        }
        else{
            meshFilter.mesh=null;
        }
        if(currentStepIdx==-2){
            bisectorLineRenderer.positionCount=0;
            connectionLineRenderer.positionCount=0;
        }
        if (currentStep!=null){
            Vector2 pI=voronoiResult.pts[currentCellIdx],pJ=voronoiResult.pts[currentStep.cellIndex];
            connectionLineRenderer.positionCount=2;
            connectionLineRenderer.SetPosition(0,new Vector3(pI.x,pI.y,-0.01f));
            connectionLineRenderer.SetPosition(1,new Vector3(pJ.x,pJ.y,-0.01f));
            Line cut=currentStep.cutterLine;
            Vector2 dir=(cut.t-cut.s).normalized;
            Vector2 p1=cut.s-dir*50,p2=cut.s+dir*50;
            bisectorLineRenderer.positionCount=2;
            bisectorLineRenderer.SetPosition(0,new Vector3(p1.x,p1.y,-0.02f));
            bisectorLineRenderer.SetPosition(1,new Vector3(p2.x,p2.y,-0.02f));
        }
        else{
            bisectorLineRenderer.positionCount=0;
            connectionLineRenderer.positionCount=0;
        }
    }
    Mesh CreatePolygonMesh(List<Vector2> vertices)
    {
        Mesh mesh=new Mesh();
        Vector3[] v3=new Vector3[vertices.Count];
        Color[] colors=new Color[vertices.Count];
        for (int i=0;i<vertices.Count;i++){
            v3[i]=new Vector3(vertices[i].x,vertices[i].y, 0);
            colors[i]=polygonColor;
        }
        List<int> triangles=new List<int>();
        for (int i=1;i<vertices.Count-1;i++){
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i+1);
        }

        mesh.vertices=v3;
        mesh.triangles=triangles.ToArray();
        mesh.colors=colors;
        mesh.RecalculateBounds();
        return mesh;
    }
}
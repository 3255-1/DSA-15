using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class main : MonoBehaviour{
    Voronoi vo;    
    void Start(){
        List<Vector2> v = new List<Vector2> {
            new Vector2(1, 1),
            new Vector2(1, 2),
            new Vector2(2, 1)
        };
        Voronoi vo = new Voronoi(v, 3, 3);
        int ptsz = vo.pts.Count;
        for(int c = 0; c < ptsz; c++){
            if(vo.cells[c] == null)continue;
            Polygon p = vo.cells[c];
            int sz = p.vertices.Count;
            for(int i = 0; i < sz; i++){
                Debug.Log(c+": "+p.vertices[i]);
            }
        }
    }

}
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

public class Line{
    public Vector2 s, t;// s->t
    public Vector2 f, m;
    public float angle;
    public Line(Vector2 s, Vector2 t){// calculate bisector
        this.s = s;
        this.t = t;
        this.angle = Mathf.Atan2(t.y - s.y, t.x - s.x);
    }
}

public class geofunc{
    public static Vector2 random_point(float mx, float my){
        System.Random random = new System.Random();
        Vector2 ans = new Vector2((float)random.NextDouble()*mx, (float)random.NextDouble()*my);
        return ans;
    }
    public static Line bisector(Vector2 f, Vector2 m){
        Vector2 s = (f + m)/2f, d = Vector2.Perpendicular(m - f), t = s + d;
        Line ans = new Line(s,t);
        ans.f = f;
        ans.m = m;
        return ans;
    }
    public static float Cross(Vector2 a, Vector2 b) {
        return a.x * b.y - a.y * b.x;
    }

    public static bool IsRight(Line l, Vector2 p) {
        return Cross(l.t - l.s, p - l.s) < -1e-6f;
    }

    public static Vector2 Lineintersect(Line l1, Line l2){
        Vector2 l1_p1 = l1.s, l1_p2 = l1.t, l2_p1 = l2.s, l2_p2 = l2.t; 
        float denominator = (l2_p2.y - l2_p1.y) * (l1_p2.x - l1_p1.x) - (l2_p2.x - l2_p1.x) * (l1_p2.y - l1_p1.y);

        float u_a = ((l2_p2.x - l2_p1.x) * (l1_p1.y - l2_p1.y) - (l2_p2.y - l2_p1.y) * (l1_p1.x - l2_p1.x)) / denominator;

        Vector2 ans = l1_p1 + u_a * (l1_p2 - l1_p1);

        return ans;
    }

    public static List<Line> hp_intersect(List<Line> lines){
        lines = lines.OrderBy(l => l.angle).ToList();
        List<Line> uLines = new List<Line>();
        for(int i = 0; i < lines.Count; i++) {
            Debug.Log(i+" angle:"+lines[i].angle);
            if (i > 0 && Mathf.Abs(lines[i].angle - lines[i - 1].angle) < 1e-6f) {
                if (IsRight(lines[i], uLines[uLines.Count - 1].s)) {
                    uLines[uLines.Count - 1] = lines[i];
                }
                continue;
            }
            uLines.Add(lines[i]);
        }

        LinkedList<Line> deq = new LinkedList<Line>();

        for(int i = 0; i < uLines.Count; i++) {
            Line l = uLines[i];
            while(deq.Count > 1 && IsRight(l, Lineintersect(deq.Last.Value, deq.Last.Previous.Value))){
                deq.RemoveLast();
            }
            while(deq.Count > 1 && IsRight(l, Lineintersect(deq.First.Value, deq.First.Next.Value))){
                deq.RemoveFirst();
            }
            deq.AddLast(l);
        }
        while(deq.Count > 2 && IsRight(deq.First.Value, Lineintersect(deq.Last.Value, deq.Last.Previous.Value))) {
            deq.RemoveLast();
        }
        while(deq.Count > 2 && IsRight(deq.Last.Value, Lineintersect(deq.First.Value, deq.First.Next.Value))) {
            deq.RemoveFirst();
        }

        if(deq.Count < 3) return new List<Line>();
        return deq.ToList();
    }
}

public class Polygon{
    public List<Vector2> vertices;
    public List<Line> edges;

    public Polygon(List<Line> lines) {
        this.edges = lines;
        this.vertices = new List<Vector2>();
        int sz = lines.Count;
        for(int i = 0; i < sz; i++){
            Line now = lines[i], next = lines[(i+1) % sz];
            Vector2 pt = geofunc.Lineintersect(now, next);
            vertices.Add(pt);
        }
    }
}

public class Voronoi{
    public List<Vector2> pts;
    public List<Polygon> cells;

    public Voronoi(List<Vector2> pts, float mx, float my) {
        this.pts = pts;
        this.cells = new List<Polygon>();
        int sz = pts.Count;
        Debug.Log("sz: "+sz);

        Vector2 b1 = new Vector2(0, 0);
        Vector2 b2 = new Vector2(mx, 0);
        Vector2 b3 = new Vector2(mx, my);
        Vector2 b4 = new Vector2(0, my);

        List<Line> boundingBox = new List<Line> {
            new Line(b1, b2),
            new Line(b2, b3),
            new Line(b3, b4),
            new Line(b4, b1)
        };

        for(int i = 0; i < sz; i++) {
            List<Line> hp = new List<Line>(boundingBox);

            for(int j = 0; j < sz; j++) {
                if (i == j) continue;
                Line bc = geofunc.bisector(pts[i], pts[j]);
                hp.Add(bc);
            }

            List<Line> ans = geofunc.hp_intersect(hp);
            
            Debug.Log("anssz: "+ ans.Count);

            if (ans.Count >= 3) {
                cells.Add(new Polygon(ans));
            } else {
                cells.Add(null); 
            }
        }
    }
}
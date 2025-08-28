using System;
using Unity.VisualScripting;
using UnityEngine;

public class LineDrawObject : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public LineRenderer lineRenderer;

    public virtual void SetPoints((Vector3, Vector3) points)
    {
        lineRenderer.SetPosition(0, points.Item1);
        lineRenderer.SetPosition(1, points.Item2);
    }
   
    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }
}

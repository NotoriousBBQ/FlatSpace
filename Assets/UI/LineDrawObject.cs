using System;
using Unity.VisualScripting;
using UnityEngine;

public class LineDrawObject : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] private LineRenderer _lineRenderer;

    public void SetPoints((Vector3, Vector3) points)
    {
        _lineRenderer.SetPosition(0, points.Item1);
        _lineRenderer.SetPosition(1, points.Item2);
    }
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void Awake()
    {
    }
}

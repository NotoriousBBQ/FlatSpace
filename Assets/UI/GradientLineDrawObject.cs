using System;
using Unity.VisualScripting;
using UnityEngine;

public class GradientLineDrawObject : LineDrawObject
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] private Color color1 = Color.white;
    [SerializeField] private Color color2 = Color.blue;
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        float alpha = 1.0f;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(color1, 0.0f), new GradientColorKey(color2, 10.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(alpha, 0.0f), new GradientAlphaKey(alpha, 10.0f) }
        );
        lineRenderer.colorGradient = gradient;        
    }
}

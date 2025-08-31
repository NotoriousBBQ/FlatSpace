using System;
using Unity.VisualScripting;
using UnityEngine;

public class LineDrawObject : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public LineRenderer lineRenderer;
    public SpriteRenderer spriteRenderer;

    public virtual void SetPoints((Vector3, Vector3) points)
    {
        lineRenderer.SetPosition(0, points.Item1);
        lineRenderer.SetPosition(1, points.Item2);
        if (spriteRenderer)
        {
            spriteRenderer.transform.localPosition = (points.Item1 + points.Item2) / 2.0f;
            float angle = Vector2.SignedAngle(Vector2.up, points.Item2 - points.Item1);
            spriteRenderer.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    public virtual void SetColor(Color32 color)
    {
        lineRenderer.startColor = lineRenderer.endColor = color;
        if (spriteRenderer)
        {
            spriteRenderer.color = color;
        }
    }
   
    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }
}

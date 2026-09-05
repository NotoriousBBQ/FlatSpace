using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class LineDrawObject : MonoBehaviour
{
    // Start is called once before the first execution of UpdatePlanet after the MonoBehaviour is created
    public LineRenderer lineRenderer;
    public SpriteRenderer spriteRenderer;

    public virtual void SetPoints((Vector3, Vector3) points, float progressAmount = 0.0f)
    {
        lineRenderer.SetPosition(0, points.Item1);
        lineRenderer.SetPosition(1, points.Item2);
        if (spriteRenderer)
        {
        var position = points.Item1 + ((points.Item2 - points.Item1) * progressAmount);
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)
                || float.IsInfinity(position.x) || float.IsInfinity(position.y))
            {
                Console.WriteLine("Caught");

            }
            spriteRenderer.transform.localPosition = points.Item1 + ((points.Item2 - points.Item1) * progressAmount);
            float angle = Vector2.SignedAngle(Vector2.up, points.Item2 - points.Item1);
            spriteRenderer.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    public virtual void SetPath(List<Vector3> pathPoints, float progressAmount = 0.0f)
    {
        if (pathPoints == null || pathPoints.Count < 2) return;

        lineRenderer.positionCount = pathPoints.Count;
        lineRenderer.SetPositions(pathPoints.ToArray());

        if (!spriteRenderer) return;

        var totalLength = 0.0f;
        for (var i = 1; i < pathPoints.Count; i++)
            totalLength += Vector3.Distance(pathPoints[i - 1], pathPoints[i]);

        var targetDistance = totalLength * progressAmount;
        var traveled = 0.0f;
        for (var i = 1; i < pathPoints.Count; i++)
        {
            var segmentStart = pathPoints[i - 1];
            var segmentEnd = pathPoints[i];
            var segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            var reachedTarget = traveled + segmentLength >= targetDistance;
            var isLastSegment = i == pathPoints.Count - 1;
            if (reachedTarget || isLastSegment)
            {
                var segmentProgress = segmentLength > 0.0f
                    ? Math.Clamp((targetDistance - traveled) / segmentLength, 0.0f, 1.0f)
                    : 0.0f;
                var position = segmentStart + (segmentEnd - segmentStart) * segmentProgress;
                spriteRenderer.transform.localPosition = position;
                var angle = Vector2.SignedAngle(Vector2.up, segmentEnd - segmentStart);
                spriteRenderer.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                break;
            }
            traveled += segmentLength;
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

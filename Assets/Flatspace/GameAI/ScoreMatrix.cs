using System.Collections.Generic;
using UnityEngine;

 
public class ScoreMatrix
{
    public struct ScoreMatrixElement
    {
        public float Surplus;
        public string Target;
        public float Shortage;
        public float Cost;
    }
    public static int ScoreMatrixElementCompare(ScoreMatrixElement x, ScoreMatrixElement y)
    {
        var xScore = x.Cost - x.Surplus;
        var yScore = y.Cost - y.Surplus;
        return xScore.CompareTo(yScore);
    }
    
    public Dictionary<string, List<ScoreMatrixElement>> MatrixElements = new Dictionary<string, List<ScoreMatrixElement>>();
}

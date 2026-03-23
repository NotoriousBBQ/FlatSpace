using Flatspace.Objects.Production;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Catalog))]
public class CatalogEditor :Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var catalog = (Catalog)target;
        if (GUILayout.Button("Save Catalog"))
        {
            catalog.Save();
        }

        if (GUILayout.Button("Load Catalog"))
        {
            catalog.Load();
        }
    }
}

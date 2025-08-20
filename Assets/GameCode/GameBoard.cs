using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class Gameboard : MonoBehaviour
{
    [SerializeField] 
    private string intialBoardState;

    [SerializeField]
    private string planetPrefab;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void Awake()
    {
        if (string.IsNullOrEmpty(intialBoardState))
        {
            CreateDefaultBoardState();
        }
                
 
    }
    
    void CreateDefaultBoardState()
    {
        if (!string.IsNullOrEmpty(planetPrefab))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath(planetPrefab, typeof(GameObject)) as GameObject;
            prefab.transform.localPosition = new Vector3(100, 100, 0);
            Debug.Log("Prefab local pos " + prefab.transform.localPosition);
            var planetUi = Instantiate(prefab, this.transform);
            planetUi.transform.localPosition = new Vector3(100, 100, 0);
            Debug.Log("GO local pos " + planetUi.transform.localPosition);
        }
            
    }
}
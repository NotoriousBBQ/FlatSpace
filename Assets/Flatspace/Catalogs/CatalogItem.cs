using System;
using UnityEngine;

namespace Flatspace.Objects.Production
{
    [CreateAssetMenu(fileName = "CatalogItem", menuName = "Scriptable Objects/Production/CatalogItem")]
    [Serializable]
    public class CatalogItem : ScriptableObject
    {
        public string itemName;
        public string description;
        public string type;
        public float cost;
        public string effect;
        public string requiredTech;
        public bool researched;
    }
}
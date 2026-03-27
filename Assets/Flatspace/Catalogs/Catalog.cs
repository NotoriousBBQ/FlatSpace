using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Flatspace.Objects.Production
{
    public class Catalog : MonoBehaviour
    {
#if UNITY_EDITOR
        private static string CATALOG_SAVE_DIR = "Flatspace/Catalogs/";
#else
        private static string CATALOG_SAVE_DIR = "Catalogs/";
#endif
        private string catalogName = "";
        public string CatalogName
        {
            get {return catalogName;}
            set
            {
                if (value == null)
                    return;
                if(value == catalogName)
                    return;
                catalogName = value;
                catalogType = catalogName[..catalogName.IndexOf(' ')];
                Load();
            }
        }
        public string catalogType;
        
        public List<CatalogItem> catalogItems = new List<CatalogItem>();
        public CatalogItem GetItem(string catalogItemName)
        {
            return catalogItems.Find(x => x.itemName == catalogItemName);
        }

        #region SaveLoad
        [Serializable]
        public class CatalogSaveData
        {
            [Serializable]
            public struct ItemSaveData
            {
                public string itemName;
                public string description;
                public string type;
                public float cost;
                public string effect;
                public string requiredTech;
                public bool researched;

                public ItemSaveData(CatalogItem item)
                {
                    itemName = item.name;
                    description = item.description;
                    type = item.type;
                    cost = item.cost;
                    effect = item.effect;
                    requiredTech = item.requiredTech;
                    researched = item.researched;
                }
            }

            public CatalogSaveData(Catalog catalog)
            {
                catalogName = catalog.catalogName;
                catalogType = catalog.catalogType;
                foreach (var catalogItem in catalog.catalogItems)
                {
                    var itemSaveData = new ItemSaveData(catalogItem);
                    items.Add(itemSaveData);
                }

            }
            public string catalogName;
            public string catalogType;
            public List<ItemSaveData>  items = new List<ItemSaveData>();
        }        
        public void Save()
        {
            var catalogSaveData = new CatalogSaveData(this);
            var jsonData  = JsonUtility.ToJson(catalogSaveData,true);
            var savePath = Path.Combine(CATALOG_SAVE_DIR, catalogType);
#if UNITY_EDITOR
            savePath = Path.Combine(Application.dataPath, savePath);
#else
            savePath = Path.Combine(Application.persistentDataPath, savePath);
#endif
            savePath = Path.Combine(savePath, catalogName);
            savePath = Path.ChangeExtension(savePath, "json");
            SaveLoadSystem.SaveJsonData(jsonData, savePath );
        }

        public void Load()
        {
            string jsonData;
            var loadPath = Path.Combine(CATALOG_SAVE_DIR, catalogType);            
#if UNITY_EDITOR
            loadPath = Path.Combine(Application.dataPath, loadPath);
#else       
            loadPath = Path.Combine(Application.persistentDataPath, loadPath);
#endif
            loadPath = Path.Combine(loadPath, catalogName);
            loadPath = Path.ChangeExtension(loadPath, "json");
            if (SaveLoadSystem.LoadJsonData(out jsonData, loadPath))
            {
                CreateCatalogFromCatalogSaveData(jsonData);            
            }
        }

        private void CreateCatalogFromCatalogSaveData(string jsonData)
        {
            var loadConfig = JsonUtility.FromJson<CatalogSaveData>(jsonData);
            catalogName = loadConfig.catalogName;
            catalogType = loadConfig.catalogType;
            catalogItems.Clear();
            foreach (var itemData in loadConfig.items)
            {
                var catalogItem = ScriptableObject.CreateInstance<CatalogItem>();
                catalogItem.name = itemData.itemName;
                catalogItem.itemName = itemData.itemName;
                catalogItem.description = itemData.description;
                catalogItem.type = itemData.type;
                catalogItem.cost = itemData.cost;
                catalogItem.effect = itemData.effect;
                catalogItem.requiredTech = itemData.requiredTech;
                catalogItem.researched = itemData.researched;
                catalogItems.Add(catalogItem);
            }
            
        }
        #endregion
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
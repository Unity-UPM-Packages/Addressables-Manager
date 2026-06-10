using System.IO;
using UnityEditor;
using UnityEngine;

using TheLegends.Base.Addressables;

namespace TheLegends.Base.Addressables.Editor
{
    [CustomEditor(typeof(AddressableConfig))]
    public class AddressableConfigEditor : UnityEditor.Editor
    {
        private static AddressableConfig instance = null;

        public static AddressableConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<AddressableConfig>(AddressableConfig.FileName);
                }

                if (instance != null)
                {
                    Selection.activeObject = instance;
                }
                else
                {
                    Directory.CreateDirectory(AddressableConfig.ResDir);

                    instance = CreateInstance<AddressableConfig>();

                    string assetPath = Path.Combine(AddressableConfig.ResDir, AddressableConfig.FileName);
                    string assetPathWithExtension = Path.ChangeExtension(assetPath, AddressableConfig.FileExtension);
                    AssetDatabase.CreateAsset(instance, assetPathWithExtension);
                    AssetDatabase.SaveAssets();
                    Selection.activeObject = instance;
                }

                return instance;
            }
        }

        [MenuItem("TripSoft/Addressable Settings")]
        public static void OpenInspector()
        {
            if (Instance == null)
            {
                Debug.Log("Created new Addressable Settings");
            }
        }
    }
}

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace TheLegends.Base.Addressables.Demo
{
    public static class AddressablesDemoSetup
    {
        private static readonly string[] PrefabNames = { "DemoCube", "DemoSphere", "DemoCapsule", "DemoCylinder" };

        [MenuItem("TripSoft/Setup Addressables Demo")]
        public static void SetupDemo()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressablesDemoSetup] AddressableAssetSettings not found! Please create settings first via Window > Asset Management > Addressables > Groups > Create Addressables Settings.");
                EditorUtility.DisplayDialog("Error", "Please create Addressables Settings first in the Addressables Groups window.", "OK");
                return;
            }

            var defaultGroup = settings.DefaultGroup;
            if (defaultGroup == null)
            {
                Debug.LogError("[AddressablesDemoSetup] Default Addressable group not found!");
                return;
            }

            int count = 0;
            foreach (var name in PrefabNames)
            {
                // Find prefab in the project
                string[] guids = AssetDatabase.FindAssets($"{name} t:Prefab");
                if (guids.Length == 0)
                {
                    Debug.LogWarning($"[AddressablesDemoSetup] Could not find prefab asset '{name}' in the project.");
                    continue;
                }

                // Use the first found prefab matching the name
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                string guid = AssetDatabase.AssetPathToGUID(path);

                // Check if already registered
                var entry = settings.FindAssetEntry(guid);
                if (entry == null)
                {
                    // Add entry to the default group
                    entry = settings.CreateOrMoveEntry(guid, defaultGroup);
                    entry.address = name;
                    count++;
                    Debug.Log($"[AddressablesDemoSetup] Registered '{name}' to Addressables (Address: '{name}').");
                }
                else if (entry.address != name)
                {
                    entry.address = name;
                    Debug.Log($"[AddressablesDemoSetup] Updated Addressables key for '{name}' to '{name}'.");
                    count++;
                }
            }

            if (count > 0)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
                AssetDatabase.SaveAssets();
                Debug.Log($"[AddressablesDemoSetup] Successfully configured {count} Addressable assets for the Demo!");
                EditorUtility.DisplayDialog("Success", $"Successfully configured {count} assets for the Addressables Demo!", "OK");
            }
            else
            {
                Debug.Log("[AddressablesDemoSetup] All demo assets are already configured!");
                EditorUtility.DisplayDialog("Info", "All demo assets are already configured!", "OK");
            }
        }
    }
}
#endif

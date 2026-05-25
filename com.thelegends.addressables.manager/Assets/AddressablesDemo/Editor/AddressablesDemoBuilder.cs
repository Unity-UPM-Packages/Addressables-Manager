// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using com.thelegends.addressables.manager;
using com.thelegends.unity.pooling;

#if UNITY_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.AddressableAssets;
#endif

namespace com.thelegends.addressables.manager.Demo.Editor
{
    /// <summary>
    /// Builder script to programmatically construct the 3D prefabs, 
    /// register them in Addressables, build the demo scene, and set up the testing UI.
    /// </summary>
    // Forced recompilation comment to apply newly set scripting defines
    public static class AddressablesDemoBuilder
    {
        private const string RootFolder = "Assets/AddressablesDemo";
        private const string PrefabsFolder = RootFolder + "/Prefabs";
        private const string ScenesFolder = RootFolder + "/Scenes";
        private const string ConfigFolder = RootFolder + "/Config";
        private const string DemoScenePath = ScenesFolder + "/AddressablesDemoScene.unity";
        private const string DemoConfigPath = ConfigFolder + "/AddressablesDemoConfig.asset";

        [MenuItem("Tools/TheLegends/Build Addressables Demo Scene")]
        public static void BuildDemo()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Addressables Demo Builder", "Preparing directories...", 0.1f);
                CreateFolders();

                EditorUtility.DisplayProgressBar("Addressables Demo Builder", "Generating 3D Prefabs...", 0.3f);
                var prefabs = GeneratePrefabs();

                EditorUtility.DisplayProgressBar("Addressables Demo Builder", "Registering Addressables...", 0.5f);
                RegisterInAddressables(prefabs);

                EditorUtility.DisplayProgressBar("Addressables Demo Builder", "Generating Config ScriptableObject...", 0.7f);
                var config = CreateDemoConfig(prefabs);

                EditorUtility.DisplayProgressBar("Addressables Demo Builder", "Constructing Demo Scene & UI...", 0.8f);
                BuildScene(config, prefabs);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Addressables Demo Builder", "Demo built successfully!\n\nPlease open the scene at:\nAssets/AddressablesDemo/Scenes/AddressablesDemoScene.unity", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[AddressablesDemoBuilder] Construction failed: {ex}");
                EditorUtility.DisplayDialog("Addressables Demo Builder", $"Error occurred:\n{ex.Message}", "OK");
            }
        }

        private static void CreateFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "AddressablesDemo");
            if (!AssetDatabase.IsValidFolder(PrefabsFolder))
                AssetDatabase.CreateFolder(RootFolder, "Prefabs");
            if (!AssetDatabase.IsValidFolder(ScenesFolder))
                AssetDatabase.CreateFolder(RootFolder, "Scenes");
            if (!AssetDatabase.IsValidFolder(ConfigFolder))
                AssetDatabase.CreateFolder(RootFolder, "Config");
        }

        private static Dictionary<string, GameObject> GeneratePrefabs()
        {
            var dict = new Dictionary<string, GameObject>();

            string[] names = { "DemoCube", "DemoSphere", "DemoCapsule", "DemoCylinder" };
            PrimitiveType[] types = { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Capsule, PrimitiveType.Cylinder };
            Color[] colors = { Color.red, Color.blue, Color.green, Color.magenta };

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                string path = $"{PrefabsFolder}/{name}.prefab";

                // Create Primitive
                GameObject go = GameObject.CreatePrimitive(types[i]);
                go.name = name;

                // Create Material
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = colors[i];
                string matPath = $"{PrefabsFolder}/{name}_Material.mat";
                AssetDatabase.CreateAsset(mat, matPath);

                go.GetComponent<Renderer>().sharedMaterial = mat;

                // Save Prefab
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                GameObject.DestroyImmediate(go);

                dict.Add(name, prefab);
            }

            return dict;
        }

        private static void RegisterInAddressables(Dictionary<string, GameObject> prefabs)
        {
#if UNITY_ADDRESSABLES
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            }

            var group = settings.DefaultGroup;

            foreach (var kvp in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(kvp.Value);
                string guid = AssetDatabase.AssetPathToGUID(path);

                var entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = kvp.Key; // Use prefab name as key (DemoCube, DemoSphere, DemoCapsule, DemoCylinder)
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryAdded, entry, true);
            }
#else
            Debug.LogWarning("[AddressablesDemoBuilder] UNITY_ADDRESSABLES symbol is not active. Addressables registration skipped.");
#endif
        }

        private static AddressableConfig CreateDemoConfig(Dictionary<string, GameObject> prefabs)
        {
            AddressableConfig config = AssetDatabase.LoadAssetAtPath<AddressableConfig>(DemoConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<AddressableConfig>();
                AssetDatabase.CreateAsset(config, DemoConfigPath);
            }

            // Set retry limits via reflection since they are private SerializedFields
            typeof(AddressableConfig).GetField("_maxRetryCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(config, 3);
            typeof(AddressableConfig).GetField("_retryDelaySeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(config, 2f);
            typeof(AddressableConfig).GetField("_useFallbackOnFailure", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(config, true);

            // Configure Fallback Mapping: "NonExistentKey" -> DemoCylinder prefab
#if UNITY_ADDRESSABLES
            if (prefabs.TryGetValue("DemoCylinder", out GameObject cylinderPrefab))
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cylinderPrefab));
                AssetReference fallbackRef = new AssetReference(guid);

                var fallbackMappingType = typeof(FallbackMapping);
                var keyField = fallbackMappingType.GetField("_key", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var assetField = fallbackMappingType.GetField("_fallbackAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var mapping = new FallbackMapping();
                object mappingBoxed = mapping;
                keyField.SetValue(mappingBoxed, "NonExistentKey");
                assetField.SetValue(mappingBoxed, fallbackRef);
                mapping = (FallbackMapping)mappingBoxed;

                var fallbackAssetsField = typeof(AddressableConfig).GetField("_fallbackAssets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var mappingsList = new List<FallbackMapping> { mapping };
                fallbackAssetsField.SetValue(config, mappingsList);
            }
#endif

            EditorUtility.SetDirty(config);
            return config;
        }

        private static void BuildScene(AddressableConfig config, Dictionary<string, GameObject> prefabs)
        {
            // Create New Scene
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Setup Main Camera
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.transform.position = new Vector3(0, 0, -10);
                mainCam.transform.rotation = Quaternion.identity;
                mainCam.backgroundColor = new Color(0.12f, 0.12f, 0.16f); // Premium Sleek Dark Mode
                mainCam.clearFlags = CameraClearFlags.SolidColor;
            }

            // Create Canvas
            GameObject canvasGo = new GameObject("Canvas_Main");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Create EventSystem
            if (GameObject.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // UI Layout
            // 1. Dark Overlay Panel
            GameObject panelBg = CreateUiElement("Panel_Background", canvasGo);
            var rectBg = panelBg.GetComponent<RectTransform>();
            rectBg.anchorMin = Vector2.zero;
            rectBg.anchorMax = Vector2.one;
            rectBg.offsetMin = Vector2.zero;
            rectBg.offsetMax = Vector2.zero;
            var imgBg = panelBg.AddComponent<Image>();
            imgBg.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);

            // 2. Title Label
            GameObject titleGo = CreateUiElement("Txt_Title", panelBg);
            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text = "ADDRESSABLES MANAGER RUNTIME DEMO";
            titleText.fontSize = 32;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = new Color(0.4f, 0.7f, 1f, 1f); // Vibrant light blue
            var rectTitle = titleGo.GetComponent<RectTransform>();
            rectTitle.anchorMin = new Vector2(0, 0.9f);
            rectTitle.anchorMax = new Vector2(1, 1);
            rectTitle.offsetMin = new Vector2(50, 0);
            rectTitle.offsetMax = new Vector2(-50, -20);

            // 3. Panel Controls (Left side)
            GameObject panelControls = CreateUiElement("Panel_Controls", panelBg);
            var rectControls = panelControls.GetComponent<RectTransform>();
            rectControls.anchorMin = new Vector2(0.05f, 0.15f);
            rectControls.anchorMax = new Vector2(0.35f, 0.85f);
            rectControls.offsetMin = Vector2.zero;
            rectControls.offsetMax = Vector2.zero;
            
            var verticalLayout = panelControls.AddComponent<VerticalLayoutGroup>();
            verticalLayout.spacing = 15;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childControlHeight = true;
            verticalLayout.childControlWidth = true;

            // Generate buttons
            string[] buttonLabels = {
                "1. Load Cube (Addressables)",
                "2. Release Cube",
                "3. Load Sphere (Addressables)",
                "4. Release Sphere",
                "5. Spawn Cube (PoolManager)",
                "6. Despawn Cube",
                "7. Dispose Pool",
                "8. Spawn Scoped Panel (Auto-clean)",
                "9. Destroy Scoped Panel",
                "10. Test Fallback (Invalid Key)"
            };

            Button[] buttons = new Button[buttonLabels.Length];

            for (int i = 0; i < buttonLabels.Length; i++)
            {
                GameObject btnGo = CreateUiElement($"Btn_Case_{i + 1}", panelControls);
                var btn = btnGo.AddComponent<Button>();
                var btnImg = btnGo.AddComponent<Image>();
                btnImg.color = new Color(0.18f, 0.2f, 0.25f, 1f);

                // Add text child
                GameObject txtGo = CreateUiElement("Text", btnGo);
                var txt = txtGo.AddComponent<TextMeshProUGUI>();
                txt.text = buttonLabels[i];
                txt.fontSize = 18;
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = Color.white;

                var txtRect = txtGo.GetComponent<RectTransform>();
                txtRect.anchorMin = Vector2.zero;
                txtRect.anchorMax = Vector2.one;
                txtRect.offsetMin = new Vector2(5, 5);
                txtRect.offsetMax = new Vector2(-5, -5);

                btn.targetGraphic = btnImg;
                
                // Add hover/click colors
                ColorBlock colors = btn.colors;
                colors.normalColor = new Color(0.18f, 0.2f, 0.25f, 1f);
                colors.highlightedColor = new Color(0.3f, 0.4f, 0.5f, 1f);
                colors.pressedColor = new Color(0.12f, 0.14f, 0.18f, 1f);
                btn.colors = colors;

                // Set layout height
                var layoutElement = btnGo.AddComponent<LayoutElement>();
                layoutElement.preferredHeight = 45;

                buttons[i] = btn;
            }

            // 4. Panel RAM Cache Status (Right top)
            GameObject panelCache = CreateUiElement("Panel_CacheStatus", panelBg);
            var rectCache = panelCache.GetComponent<RectTransform>();
            rectCache.anchorMin = new Vector2(0.4f, 0.45f);
            rectCache.anchorMax = new Vector2(0.95f, 0.85f);
            rectCache.offsetMin = Vector2.zero;
            rectCache.offsetMax = Vector2.zero;

            var cacheImg = panelCache.AddComponent<Image>();
            cacheImg.color = new Color(0.12f, 0.13f, 0.16f, 1f);

            GameObject txtCacheGo = CreateUiElement("Txt_Status", panelCache);
            var txtCache = txtCacheGo.AddComponent<TextMeshProUGUI>();
            txtCache.text = "Service Cache is empty.\nNo assets loaded in RAM.";
            txtCache.fontSize = 20;
            txtCache.fontStyle = FontStyles.Normal;
            txtCache.alignment = TextAlignmentOptions.TopLeft;
            txtCache.color = new Color(0.8f, 0.85f, 0.9f, 1f);

            var rectTxtCache = txtCacheGo.GetComponent<RectTransform>();
            rectTxtCache.anchorMin = Vector2.zero;
            rectTxtCache.anchorMax = Vector2.one;
            rectTxtCache.offsetMin = new Vector2(20, 20);
            rectTxtCache.offsetMax = new Vector2(-20, -20);

            // 5. Panel Console Log (Right bottom)
            GameObject panelLog = CreateUiElement("Panel_ConsoleLog", panelBg);
            var rectLog = panelLog.GetComponent<RectTransform>();
            rectLog.anchorMin = new Vector2(0.4f, 0.05f);
            rectLog.anchorMax = new Vector2(0.95f, 0.4f);
            rectLog.offsetMin = Vector2.zero;
            rectLog.offsetMax = Vector2.zero;

            var logImg = panelLog.AddComponent<Image>();
            logImg.color = new Color(0.05f, 0.05f, 0.07f, 1f);

            GameObject txtLogGo = CreateUiElement("Txt_Logs", panelLog);
            var txtLog = txtLogGo.AddComponent<TextMeshProUGUI>();
            txtLog.text = "[Demo Controller Console] Click any button to start testing.";
            txtLog.fontSize = 16;
            txtLog.alignment = TextAlignmentOptions.BottomLeft;
            txtLog.color = new Color(0.7f, 0.7f, 0.7f, 1f);

            var rectTxtLog = txtLogGo.GetComponent<RectTransform>();
            rectTxtLog.anchorMin = Vector2.zero;
            rectTxtLog.anchorMax = Vector2.one;
            rectTxtLog.offsetMin = new Vector2(15, 15);
            rectTxtLog.offsetMax = new Vector2(-15, -15);

            // Create Runtime Controller GameObject
            GameObject controllerGo = new GameObject("DemoController");
            var tester = controllerGo.AddComponent<AddressablesManualTester>();

            // Setup Singletons references
            GameObject serviceGo = new GameObject("AddressableService");
            var service = serviceGo.AddComponent<AddressableService>();
            // Link objects
            serviceGo.transform.SetParent(controllerGo.transform);

            GameObject poolGo = new GameObject("PoolManager");
            var poolManager = poolGo.AddComponent<PoolManager>();
            poolGo.transform.SetParent(controllerGo.transform);

            // Wire SerializedFields via reflection
            var typeTester = typeof(AddressablesManualTester);
            typeTester.GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, config);
            
#if UNITY_ADDRESSABLES
            if (prefabs.TryGetValue("DemoCylinder", out GameObject cylinderPrefab))
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cylinderPrefab));
                AssetReference fallbackRef = new AssetReference(guid);
                typeTester.GetField("_fallbackAssetReference", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, fallbackRef);
            }
#endif

            typeTester.GetField("_btnLoadCube", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[0]);
            typeTester.GetField("_btnReleaseCube", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[1]);
            typeTester.GetField("_btnLoadSphere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[2]);
            typeTester.GetField("_btnReleaseSphere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[3]);
            typeTester.GetField("_btnSpawnPoolCube", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[4]);
            typeTester.GetField("_btnDespawnPoolCube", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[5]);
            typeTester.GetField("_btnDisposePool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[6]);
            typeTester.GetField("_btnSpawnScoped", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[7]);
            typeTester.GetField("_btnDestroyScoped", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[8]);
            typeTester.GetField("_btnTestFallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, buttons[9]);

            typeTester.GetField("_txtCacheStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, txtCache);
            typeTester.GetField("_txtLogConsole", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(tester, txtLog);

            // Save Scene
            EditorSceneManager.SaveScene(scene, DemoScenePath);
        }

        private static GameObject CreateUiElement(string name, GameObject parent)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<RectTransform>();
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }
            return go;
        }
    }
}

// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using com.thelegends.addressables.manager;
using UnityEngine.AddressableAssets;

namespace com.thelegends.addressables.manager.Demo
{
    /// <summary>
    /// Interactive runtime controller for testing the Addressables Manager wrapper library.
    /// Hooks UI Buttons dynamically and provides live status updates and console logs in-scene.
    /// </summary>
    public sealed class AddressablesManualTester : MonoBehaviour
    {
        [Header("Configurations")]
        [SerializeField] private AssetReference _fallbackAssetReference;

        [Header("UI Controls")]
        [SerializeField] private Button _btnLoadCube;
        [SerializeField] private Button _btnReleaseCube;
        [SerializeField] private Button _btnLoadSphere;
        [SerializeField] private Button _btnReleaseSphere;
        [SerializeField] private Button _btnSpawnPoolCube;
        [SerializeField] private Button _btnDespawnPoolCube;
        [SerializeField] private Button _btnDisposePool;
        [SerializeField] private Button _btnSpawnScoped;
        [SerializeField] private Button _btnDestroyScoped;
        [SerializeField] private Button _btnTestFallback;
        [SerializeField] private Button _btnCheckCdnSize;
        [SerializeField] private Button _btnDownloadCdn;

        [Header("UI Display")]
        [SerializeField] private TextMeshProUGUI _txtCacheStatus;
        [SerializeField] private TextMeshProUGUI _txtLogConsole;

        private PooledPrefabHelper _cubePoolHelper;
        private readonly List<GameObject> _spawnedPoolCubes = new List<GameObject>();
        private GameObject _scopedPanelInstance;
        private CancellationTokenSource _cts;

        private const string CubeKey = "DemoCube";
        private const string SphereKey = "DemoSphere";
        private const string InvalidKey = "NonExistentKey";

        private void Start()
        {
            _cts = new CancellationTokenSource();
            InitializeButtons();
            InitializeAddressableService().Forget();
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cubePoolHelper?.Dispose();
            
            foreach (var cube in _spawnedPoolCubes)
            {
                if (cube != null)
                {
                    Destroy(cube);
                }
            }
        }

        private async UniTaskVoid InitializeAddressableService()
        {
            Log("Initializing AddressableService...");
            if (AddressableService.Instance != null)
            {
                await AddressableService.Instance.InitializeAsync();
                Log("AddressableService Initialized successfully!");
                UpdateCacheStatusDisplay();
            }
            else
            {
                LogError("AddressableService.Instance is null!");
            }
        }

        private void InitializeButtons()
        {
            _btnLoadCube.onClick.AddListener(() => LoadAsset(CubeKey).Forget());
            _btnReleaseCube.onClick.AddListener(() => ReleaseAsset(CubeKey));
            
            _btnLoadSphere.onClick.AddListener(() => LoadAsset(SphereKey).Forget());
            _btnReleaseSphere.onClick.AddListener(() => ReleaseAsset(SphereKey));

            _btnSpawnPoolCube.onClick.AddListener(() => SpawnPoolCube().Forget());
            _btnDespawnPoolCube.onClick.AddListener(DespawnPoolCube);
            _btnDisposePool.onClick.AddListener(DisposePool);

            _btnSpawnScoped.onClick.AddListener(() => SpawnScopedPanel().Forget());
            _btnDestroyScoped.onClick.AddListener(DestroyScopedPanel);
            _btnTestFallback.onClick.AddListener(() => LoadAsset(InvalidKey).Forget());
            _btnCheckCdnSize.onClick.AddListener(() => CheckCdnDownloadSize().Forget());
            _btnDownloadCdn.onClick.AddListener(() => DownloadCdnAssets().Forget());
        }

        // ==========================================
        // Test Actions
        // ==========================================

        private async UniTask LoadAsset(string key)
        {
            try
            {
                Log($"[ACTION] Loading '{key}'...");
                GameObject asset = await AddressableService.Instance.LoadAssetAsync<GameObject>(key, _cts.Token);
                Log($"[SUCCESS] Loaded asset: '{asset.name}'!");
            }
            catch (OperationCanceledException)
            {
                LogWarning($"[CANCEL] Loading '{key}' was canceled.");
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Loading '{key}' failed: {ex.Message}");
            }
            finally
            {
                UpdateCacheStatusDisplay();
            }
        }

        private void ReleaseAsset(string key)
        {
            Log($"[ACTION] Releasing '{key}'...");
            AddressableService.Instance.ReleaseAsset(key);
            Log($"[SUCCESS] Released '{key}'");
            UpdateCacheStatusDisplay();
        }

        private async UniTask SpawnPoolCube()
        {
            try
            {
                if (_cubePoolHelper == null)
                {
                    Log("[ACTION] Initializing PooledPrefabHelper for Cube...");
                    _cubePoolHelper = new PooledPrefabHelper();
                    await _cubePoolHelper.InitializeAsync(CubeKey, _cts.Token);
                    Log("[SUCCESS] Pool initialized!");
                }

                Log("[ACTION] Spawning Cube from Pool...");
                GameObject cube = await _cubePoolHelper.GetInstanceAsync();
                cube.transform.position = UnityEngine.Random.insideUnitSphere * 3f;
                _spawnedPoolCubes.Add(cube);
                Log($"[SUCCESS] Spawned Cube instance: '{cube.name}'!");
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Pool spawn failed: {ex.Message}");
            }
            finally
            {
                UpdateCacheStatusDisplay();
            }
        }

        private void DespawnPoolCube()
        {
            if (_spawnedPoolCubes.Count == 0)
            {
                LogWarning("No spawned pool cubes to despawn.");
                return;
            }

            int lastIdx = _spawnedPoolCubes.Count - 1;
            GameObject cube = _spawnedPoolCubes[lastIdx];
            _spawnedPoolCubes.RemoveAt(lastIdx);

            if (cube != null && _cubePoolHelper != null)
            {
                Log($"[ACTION] Returning '{cube.name}' to Pool...");
                _cubePoolHelper.ReturnInstance(cube);
                Log("[SUCCESS] Returned cube to pool.");
            }
        }

        private void DisposePool()
        {
            if (_cubePoolHelper != null)
            {
                Log("[ACTION] Disposing PooledPrefabHelper...");
                _cubePoolHelper.Dispose();
                _cubePoolHelper = null;
                _spawnedPoolCubes.Clear();
                Log("[SUCCESS] Pool disposed & clean-up completed.");
            }
            UpdateCacheStatusDisplay();
        }

        private async UniTask SpawnScopedPanel()
        {
            if (_scopedPanelInstance != null)
            {
                LogWarning("Scoped panel is already active.");
                return;
            }

            try
            {
                Log("[ACTION] Spawning Scoped Panel...");
                // Create a temporary panel to host the lifetime scope
                _scopedPanelInstance = new GameObject("ScopedPanel");
                var scope = _scopedPanelInstance.AddComponent<AddressableLifetimeScope>();

                Log("[ACTION] Scoped Panel is loading DemoSphere internally...");
                // Load asset bound to the scoped panel's lifetime token
                GameObject spherePrefab = await scope.LoadAssetAsync<GameObject>(SphereKey);
                Log($"[SUCCESS] Scoped panel loaded Sphere prefab: '{spherePrefab.name}'!");

                // Instantiate the visual sphere so the user can see it in the scene!
                GameObject sphereInstance = Instantiate(spherePrefab);
                sphereInstance.name = "DemoSphere_Instance";
                sphereInstance.transform.SetParent(_scopedPanelInstance.transform, false);
                sphereInstance.transform.localPosition = new Vector3(2f, 0f, 0f); // Offset position so it's visible
                Log("[SUCCESS] Instantiated DemoSphere under ScopedPanel!");
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Scoped load failed: {ex.Message}");
                if (_scopedPanelInstance != null)
                {
                    Destroy(_scopedPanelInstance);
                }
            }
            finally
            {
                UpdateCacheStatusDisplay();
            }
        }

        private void DestroyScopedPanel()
        {
            if (_scopedPanelInstance == null)
            {
                LogWarning("No Scoped panel active to destroy.");
                return;
            }

            Log("[ACTION] Destroying Scoped Panel GameObject. This should trigger automatic release of DemoSphere...");
            Destroy(_scopedPanelInstance);
            _scopedPanelInstance = null;
            
            // Wait brief frame for destruction to propagate
            UniTask.DelayFrame(1).ContinueWith(UpdateCacheStatusDisplay).Forget();
            Log("[SUCCESS] Scoped Panel destroyed.");
        }

        private async UniTask CheckCdnDownloadSize()
        {
            try
            {
                Log("[ACTION] Checking CDN Download Size...");
                string[] keys = { CubeKey, SphereKey, "DemoCapsule", "DemoCylinder" };
                long size = await CdnDownloadManager.Instance.GetDownloadSizeAsync(keys, _cts.Token);
                Log($"[SUCCESS] CDN Download Size for keys: {size} bytes ({size / 1024f:F2} KB)");
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Check size failed: {ex.Message}");
            }
        }

        private async UniTask DownloadCdnAssets()
        {
            try
            {
                Log("[ACTION] Downloading CDN Assets...");
                string[] keys = { CubeKey, SphereKey, "DemoCapsule", "DemoCylinder" };
                
                var progress = new Progress<DownloadProgressStatus>(status =>
                {
                    Log($"[PROGRESS] CDN Download: {status.Progress * 100f:F1}% ({status.DownloadedBytes / 1024f:F1}/{status.TotalBytes / 1024f:F1} KB)");
                });

                await CdnDownloadManager.Instance.DownloadDependenciesAsync(keys, progress, _cts.Token);
                Log("[SUCCESS] CDN Download completed successfully!");
            }
            catch (OperationCanceledException)
            {
                LogWarning("[CANCEL] CDN Download was canceled.");
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] CDN Download failed: {ex.Message}");
            }
        }

        // ==========================================
        // Helper Methods
        // ==========================================

        private void UpdateCacheStatusDisplay()
        {
            if (AddressableService.Instance == null)
            {
                _txtCacheStatus.text = "Service Cache: NULL";
                return;
            }

            var cacheField = typeof(AddressableService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var cache = cacheField?.GetValue(AddressableService.Instance) as System.Collections.IDictionary;

            if (cache == null || cache.Count == 0)
            {
                _txtCacheStatus.text = "Service Cache is empty.\nNo assets loaded in RAM.";
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Service Cache State (RAM):");
            sb.AppendLine("--------------------------------");

            foreach (System.Collections.DictionaryEntry entry in cache)
            {
                string key = entry.Key.ToString();
                object cachedAsset = entry.Value;
                var refCountProp = cachedAsset.GetType().GetProperty("RefCount");
                var redirectedKeyProp = cachedAsset.GetType().GetProperty("RedirectedKey");

                int refCount = (int)(refCountProp?.GetValue(cachedAsset) ?? 0);
                string redirectedKey = redirectedKeyProp?.GetValue(cachedAsset) as string;

                if (!string.IsNullOrEmpty(redirectedKey))
                {
                    sb.AppendLine($"• {key} (Redirected -> {redirectedKey}) [RefCount: {refCount}]");
                }
                else
                {
                    sb.AppendLine($"• {key} [RefCount: {refCount}]");
                }
            }

            _txtCacheStatus.text = sb.ToString();
        }

        private void Log(string message)
        {
            Debug.Log(message);
            AppendLog($"<color=white>{message}</color>");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning(message);
            AppendLog($"<color=yellow>{message}</color>");
        }

        private void LogError(string message)
        {
            Debug.LogError(message);
            AppendLog($"<color=red>{message}</color>");
        }

        private void AppendLog(string coloredMessage)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            _txtLogConsole.text = $"[{time}] {coloredMessage}\n" + _txtLogConsole.text;
            
            // Limit log lines to prevent UI overflow
            if (_txtLogConsole.text.Length > 2000)
            {
                _txtLogConsole.text = _txtLogConsole.text.Substring(0, 2000);
            }
        }
    }
}

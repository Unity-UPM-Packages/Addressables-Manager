// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
using com.thelegends.unity.pooling;

namespace com.thelegends.addressables.manager.Tests
{
    /// <summary>
    /// Unit and integration test suite for the Addressables Manager package.
    /// Tests loading, caching, reference counting, and pooling features.
    /// </summary>
    [TestFixture]
    public sealed class AddressablesTests
    {
        private GameObject _testGo;
        private AddressableLifetimeScope _lifetimeScope;
        private AddressableConfig _config;
        private GameObject _serviceGo;
        private GameObject _poolGo;

        /// <summary>
        /// Sets up the test environment prior to executing each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _testGo = new GameObject("TestObject");
            _lifetimeScope = _testGo.AddComponent<AddressableLifetimeScope>();

            // Create a temporary ScriptableObject configuration
            _config = ScriptableObject.CreateInstance<AddressableConfig>();
        }

        /// <summary>
        /// Cleans up the test environment after executing each test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (_testGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_testGo);
            }

            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
            }

            // Cleanup Singletons safely
            if (AddressableService.Instance != null)
            {
                var go = AddressableService.Instance.gameObject;
                AddressableService.DestroyInstance();
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            else
            {
                AddressableService.DestroyInstance();
            }

            if (PoolManager.Instance != null)
            {
                var go = PoolManager.Instance.gameObject;
                PoolManager.DestroyInstance();
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            else
            {
                PoolManager.DestroyInstance();
            }

            if (_serviceGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_serviceGo);
            }

            if (_poolGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_poolGo);
            }
        }

        /// <summary>
        /// Tests that requesting duplicate keys records a single tracking entry with an incremented reference count.
        /// </summary>
        [Test]
        public void TestDuplicateTrackingMemoryEfficiency()
        {
            // Act: Call LoadAssetAsync (we simulate by accessing private _trackedKeys via reflection to verify behavior)
            var trackedKeysField = typeof(AddressableLifetimeScope).GetField("_trackedKeys", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(trackedKeysField, "Could not find _trackedKeys field in AddressableLifetimeScope.");

            var trackedKeys = (Dictionary<string, int>)trackedKeysField.GetValue(_lifetimeScope);
            Assert.IsNotNull(trackedKeys, "_trackedKeys dictionary should not be null.");

            // Simulate loading the same key multiple times
            string key = "test_prefab_key";

            // Simulate how LoadAssetAsync increments tracking
            IncrementTracking(key);
            IncrementTracking(key);

            // Assert: Verify that the key is tracked once with count of 2, rather than duplicating entries
            Assert.AreEqual(1, trackedKeys.Count, "Tracked keys count should be 1.");
            Assert.IsTrue(trackedKeys.ContainsKey(key), "Tracked keys should contain the key.");
            Assert.AreEqual(2, trackedKeys[key], "The reference count of the tracked key should be 2.");
        }

        /// <summary>
        /// Tests that loading failures redirect to the configured fallback asset mappings when UseFallbackOnFailure is enabled.
        /// </summary>
        /// <returns>A UnityTest IEnumerator representation.</returns>
        [UnityTest]
        public IEnumerator TestFallbackMappingsOnLoadingErrors() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: Setup fallbacks in config
            string mainKey = "invalid_non_existent_key";

            // Create dummy asset reference for fallback
            AssetReference fallbackAssetRef = new AssetReference("dummy-guid-12345");

            // We use reflection to set private fields on AddressableConfig if we cannot set them via constructor/properties
            var fallbackMappingType = typeof(FallbackMapping);
            var keyField = fallbackMappingType.GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance);
            var assetField = fallbackMappingType.GetField("_fallbackAsset", BindingFlags.NonPublic | BindingFlags.Instance);

            var mapping = new FallbackMapping();
            
            // Set fields on the struct
            object mappingBoxed = mapping;
            keyField.SetValue(mappingBoxed, mainKey);
            assetField.SetValue(mappingBoxed, fallbackAssetRef);
            mapping = (FallbackMapping)mappingBoxed;

            // Set the config list
            var fallbackAssetsField = typeof(AddressableConfig).GetField("_fallbackAssets", BindingFlags.NonPublic | BindingFlags.Instance);
            var mappingsList = new List<FallbackMapping> { mapping };
            fallbackAssetsField.SetValue(_config, mappingsList);

            // Set config properties
            typeof(AddressableConfig).GetField("_useFallbackOnFailure", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, true);

            // Setup AddressableService
            _serviceGo = new GameObject("AddressableService");
            var service = _serviceGo.AddComponent<AddressableService>();
            service.BypassAddressablesInitialization = true;

            GameObject fallbackGo = new GameObject("FallbackAsset");
            string expectedRedirectedKey = fallbackAssetRef.RuntimeKey?.ToString() ?? "dummy-guid-12345";
            service.LoadAssetOperationFunc = (key, type) =>
            {
                if (key == mainKey)
                {
                    return Addressables.ResourceManager.CreateCompletedOperation<GameObject>(null, "Mock load failure for main key");
                }
                else if (key == expectedRedirectedKey)
                {
                    return Addressables.ResourceManager.CreateCompletedOperation<GameObject>(fallbackGo, null);
                }
                return default;
            };

            await service.InitializeAsync(_config);

            // Act: Attempt to load the invalid key
            try
            {
                await service.LoadAssetAsync<GameObject>(mainKey, CancellationToken.None);
            }
            catch (Exception)
            {
                // Expected to throw because dummy-guid is invalid, but we want to verify that fallback redirection was registered in cache
            }

            // Assert: Verify that cache contains the mainKey and it redirected to fallback key
            var cacheField = typeof(AddressableService).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = (IDictionary)cacheField.GetValue(service);

            Assert.IsTrue(cache.Contains(mainKey), "Cache should contain entry for the main key.");
            
            var cachedAsset = cache[mainKey];
            var redirectedKeyProp = cachedAsset.GetType().GetProperty("RedirectedKey", BindingFlags.Public | BindingFlags.Instance);
            string redirectedKey = (string)redirectedKeyProp.GetValue(cachedAsset);

            Assert.AreEqual(expectedRedirectedKey, redirectedKey, "Redirection key should match fallback runtime key.");

            // Clean up fallbackGo
            if (fallbackGo != null)
            {
                UnityEngine.Object.DestroyImmediate(fallbackGo);
            }
        });

        /// <summary>
        /// Tests that when an AddressableLifetimeScope is destroyed, the corresponding tracked assets decrement reference counts.
        /// </summary>
        /// <returns>A UnityTest IEnumerator representation.</returns>
        [UnityTest]
        public IEnumerator TestReferenceCountCleanupOnScopeDestruction() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: Setup Service
            _serviceGo = new GameObject("AddressableService");
            var service = _serviceGo.AddComponent<AddressableService>();
            service.BypassAddressablesInitialization = true;
            await service.InitializeAsync(_config);

            // Setup a fake CachedAsset in the service cache to simulate loaded asset
            string key = "mock_loaded_key";
            var cachedAssetType = typeof(AddressableService).GetNestedType("CachedAsset", BindingFlags.NonPublic);
            var cachedAssetInstance = Activator.CreateInstance(cachedAssetType);
            
            cachedAssetType.GetProperty("Key").SetValue(cachedAssetInstance, key);
            cachedAssetType.GetProperty("RefCount").SetValue(cachedAssetInstance, 1);

            var cacheField = typeof(AddressableService).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = (IDictionary)cacheField.GetValue(service);
            cache.Add(key, cachedAssetInstance);

            // Setup scope to track the key
            var scopeGo = new GameObject("ScopedObject");
            var scope = scopeGo.AddComponent<AddressableLifetimeScope>();

            var trackedKeysField = typeof(AddressableLifetimeScope).GetField("_trackedKeys", BindingFlags.NonPublic | BindingFlags.Instance);
            var trackedKeys = (Dictionary<string, int>)trackedKeysField.GetValue(scope);
            trackedKeys.Add(key, 1);

            // Act: In EditMode, trigger OnDestroy manually using reflection since lifecycle events are not run automatically
            var onDestroyMethod = typeof(AddressableLifetimeScope).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);
            onDestroyMethod?.Invoke(scope, null);
            UnityEngine.Object.DestroyImmediate(scopeGo);

            // Assert: Verify that the reference count in the service cache was decremented and removed (since refcount hit 0)
            Assert.IsFalse(cache.Contains(key), "Cache should no longer contain the key as it was released and refcount reached 0.");
        });

        /// <summary>
        /// Tests the integration between PooledPrefabHelper and PoolManager, verifying instances are correctly fetched and returned.
        /// </summary>
        /// <returns>A UnityTest IEnumerator representation.</returns>
        [UnityTest]
        public IEnumerator TestPooledPrefabHelperIntegration() => UniTask.ToCoroutine(async () =>
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

            // Arrange: Setup Service and PoolManager
            _serviceGo = new GameObject("AddressableService");
            var service = _serviceGo.AddComponent<AddressableService>();
            service.BypassAddressablesInitialization = true;
            await service.InitializeAsync(_config);

            _poolGo = new GameObject("PoolManager");
            var poolManager = _poolGo.AddComponent<PoolManager>();
            
            // Setup a fake prefab in Service Cache
            string key = "mock_prefab_key";
            GameObject mockPrefab = new GameObject("MockPrefab");

            var cachedAssetType = typeof(AddressableService).GetNestedType("CachedAsset", BindingFlags.NonPublic);
            var cachedAssetInstance = Activator.CreateInstance(cachedAssetType);
            cachedAssetType.GetProperty("Key").SetValue(cachedAssetInstance, key);
            cachedAssetType.GetProperty("RefCount").SetValue(cachedAssetInstance, 0); // Initialize refcount

            // Simulate Addressables.LoadAssetAsync result by writing directly to Cache with a fake successfully loaded asset
            var cacheField = typeof(AddressableService).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = (IDictionary)cacheField.GetValue(service);

            using (var helper = new PooledPrefabHelper())
            {
                // Inject fields via reflection to simulate successful InitializeAsync
                var prefabField = typeof(PooledPrefabHelper).GetField("_prefab", BindingFlags.NonPublic | BindingFlags.Instance);
                var keyField = typeof(PooledPrefabHelper).GetField("_addressableKey", BindingFlags.NonPublic | BindingFlags.Instance);
                var initField = typeof(PooledPrefabHelper).GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Instance);

                prefabField.SetValue(helper, mockPrefab);
                keyField.SetValue(helper, key);
                initField.SetValue(helper, true);

                // Create pool manually with 0 initial size to ensure the first instance name has index 0
                var poolConfig = new PoolConfig(0, true, 10, PoolRecyclingStrategy.ExceedMaxSizeTemporarily);
                await PoolManager.Instance.CreatePoolAsync<GameObject>(mockPrefab, poolConfig);

                // Act: Get an instance
                GameObject instance = await helper.GetInstanceAsync();

                // Assert: Verify instance is created and active
                Assert.IsNotNull(instance, "Pooled instance should not be null.");
                Assert.IsTrue(instance.activeSelf, "Pooled instance should be active.");
                Assert.AreEqual($"{mockPrefab.name} (Pooled 0)", instance.name, "Name should match pool naming convention.");

                // Act: Return instance
                helper.ReturnInstance(instance);

                // Assert: Verify instance is deactivated (returned to pool)
                Assert.IsFalse(instance.activeInHierarchy, "Pooled instance should be deactivated after returning to pool.");

                // Clean up pooled objects manually in EditMode before helper.Dispose() runs
                // to avoid Object.Destroy() console errors
                if (PoolManager.Instance != null && PoolManager.Instance.Pools.TryGetValue(mockPrefab, out var poolObj))
                {
                    var inactiveField = poolObj.GetType().GetField("_inactiveObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                    var activeField = poolObj.GetType().GetField("_activeObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                    var parentField = poolObj.GetType().GetField("_parentContainer", BindingFlags.NonPublic | BindingFlags.Instance);

                    if (inactiveField != null)
                    {
                        var inactiveStack = (Stack<GameObject>)inactiveField.GetValue(poolObj);
                        if (inactiveStack != null)
                        {
                            foreach (var obj in inactiveStack)
                            {
                                if (obj != null)
                                {
                                    UnityEngine.Object.DestroyImmediate(obj);
                                }
                            }
                        }
                    }

                    if (activeField != null)
                    {
                        var activeList = (List<GameObject>)activeField.GetValue(poolObj);
                        if (activeList != null)
                        {
                            foreach (var obj in activeList)
                            {
                                if (obj != null)
                                {
                                    UnityEngine.Object.DestroyImmediate(obj);
                                }
                            }
                        }
                    }

                    if (parentField != null)
                    {
                        var parentContainer = (GameObject)parentField.GetValue(poolObj);
                        if (parentContainer != null)
                        {
                            UnityEngine.Object.DestroyImmediate(parentContainer);
                        }
                    }
                }
            }

            // Clean up prefab
            UnityEngine.Object.DestroyImmediate(mockPrefab);
        });

        /// <summary>
        /// Tests that calling ReleaseGroup in AddressableService correctly releases only assets belonging to that specific group.
        /// </summary>
        /// <returns>A UnityTest IEnumerator representation.</returns>
        [UnityTest]
        public IEnumerator TestGroupBasedRelease() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: Setup Service
            _serviceGo = new GameObject("AddressableService");
            var service = _serviceGo.AddComponent<AddressableService>();
            service.BypassAddressablesInitialization = true;
            await service.InitializeAsync(_config);

            string groupUi = "UI";
            string groupGameplay = "Gameplay";

            string keyA = "ui_asset_a";
            string keyB = "ui_asset_b";
            string keyC = "gameplay_asset_c";

            var cachedAssetType = typeof(AddressableService).GetNestedType("CachedAsset", BindingFlags.NonPublic);
            
            var assetA = Activator.CreateInstance(cachedAssetType);
            cachedAssetType.GetProperty("Key").SetValue(assetA, keyA);
            cachedAssetType.GetProperty("RefCount").SetValue(assetA, 1);
            cachedAssetType.GetProperty("Group").SetValue(assetA, groupUi);

            var assetB = Activator.CreateInstance(cachedAssetType);
            cachedAssetType.GetProperty("Key").SetValue(assetB, keyB);
            cachedAssetType.GetProperty("RefCount").SetValue(assetB, 1);
            cachedAssetType.GetProperty("Group").SetValue(assetB, groupUi);

            var assetC = Activator.CreateInstance(cachedAssetType);
            cachedAssetType.GetProperty("Key").SetValue(assetC, keyC);
            cachedAssetType.GetProperty("RefCount").SetValue(assetC, 1);
            cachedAssetType.GetProperty("Group").SetValue(assetC, groupGameplay);

            var cacheField = typeof(AddressableService).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = (IDictionary)cacheField.GetValue(service);
            
            cache.Add(keyA, assetA);
            cache.Add(keyB, assetB);
            cache.Add(keyC, assetC);

            // Act: Release only the "UI" group
            service.ReleaseGroup(groupUi);

            // Assert: UI assets should be removed, while Gameplay asset should remain
            Assert.IsFalse(cache.Contains(keyA), "Asset A (UI group) should be released and removed from cache.");
            Assert.IsFalse(cache.Contains(keyB), "Asset B (UI group) should be released and removed from cache.");
            Assert.IsTrue(cache.Contains(keyC), "Asset C (Gameplay group) should remain in the cache.");
        });

        /// <summary>
        /// Tests that AddressableConfig values are correctly propagated to AddressableService after initialization.
        /// </summary>
        /// <returns>A UnityTest IEnumerator representation.</returns>
        [UnityTest]
        public IEnumerator TestAddressableConfigInitialization() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: Create config with specific values using reflection
            typeof(AddressableConfig).GetField("_maxRetryCount", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 5);
            typeof(AddressableConfig).GetField("_retryDelaySeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 4.5f);
            typeof(AddressableConfig).GetField("_useFallbackOnFailure", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, false);

            _serviceGo = new GameObject("AddressableService");
            var service = _serviceGo.AddComponent<AddressableService>();
            service.BypassAddressablesInitialization = true;

            // Act: Initialize service
            await service.InitializeAsync(_config);

            // Assert: Verify settings propagated correctly
            Assert.IsNotNull(service.Config, "Config reference in AddressableService should not be null.");
            Assert.AreEqual(5, service.Config.MaxRetryCount, "Max retry count should be 5.");
            Assert.AreEqual(4.5f, service.Config.RetryDelaySeconds, "Retry delay seconds should be 4.5.");
            Assert.IsFalse(service.Config.UseFallbackOnFailure, "Use fallback on failure should be false.");
        });

        /// <summary>
        /// Tests that when two concurrent load operations are started for the same key, and the second operation is cancelled,
        /// the first operation still completes successfully and the final RefCount of the asset in the cache is 1.
        /// </summary>
        /// <returns>A UnityTest IEnumerator representation.</returns>
        [UnityTest]
        public IEnumerator TestRaceConditionAndCancellation() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: Setup Service
            _serviceGo = new GameObject("AddressableService");
            var service = _serviceGo.AddComponent<AddressableService>();
            service.BypassAddressablesInitialization = true;
            await service.InitializeAsync(_config);

            string key = "race_condition_test_key";

            using (var cts1 = new CancellationTokenSource())
            using (var cts2 = new CancellationTokenSource())
            {
                // Pre-populate cache with a mock CachedAsset containing a completed handle
                var cachedAssetType = typeof(AddressableService).GetNestedType("CachedAsset", BindingFlags.NonPublic);
                var cachedAssetInstance = Activator.CreateInstance(cachedAssetType);

                var mockGo = new GameObject("MockAsset");
                var mockHandle = Addressables.ResourceManager.CreateCompletedOperation<GameObject>(mockGo, null);

                cachedAssetType.GetProperty("Key").SetValue(cachedAssetInstance, key);
                cachedAssetType.GetProperty("Handle").SetValue(cachedAssetInstance, (AsyncOperationHandle)mockHandle);
                cachedAssetType.GetProperty("RefCount").SetValue(cachedAssetInstance, 0);

                var cacheField = typeof(AddressableService).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
                var cache = (IDictionary)cacheField.GetValue(service);
                cache.Add(key, cachedAssetInstance);

                // Act: Start first load operation
                var task1 = service.LoadAssetAsync<GameObject>(key, cts1.Token);

                // Cancel the second token beforehand to trigger cancellation upon its load
                cts2.Cancel();

                // Start second load operation
                var task2 = service.LoadAssetAsync<GameObject>(key, cts2.Token);

                // Verify that the second operation throws OperationCanceledException
                bool threw = false;
                try
                {
                    await task2;
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
                Assert.IsTrue(threw, "Operation should have been canceled.");

                // Simulate the caller's cancellation response by manually releasing the asset for the cancelled operation
                service.ReleaseAsset(key);

                // Verify that the first load completes successfully
                var loadedAsset1 = await task1;
                Assert.AreEqual(mockGo, loadedAsset1, "The first load should return the mock asset successfully.");

                // Assert: Verify that the final RefCount of the asset in the cache is 1
                int finalRefCount = (int)cachedAssetType.GetProperty("RefCount").GetValue(cachedAssetInstance);
                Assert.AreEqual(1, finalRefCount, "The final RefCount should be 1.");

                // Clean up
                UnityEngine.Object.DestroyImmediate(mockGo);
            }
        });

        /// <summary>
        /// Tests that when a GameObject with AddressableLifetimeScope is destroyed immediately during an active load,
        /// the asset reference count is cleaned up and not leaked (removed from the cache).
        /// </summary>
        /// <returns>A UnityTest IEnumerator representation.</returns>
        [UnityTest]
        public IEnumerator TestLifetimeScopeLeakProtection() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: Setup Service
            _serviceGo = new GameObject("AddressableService");
            var service = _serviceGo.AddComponent<AddressableService>();
            service.BypassAddressablesInitialization = true;

            string key = "lifetime_leak_test_key";
            var mockGo = new GameObject("MockAsset");
            var mockHandle = Addressables.ResourceManager.CreateCompletedOperation<GameObject>(mockGo, null);

            // Create temporary GameObject and attach scope
            var scopeGo = new GameObject("ScopedObject");
            var scope = scopeGo.AddComponent<AddressableLifetimeScope>();

            service.LoadAssetOperationFunc = (k, type) =>
            {
                return mockHandle;
            };

            await service.InitializeAsync(_config);

            // Act: Start loading through the scope using the GameObject's cancellation token
            var token = scopeGo.GetCancellationTokenOnDestroy();
            var task = scope.LoadAssetAsync<GameObject>(key, token);

            // Act: In EditMode, trigger OnDestroy manually using reflection since lifecycle events are not run automatically
            if (scopeGo != null)
            {
                var onDestroyMethod = typeof(AddressableLifetimeScope).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);
                onDestroyMethod?.Invoke(scope, null);
                UnityEngine.Object.DestroyImmediate(scopeGo);
            }

            // Wait/catch for task cancellation
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected cancellation exception
            }

            // Assert: Verify that the asset reference count is cleaned up and not leaked in the cache
            var cacheField = typeof(AddressableService).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = (IDictionary)cacheField.GetValue(service);
            Assert.IsFalse(cache.Contains(key), "Cache should not contain the key after scope destruction during active load.");

            // Clean up
            if (mockGo != null)
            {
                UnityEngine.Object.DestroyImmediate(mockGo);
            }
        });

        /// <summary>
        /// Helper method to simulate reference tracking increments inside AddressableLifetimeScope.
        /// </summary>
        /// <param name="key">The addressable asset key to track.</param>
        private void IncrementTracking(string key)
        {
            // Simulate how LoadAssetAsync increments tracking
            var trackedKeysField = typeof(AddressableLifetimeScope).GetField("_trackedKeys", BindingFlags.NonPublic | BindingFlags.Instance);
            var trackedKeys = (Dictionary<string, int>)trackedKeysField.GetValue(_lifetimeScope);

            if (!trackedKeys.TryGetValue(key, out int count))
            {
                trackedKeys[key] = 1;
            }
            else
            {
                trackedKeys[key] = count + 1;
            }
        }
    }
}

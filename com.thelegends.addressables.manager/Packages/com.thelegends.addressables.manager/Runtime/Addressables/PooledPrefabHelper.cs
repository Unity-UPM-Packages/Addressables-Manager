// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using com.thelegends.unity.pooling;

namespace com.thelegends.addressables.manager
{
    /// <summary>
    /// Helper adapter class that manages loading a prefab via Addressables and integrating it with the pooling system.
    /// Implements <see cref="IDisposable"/> to cleanly tear down the object pool and release addressable resources.
    /// </summary>
    public sealed class PooledPrefabHelper : IDisposable
    {
        private GameObject _prefab;
        private string _addressableKey;
        private bool _isInitialized;
        private bool _isUiPool;

        /// <summary>
        /// Gets a value indicating whether this helper has been initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Gets the loaded prefab GameObject reference.
        /// </summary>
        public GameObject Prefab => _prefab;

        /// <summary>
        /// Gets the addressable key used for loading this prefab.
        /// </summary>
        public string AddressableKey => _addressableKey;

        /// <summary>
        /// Gets a value indicating whether this pool is a UI pool.
        /// </summary>
        public bool IsUiPool => _isUiPool;

        /// <summary>
        /// Initializes the helper by loading the prefab via addressable key and creating standard or UI pool.
        /// </summary>
        /// <param name="key">The addressable key identifying the prefab.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <param name="poolConfig">Optional custom pool configuration.</param>
        /// <param name="group">Optional group identifier for Addressables tracking.</param>
        /// <param name="isUiPool">Whether the pool should be registered as a UI pool.</param>
        /// <param name="parentTransform">The parent RectTransform if registering a UI pool.</param>
        /// <returns>A UniTask representing the asynchronous initialization operation.</returns>
        public async UniTask InitializeAsync(
            string key,
            CancellationToken cancellationToken,
            PoolConfig? poolConfig = null,
            string group = null,
            bool isUiPool = false,
            RectTransform parentTransform = null)
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException("PooledPrefabHelper is already initialized.");
            }

            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key), "Asset key cannot be null or empty.");
            }

            _addressableKey = key;
            _isUiPool = isUiPool;

            if (AddressableService.Instance == null)
            {
                throw new InvalidOperationException("AddressableService is not initialized.");
            }

            try
            {
                _prefab = await AddressableService.Instance.LoadAssetAsync<GameObject>(key, cancellationToken, group);

                if (_prefab == null)
                {
                    throw new InvalidOperationException($"Failed to load prefab with key: {key}");
                }

                if (PoolManager.Instance == null)
                {
                    throw new InvalidOperationException("PoolManager is not initialized.");
                }

                if (_isUiPool)
                {
                    if (parentTransform == null)
                    {
                        throw new ArgumentNullException(nameof(parentTransform), "parentTransform cannot be null for UI pools.");
                    }
                    await PoolManager.Instance.CreateUIPoolAsync<GameObject>(_prefab, parentTransform, poolConfig).AsUniTask().AttachExternalCancellation(cancellationToken);
                }
                else
                {
                    await PoolManager.Instance.CreatePoolAsync<GameObject>(_prefab, poolConfig).AsUniTask().AttachExternalCancellation(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // If LoadAssetAsync or CreatePoolAsync is canceled:
                // 1. If prefab was loaded successfully, we must release it.
                // 2. If prefab loading was canceled, AddressableService already decremented RefCount.
                // In either case, we reset our local fields to avoid double-release during subsequent Dispose().
                if (_prefab != null && AddressableService.Instance != null)
                {
                    AddressableService.Instance.ReleaseAsset(_addressableKey);
                }
                _prefab = null;
                _addressableKey = null;
                throw;
            }
            catch (Exception)
            {
                // For other exceptions:
                // 1. If prefab was loaded successfully, we must release it.
                // 2. If LoadAssetAsync failed, AddressableService already decremented RefCount.
                if (_prefab != null && AddressableService.Instance != null)
                {
                    AddressableService.Instance.ReleaseAsset(_addressableKey);
                }
                _prefab = null;
                _addressableKey = null;
                throw;
            }

            _isInitialized = true;
        }

        /// <summary>
        /// Initializes the helper by loading the prefab via AssetReference and creating a standard or UI pool.
        /// </summary>
        /// <param name="assetReference">The AssetReference of the prefab.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <param name="poolConfig">Optional custom pool configuration.</param>
        /// <param name="group">Optional group identifier for Addressables tracking.</param>
        /// <param name="isUiPool">Whether the pool should be registered as a UI pool.</param>
        /// <param name="parentTransform">The parent RectTransform if registering a UI pool.</param>
        /// <returns>A UniTask representing the asynchronous initialization operation.</returns>
        public async UniTask InitializeAsync(
            AssetReference assetReference,
            CancellationToken cancellationToken,
            PoolConfig? poolConfig = null,
            string group = null,
            bool isUiPool = false,
            RectTransform parentTransform = null)
        {
            if (assetReference == null)
            {
                throw new ArgumentNullException(nameof(assetReference), "AssetReference cannot be null.");
            }

            object runtimeKey = assetReference.RuntimeKey;
            string key = runtimeKey is string stringKey ? stringKey : runtimeKey?.ToString();
            await InitializeAsync(key, cancellationToken, poolConfig, group, isUiPool, parentTransform);
        }

        /// <summary>
        /// Gets a pooled instance asynchronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to cancel the retrieval operation.</param>
        /// <returns>A UniTask returning the pooled GameObject instance.</returns>
        public async UniTask<GameObject> GetInstanceAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            GameObject instance = await PoolManager.Instance.GetAsync<GameObject>(_prefab).AsUniTask().AttachExternalCancellation(cancellationToken);
            return instance;
        }

        /// <summary>
        /// Gets a pooled instance asynchronously and retrieves the specified component.
        /// Throws a <see cref="MissingComponentException"/> if the component does not exist.
        /// </summary>
        /// <typeparam name="TComponent">The type of the component to retrieve.</typeparam>
        /// <param name="cancellationToken">The cancellation token to cancel the retrieval operation.</param>
        /// <returns>A UniTask returning the component instance.</returns>
        public async UniTask<TComponent> GetInstanceAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : Component
        {
            GameObject instance = await GetInstanceAsync(cancellationToken);
            if (instance == null)
            {
                return null;
            }

            if (instance.TryGetComponent<TComponent>(out TComponent component))
            {
                return component;
            }

            // Return instance immediately to avoid memory leaks if component is missing
            ReturnInstance(instance);
            throw new MissingComponentException($"Component of type {typeof(TComponent).Name} is missing on the pooled prefab.");
        }

        /// <summary>
        /// Gets a pooled instance synchronously.
        /// </summary>
        /// <returns>The pooled GameObject instance.</returns>
        public GameObject GetInstance()
        {
            EnsureInitialized();

            return PoolManager.Instance.Get<GameObject>(_prefab);
        }

        /// <summary>
        /// Gets a pooled instance synchronously and retrieves the specified component.
        /// Throws a <see cref="MissingComponentException"/> if the component does not exist.
        /// </summary>
        /// <typeparam name="TComponent">The type of the component to retrieve.</typeparam>
        /// <returns>The component instance.</returns>
        public TComponent GetInstance<TComponent>() where TComponent : Component
        {
            GameObject instance = GetInstance();
            if (instance == null)
            {
                return null;
            }

            if (instance.TryGetComponent<TComponent>(out TComponent component))
            {
                return component;
            }

            // Return instance immediately to avoid memory leaks if component is missing
            ReturnInstance(instance);
            throw new MissingComponentException($"Component of type {typeof(TComponent).Name} is missing on the pooled prefab.");
        }

        /// <summary>
        /// Returns an instance to the pool.
        /// </summary>
        /// <param name="instance">The GameObject instance to return.</param>
        public void ReturnInstance(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (PoolManager.Instance != null)
            {
                PoolManager.Instance.ReturnToPool(instance);
            }
        }

        /// <summary>
        /// Disposes of the helper, clearing the pool from PoolManager and releasing the prefab in AddressableService.
        /// </summary>
        public void Dispose()
        {
            if (PoolManager.Instance != null && _prefab != null)
            {
                PoolManager.Instance.ClearPool<GameObject>(_prefab, isUIPool: _isUiPool);
            }

            if (AddressableService.Instance != null && !string.IsNullOrEmpty(_addressableKey))
            {
                AddressableService.Instance.ReleaseAsset(_addressableKey);
            }

            _prefab = null;
            _addressableKey = null;
            _isInitialized = false;
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("PooledPrefabHelper is not initialized. Call InitializeAsync first.");
            }
        }
    }
}

// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Scripting.APIUpdating;
using TheLegends.Base.Pool;

namespace TheLegends.Base.Addressables
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
        private ComponentPool<Transform> _pool;

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
        /// <param name="collectionCheck">Collection checks will throw errors if you try to release an item already in the pool.</param>
        /// <param name="defaultCapacity">The default capacity the pool will be created with.</param>
        /// <param name="maxSize">The maximum size of the pool.</param>
        /// <param name="group">Optional group identifier for Addressables tracking.</param>
        /// <param name="isUiPool">Whether the pool should be registered as a UI pool.</param>
        /// <param name="parentTransform">The parent RectTransform if registering a UI pool.</param>
        /// <returns>A UniTask representing the asynchronous initialization operation.</returns>
        public async UniTask InitializeAsync(
            string key,
            CancellationToken cancellationToken,
            bool collectionCheck = true,
            int defaultCapacity = 10,
            int maxSize = 10000,
            string group = null,
            bool isUiPool = false,
            Transform parentTransform = null)
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

                if (_isUiPool && parentTransform == null)
                {
                    throw new ArgumentNullException(nameof(parentTransform), "parentTransform cannot be null for UI pools.");
                }

                _pool = new ComponentPool<Transform>(_prefab.transform, parentTransform, collectionCheck, defaultCapacity, maxSize);
            }
            catch (OperationCanceledException)
            {
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
        /// <param name="collectionCheck">Collection checks will throw errors if you try to release an item already in the pool.</param>
        /// <param name="defaultCapacity">The default capacity the pool will be created with.</param>
        /// <param name="maxSize">The maximum size of the pool.</param>
        /// <param name="group">Optional group identifier for Addressables tracking.</param>
        /// <param name="isUiPool">Whether the pool should be registered as a UI pool.</param>
        /// <param name="parentTransform">The parent RectTransform if registering a UI pool.</param>
        /// <returns>A UniTask representing the asynchronous initialization operation.</returns>
        public async UniTask InitializeAsync(
            AssetReference assetReference,
            CancellationToken cancellationToken,
            bool collectionCheck = true,
            int defaultCapacity = 10,
            int maxSize = 10000,
            string group = null,
            bool isUiPool = false,
            Transform parentTransform = null)
        {
            if (assetReference == null)
            {
                throw new ArgumentNullException(nameof(assetReference), "AssetReference cannot be null.");
            }

            object runtimeKey = assetReference.RuntimeKey;
            string key = runtimeKey is string stringKey ? stringKey : runtimeKey?.ToString();
            await InitializeAsync(key, cancellationToken, collectionCheck, defaultCapacity, maxSize, group, isUiPool, parentTransform);
        }

        /// <summary>
        /// Gets a pooled instance asynchronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to cancel the retrieval operation.</param>
        /// <returns>A UniTask returning the pooled GameObject instance.</returns>
        public UniTask<GameObject> GetInstanceAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled<GameObject>(cancellationToken);
            }

            Transform instanceTransform = _pool.Get();
            if (instanceTransform == null)
            {
                return UniTask.FromResult<GameObject>(null);
            }

            GameObject instance = instanceTransform.gameObject;

            // Manually call OnSpawn on IPoolable components since Transform doesn't implement IPoolable directly
            var poolables = instance.GetComponentsInChildren<IPoolable>(true);
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i]?.OnSpawn();
            }

            return UniTask.FromResult(instance);
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

            Transform instanceTransform = _pool.Get();
            if (instanceTransform == null)
            {
                return null;
            }

            GameObject instance = instanceTransform.gameObject;

            var poolables = instance.GetComponentsInChildren<IPoolable>(true);
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i]?.OnSpawn();
            }

            return instance;
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

            if (_pool != null)
            {
                var poolables = instance.GetComponentsInChildren<IPoolable>(true);
                for (int i = 0; i < poolables.Length; i++)
                {
                    poolables[i]?.OnDespawn();
                }

                _pool.Release(instance.transform);
            }
        }

        /// <summary>
        /// Clear of the helper, clearing the pool and releasing the prefab in AddressableService.
        /// </summary>
        public void Dispose()
        {
            if (_pool != null)
            {
                _pool.Clear();
                _pool = null;
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

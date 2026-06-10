// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Scripting.APIUpdating;
using TheLegends.Base.UnitySingleton;
namespace TheLegends.Base.Addressables
{
    using Addressables = UnityEngine.AddressableAssets.Addressables;

    /// <summary>
    /// Service manager for handling Addressables loading, caching, reference counting, retries, and fallback assets.
    /// </summary>
    public sealed class AddressableService : PersistentMonoSingleton<AddressableService>
    {
        private readonly Dictionary<string, CachedAsset> _cache = new Dictionary<string, CachedAsset>();
        private readonly Dictionary<string, AssetReference> _fallbackCache = new Dictionary<string, AssetReference>();
        private readonly List<string> _tempKeysToRemove = new List<string>();

        private AddressableConfig _config;

        /// <summary>
        /// Gets or sets a value indicating whether to bypass the actual Addressables.InitializeAsync call.
        /// Primarily used for testing in environments without Addressable settings.
        /// </summary>
        public bool BypassAddressablesInitialization { get; set; }

        /// <summary>
        /// Gets or sets a delegate to override the actual Addressables loading operations during unit testing.
        /// </summary>
        internal Func<string, Type, AsyncOperationHandle> LoadAssetOperationFunc { get; set; }

        /// <summary>
        /// Gets the configuration settings of the Addressables service.
        /// </summary>
        public AddressableConfig Config => _config;

        /// <summary>
        /// Initializes the Addressables service, caching configuration and initializing Addressables.
        /// </summary>
        /// <param name="config">The Addressables configuration ScriptableObject.</param>
        /// <returns>A UniTask representing the asynchronous initialization operation.</returns>
        public async UniTask InitializeAsync()
        {
            _config = AddressableConfig.Instance;
            _fallbackCache.Clear();

            if (_config != null && _config.FallbackAssets != null)
            {
                int fallbackCount = _config.FallbackAssets.Count;
                for (int i = 0; i < fallbackCount; i++)
                {
                    FallbackMapping mapping = _config.FallbackAssets[i];
                    if (!string.IsNullOrEmpty(mapping.Key) && mapping.FallbackAsset != null)
                    {
                        _fallbackCache[mapping.Key] = mapping.FallbackAsset;
                    }
                }
            }

            if (!BypassAddressablesInitialization)
            {
                await Addressables.InitializeAsync().ToUniTask();
            }
        }

        /// <summary>
        /// Loads an asset by its addressable key asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="key">The addressable key of the asset.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <param name="group">Optional group identifier for categorizing/releasing assets together.</param>
        /// <returns>A UniTask returning the loaded asset.</returns>
        public UniTask<T> LoadAssetAsync<T>(string key, CancellationToken cancellationToken, string group = null) where T : UnityEngine.Object
        {
            return LoadAssetInternalAsync<T>(key, cancellationToken, group);
        }

        /// <summary>
        /// Loads an asset by its AssetReference asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="assetReference">The AssetReference of the asset.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <param name="group">Optional group identifier for categorizing/releasing assets together.</param>
        /// <returns>A UniTask returning the loaded asset.</returns>
        public UniTask<T> LoadAssetAsync<T>(AssetReference assetReference, CancellationToken cancellationToken, string group = null) where T : UnityEngine.Object
        {
            if (assetReference == null)
            {
                throw new ArgumentNullException(nameof(assetReference), "AssetReference cannot be null.");
            }
            string normalizedKey = NormalizeKey(assetReference);
            return LoadAssetInternalAsync<T>(normalizedKey, cancellationToken, group);
        }

        /// <summary>
        /// Releases an asset using its addressable key.
        /// </summary>
        /// <param name="key">The lookup key (string).</param>
        public void ReleaseAsset(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (_cache.TryGetValue(key, out CachedAsset cachedAsset))
            {
                cachedAsset.RefCount--;
                if (cachedAsset.RefCount <= 0)
                {
                    if (!string.IsNullOrEmpty(cachedAsset.RedirectedKey))
                    {
                        ReleaseAsset(cachedAsset.RedirectedKey);
                    }
                    else if (cachedAsset.Handle.IsValid())
                    {
                        Addressables.Release(cachedAsset.Handle);
                    }
                    _cache.Remove(key);
                }
            }
        }

        /// <summary>
        /// Releases an asset using its AssetReference.
        /// </summary>
        /// <param name="assetReference">The AssetReference of the asset.</param>
        public void ReleaseAsset(AssetReference assetReference)
        {
            if (assetReference == null)
            {
                return;
            }
            string key = NormalizeKey(assetReference);
            ReleaseAsset(key);
        }

        /// <summary>
        /// Releases an asset using its addressable key or AssetReference.
        /// </summary>
        /// <param name="key">The lookup key (string or AssetReference).</param>
        public void ReleaseAsset(object key)
        {
            if (key == null)
            {
                return;
            }

            if (key is string stringKey)
            {
                ReleaseAsset(stringKey);
            }
            else if (key is AssetReference assetReference)
            {
                ReleaseAsset(assetReference);
            }
            else
            {
                ReleaseAsset(key.ToString());
            }
        }

        /// <summary>
        /// Releases all assets belonging to the specified group.
        /// </summary>
        /// <param name="group">The group identifier.</param>
        public void ReleaseGroup(string group)
        {
            if (string.IsNullOrEmpty(group))
            {
                return;
            }

            _tempKeysToRemove.Clear();

            foreach (KeyValuePair<string, CachedAsset> pair in _cache)
            {
                if (pair.Value.Group == group)
                {
                    _tempKeysToRemove.Add(pair.Key);
                }
            }

            int count = _tempKeysToRemove.Count;
            for (int i = 0; i < count; i++)
            {
                string key = _tempKeysToRemove[i];
                if (_cache.TryGetValue(key, out CachedAsset cachedAsset))
                {
                    if (!string.IsNullOrEmpty(cachedAsset.RedirectedKey))
                    {
                        ReleaseAsset(cachedAsset.RedirectedKey);
                    }
                    else if (cachedAsset.Handle.IsValid())
                    {
                        Addressables.Release(cachedAsset.Handle);
                    }
                    _cache.Remove(key);
                }
            }

            _tempKeysToRemove.Clear();
        }

        /// <summary>
        /// Synchronously loads an asset by its key. Obsolete. Use LoadAssetAsync instead.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="key">The key of the asset to load (string or AssetReference).</param>
        /// <returns>The loaded asset.</returns>
        [Obsolete("Use LoadAssetAsync instead.")]
        public T LoadAssetSync<T>(object key) where T : UnityEngine.Object
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key), "Asset key cannot be null.");
            }

            string normalizedKey = key is AssetReference assetRef ? NormalizeKey(assetRef) : key.ToString();
            if (string.IsNullOrEmpty(normalizedKey))
            {
                throw new ArgumentException("Asset key cannot be null or empty.", nameof(key));
            }

            if (_cache.TryGetValue(normalizedKey, out CachedAsset cachedAsset))
            {
                cachedAsset.RefCount++;
                try
                {
                    if (!string.IsNullOrEmpty(cachedAsset.RedirectedKey))
                    {
                        return LoadAssetSync<T>(cachedAsset.RedirectedKey);
                    }
                    return cachedAsset.Handle.Convert<T>().WaitForCompletion();
                }
                catch (Exception)
                {
                    cachedAsset.RefCount--;
                    if (cachedAsset.RefCount <= 0)
                    {
                        if (!string.IsNullOrEmpty(cachedAsset.RedirectedKey))
                        {
                            ReleaseAsset(cachedAsset.RedirectedKey);
                        }
                        else if (cachedAsset.Handle.IsValid())
                        {
                            Addressables.Release(cachedAsset.Handle);
                        }
                        _cache.Remove(normalizedKey);
                    }
                    throw;
                }
            }

            AsyncOperationHandle<T> handle;
            if (LoadAssetOperationFunc != null)
            {
                handle = LoadAssetOperationFunc(normalizedKey, typeof(T)).Convert<T>();
            }
            else
            {
                handle = Addressables.LoadAssetAsync<T>(normalizedKey);
            }
            CachedAsset newCachedAsset = new CachedAsset
            {
                Key = normalizedKey,
                Handle = handle,
                RefCount = 1,
                Group = null
            };
            _cache.Add(normalizedKey, newCachedAsset);

            try
            {
                return handle.WaitForCompletion();
            }
            catch (Exception ex)
            {
                newCachedAsset.RefCount--;
                if (newCachedAsset.RefCount <= 0)
                {
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                    _cache.Remove(normalizedKey);
                }

                if (_config != null && _config.UseFallbackOnFailure && _fallbackCache.TryGetValue(normalizedKey, out AssetReference fallbackAsset))
                {
                    Debug.LogWarning($"[AddressableService] Failed to load '{normalizedKey}' synchronously, falling back to alternative asset. Error: {ex.Message}");

                    string fallbackKey = NormalizeKey(fallbackAsset);
                    T fallbackAssetValue = LoadAssetSync<T>(fallbackAsset);

                    CachedAsset redirectCachedAsset = new CachedAsset
                    {
                        Key = normalizedKey,
                        RefCount = 1,
                        Group = null,
                        RedirectedKey = fallbackKey
                    };
                    _cache[normalizedKey] = redirectCachedAsset;

                    return fallbackAssetValue;
                }

                Debug.LogError($"[AddressableService] Failed to load asset synchronously with key '{normalizedKey}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Cleans up cached assets when the singleton is cleared.
        /// </summary>
        public override void ClearSingleton()
        {
            base.ClearSingleton();
            CleanupCache();
        }

        /// <summary>
        /// Cleans up cached assets when the component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            CleanupCache();
        }

        /// <summary>
        /// Releases all active Addressables handles and clears internal caches.
        /// </summary>
        private void CleanupCache()
        {
            foreach (CachedAsset cachedAsset in _cache.Values)
            {
                if (string.IsNullOrEmpty(cachedAsset.RedirectedKey) && cachedAsset.Handle.IsValid())
                {
                    Addressables.Release(cachedAsset.Handle);
                }
            }
            _cache.Clear();
            _fallbackCache.Clear();
            _tempKeysToRemove.Clear();
        }

        private async UniTask<T> LoadAssetInternalAsync<T>(string normalizedKey, CancellationToken cancellationToken, string group = null) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(normalizedKey))
            {
                throw new ArgumentNullException(nameof(normalizedKey), "Asset key cannot be null or empty.");
            }

            if (_cache.TryGetValue(normalizedKey, out CachedAsset cachedAsset))
            {
                cachedAsset.RefCount++;
                try
                {
                    if (!string.IsNullOrEmpty(cachedAsset.RedirectedKey))
                    {
                        return await LoadAssetInternalAsync<T>(cachedAsset.RedirectedKey, cancellationToken, group);
                    }
                    return await cachedAsset.Handle.Convert<T>().ToUniTask(cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    cachedAsset.RefCount--;
                    if (cachedAsset.RefCount <= 0)
                    {
                        if (!string.IsNullOrEmpty(cachedAsset.RedirectedKey))
                        {
                            ReleaseAsset(cachedAsset.RedirectedKey);
                        }
                        else if (cachedAsset.Handle.IsValid())
                        {
                            Addressables.Release(cachedAsset.Handle);
                        }
                        _cache.Remove(normalizedKey);
                    }
                    throw;
                }
            }

            AsyncOperationHandle<T> handle;
            if (LoadAssetOperationFunc != null)
            {
                handle = LoadAssetOperationFunc(normalizedKey, typeof(T)).Convert<T>();
            }
            else
            {
                handle = Addressables.LoadAssetAsync<T>(normalizedKey);
            }
            CachedAsset newCachedAsset = new CachedAsset
            {
                Key = normalizedKey,
                Handle = handle,
                RefCount = 1,
                Group = group
            };
            _cache.Add(normalizedKey, newCachedAsset);

            try
            {
                return await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                newCachedAsset.RefCount--;
                if (newCachedAsset.RefCount <= 0)
                {
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                    _cache.Remove(normalizedKey);
                }

                if (_config != null && _config.UseFallbackOnFailure && _fallbackCache.TryGetValue(normalizedKey, out AssetReference fallbackAsset))
                {
                    Debug.LogWarning($"[AddressableService] Failed to load '{normalizedKey}', falling back to alternative asset. Error: {ex.Message}");

                    string fallbackKey = NormalizeKey(fallbackAsset);
                    T fallbackAssetValue = await LoadAssetAsync<T>(fallbackAsset, cancellationToken, group);

                    CachedAsset redirectCachedAsset = new CachedAsset
                    {
                        Key = normalizedKey,
                        RefCount = 1,
                        Group = group,
                        RedirectedKey = fallbackKey
                    };
                    _cache[normalizedKey] = redirectCachedAsset;

                    return fallbackAssetValue;
                }

                Debug.LogError($"[AddressableService] Failed to load asset with key '{normalizedKey}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Normalizes the given AssetReference to a string representation.
        /// </summary>
        /// <param name="assetReference">The AssetReference to normalize.</param>
        /// <returns>The normalized string key, or null if the AssetReference is null.</returns>
        private string NormalizeKey(AssetReference assetReference)
        {
            if (assetReference == null)
            {
                return null;
            }
            return assetReference.RuntimeKey?.ToString();
        }

        /// <summary>
        /// Represents an entry in the addressable service cache containing reference counting and group tracking.
        /// </summary>
        private sealed class CachedAsset
        {
            /// <summary>
            /// Gets or sets the lookup key of the cached asset.
            /// </summary>
            public string Key { get; set; }

            /// <summary>
            /// Gets or sets the asynchronous operation handle for the asset.
            /// </summary>
            public AsyncOperationHandle Handle { get; set; }

            /// <summary>
            /// Gets or sets the reference count of the cached asset.
            /// </summary>
            public int RefCount { get; set; }

            /// <summary>
            /// Gets or sets the group name the cached asset belongs to.
            /// </summary>
            public string Group { get; set; }

            /// <summary>
            /// Gets or sets the redirected key if this asset fell back to an alternative asset.
            /// </summary>
            public string RedirectedKey { get; set; }
        }
    }
}

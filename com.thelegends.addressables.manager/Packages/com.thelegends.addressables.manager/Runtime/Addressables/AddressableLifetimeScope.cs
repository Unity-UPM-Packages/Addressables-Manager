// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Scripting.APIUpdating;

namespace TheLegends.Base.Addressables
{
    /// <summary>
    /// Component that automatically tracks and releases loaded addressable assets bound to a GameObject's lifecycle.
    /// Handles double-release prevention and clean-up during cancellation or destruction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AddressableLifetimeScope : MonoBehaviour
    {
        private readonly Dictionary<string, int> _trackedKeys = new Dictionary<string, int>();

        /// <summary>
        /// Loads an asset by its addressable key asynchronously, bound to the GameObject's lifetime.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="key">The addressable key of the asset.</param>
        /// <param name="group">Optional group identifier.</param>
        /// <returns>A UniTask returning the loaded asset.</returns>
        public UniTask<T> LoadAssetAsync<T>(string key, string group = null) where T : UnityEngine.Object
        {
            return LoadAssetAsync<T>(key, this.GetCancellationTokenOnDestroy(), group);
        }

        /// <summary>
        /// Loads an asset by its AssetReference asynchronously, bound to the GameObject's lifetime.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="assetReference">The AssetReference of the asset.</param>
        /// <param name="group">Optional group identifier.</param>
        /// <returns>A UniTask returning the loaded asset.</returns>
        public UniTask<T> LoadAssetAsync<T>(AssetReference assetReference, string group = null) where T : UnityEngine.Object
        {
            return LoadAssetAsync<T>(assetReference, this.GetCancellationTokenOnDestroy(), group);
        }

        /// <summary>
        /// Loads an asset by its addressable key asynchronously with a custom CancellationToken.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="key">The addressable key of the asset.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <param name="group">Optional group identifier.</param>
        /// <returns>A UniTask returning the loaded asset.</returns>
        public async UniTask<T> LoadAssetAsync<T>(string key, CancellationToken cancellationToken, string group = null) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key), "Asset key cannot be null or empty.");
            }

            // Pre-track: increment tracking count
            if (!_trackedKeys.TryGetValue(key, out int count))
            {
                _trackedKeys[key] = 1;
            }
            else
            {
                _trackedKeys[key] = count + 1;
            }

            try
            {
                if (AddressableService.Instance == null)
                {
                    throw new InvalidOperationException("AddressableService instance is not initialized.");
                }
                return await AddressableService.Instance.LoadAssetAsync<T>(key, cancellationToken, group);
            }
            catch (OperationCanceledException)
            {
                // If cancellation occurs, decrement tracking count and release asset
                if (_trackedKeys.TryGetValue(key, out int currentCount))
                {
                    if (currentCount <= 1)
                    {
                        _trackedKeys.Remove(key);
                    }
                    else
                    {
                        _trackedKeys[key] = currentCount - 1;
                    }

                    if (AddressableService.Instance != null)
                    {
                        AddressableService.Instance.ReleaseAsset(key);
                    }
                }
                throw;
            }
            catch (Exception)
            {
                // For other exceptions, only decrement tracking count (AddressableService handles its own clean-up)
                if (_trackedKeys.TryGetValue(key, out int currentCount))
                {
                    if (currentCount <= 1)
                    {
                        _trackedKeys.Remove(key);
                    }
                    else
                    {
                        _trackedKeys[key] = currentCount - 1;
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Loads an asset by its AssetReference asynchronously with a custom CancellationToken.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="assetReference">The AssetReference of the asset.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <param name="group">Optional group identifier.</param>
        /// <returns>A UniTask returning the loaded asset.</returns>
        public async UniTask<T> LoadAssetAsync<T>(AssetReference assetReference, CancellationToken cancellationToken, string group = null) where T : UnityEngine.Object
        {
            if (assetReference == null)
            {
                throw new ArgumentNullException(nameof(assetReference), "AssetReference cannot be null.");
            }

            string normalizedKey = NormalizeKey(assetReference);
            if (string.IsNullOrEmpty(normalizedKey))
            {
                throw new ArgumentException("AssetReference runtime key is null or empty.", nameof(assetReference));
            }

            // Pre-track: increment tracking count
            if (!_trackedKeys.TryGetValue(normalizedKey, out int count))
            {
                _trackedKeys[normalizedKey] = 1;
            }
            else
            {
                _trackedKeys[normalizedKey] = count + 1;
            }

            try
            {
                if (AddressableService.Instance == null)
                {
                    throw new InvalidOperationException("AddressableService instance is not initialized.");
                }
                return await AddressableService.Instance.LoadAssetAsync<T>(assetReference, cancellationToken, group);
            }
            catch (OperationCanceledException)
            {
                // If cancellation occurs, decrement tracking count and release asset
                if (_trackedKeys.TryGetValue(normalizedKey, out int currentCount))
                {
                    if (currentCount <= 1)
                    {
                        _trackedKeys.Remove(normalizedKey);
                    }
                    else
                    {
                        _trackedKeys[normalizedKey] = currentCount - 1;
                    }

                    if (AddressableService.Instance != null)
                    {
                        AddressableService.Instance.ReleaseAsset(normalizedKey);
                    }
                }
                throw;
            }
            catch (Exception)
            {
                // For other exceptions, only decrement tracking count (AddressableService handles its own clean-up)
                if (_trackedKeys.TryGetValue(normalizedKey, out int currentCount))
                {
                    if (currentCount <= 1)
                    {
                        _trackedKeys.Remove(normalizedKey);
                    }
                    else
                    {
                        _trackedKeys[normalizedKey] = currentCount - 1;
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Releases all tracked assets when this component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (AddressableService.Instance != null)
            {
                foreach (var kvp in _trackedKeys)
                {
                    for (int i = 0; i < kvp.Value; i++)
                    {
                        AddressableService.Instance.ReleaseAsset(kvp.Key);
                    }
                }
            }
            _trackedKeys.Clear();
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

            object key = assetReference.RuntimeKey;
            if (key is string stringKey)
            {
                return stringKey;
            }
            return key?.ToString();
        }
    }
}

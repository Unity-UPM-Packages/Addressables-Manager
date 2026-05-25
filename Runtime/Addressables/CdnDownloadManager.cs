// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using TheLegends.Base.UnitySingleton;

namespace com.thelegends.addressables.manager
{
    /// <summary>
    /// Progress status data containing the progress percentage and download sizes.
    /// </summary>
    public struct DownloadProgressStatus
    {
        /// <summary>
        /// Gets or sets the progress value, ranging from 0.0 to 1.0.
        /// </summary>
        public float Progress { get; set; }

        /// <summary>
        /// Gets or sets the number of bytes downloaded so far.
        /// </summary>
        public long DownloadedBytes { get; set; }

        /// <summary>
        /// Gets or sets the total number of bytes to download.
        /// </summary>
        public long TotalBytes { get; set; }
    }

    /// <summary>
    /// Service manager for handling catalog updates, dependency download monitoring, retries with exponential backoff, and CDN connection management.
    /// </summary>
    public sealed class CdnDownloadManager : PersistentMonoSingleton<CdnDownloadManager>
    {
        /// <summary>
        /// Checks for catalog updates asynchronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to cancel the check operation.</param>
        /// <returns>A UniTask returning a list of catalog keys that have updates.</returns>
        public async UniTask<List<string>> CheckForCatalogUpdatesAsync(CancellationToken cancellationToken)
        {
            AsyncOperationHandle<List<string>> handle = Addressables.CheckForCatalogUpdates(false);
            try
            {
                return await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            finally
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
        }

        /// <summary>
        /// Updates the specified catalogs asynchronously.
        /// </summary>
        /// <param name="catalogs">The list of catalogs to update.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the update operation.</param>
        /// <returns>A UniTask returning a list of updated resource locators.</returns>
        public async UniTask<List<IResourceLocator>> UpdateCatalogsAsync(IEnumerable<string> catalogs, CancellationToken cancellationToken)
        {
            if (catalogs == null)
            {
                throw new ArgumentNullException(nameof(catalogs), "Catalogs cannot be null.");
            }

            AsyncOperationHandle<List<IResourceLocator>> handle = Addressables.UpdateCatalogs(catalogs, false);
            try
            {
                return await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            finally
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
        }

        /// <summary>
        /// Gets the download size in bytes for a set of keys asynchronously.
        /// </summary>
        /// <param name="keys">The keys to check size for.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the size operation.</param>
        /// <returns>A UniTask returning the total download size in bytes.</returns>
        public async UniTask<long> GetDownloadSizeAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys), "Keys cannot be null.");
            }

            AsyncOperationHandle<long> handle = Addressables.GetDownloadSizeAsync(keys);
            try
            {
                return await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            finally
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
        }

        /// <summary>
        /// Downloads dependencies for a set of keys with progress reporting and auto-retries with exponential backoff.
        /// </summary>
        /// <param name="keys">The keys of the dependencies to download.</param>
        /// <param name="progress">The progress reporter for status updates.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the download operation.</param>
        /// <returns>A UniTask returning true if download succeeds, throws exception otherwise.</returns>
        public async UniTask<bool> DownloadDependenciesAsync(
            IEnumerable<string> keys,
            IProgress<DownloadProgressStatus> progress,
            CancellationToken cancellationToken)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys), "Keys cannot be null.");
            }

            int maxRetries = 3;
            float initialDelay = 2.0f;

            if (AddressableService.Instance != null && AddressableService.Instance.Config != null)
            {
                maxRetries = AddressableService.Instance.Config.MaxRetryCount;
                initialDelay = AddressableService.Instance.Config.RetryDelaySeconds;
            }

            int attempt = 0;
            while (true)
            {
                attempt++;
                AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(keys, Addressables.MergeMode.Union, false);
                try
                {
                    while (!handle.IsDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DownloadStatus status = handle.GetDownloadStatus();
                        
                        progress?.Report(new DownloadProgressStatus
                        {
                            Progress = status.Percent,
                            DownloadedBytes = status.DownloadedBytes,
                            TotalBytes = status.TotalBytes
                        });

                        await UniTask.Delay(100, cancellationToken: cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (handle.Status == AsyncOperationStatus.Failed)
                    {
                        throw handle.OperationException ?? new Exception("Addressables dependency download failed.");
                    }

                    // Report final completion status
                    if (handle.IsValid())
                    {
                        DownloadStatus status = handle.GetDownloadStatus();
                        progress?.Report(new DownloadProgressStatus
                        {
                            Progress = 1.0f,
                            DownloadedBytes = status.TotalBytes,
                            TotalBytes = status.TotalBytes
                        });
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempt > maxRetries)
                    {
                        Debug.LogError($"[CdnDownloadManager] Download failed after {attempt} attempts: {ex.Message}");
                        throw;
                    }

                    float delay = initialDelay * Mathf.Pow(2, attempt - 1);
                    Debug.LogWarning($"[CdnDownloadManager] Download attempt {attempt} failed: {ex.Message}. Retrying in {delay:F2} seconds...");
                    await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: cancellationToken);
                }
                finally
                {
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                }
            }
        }
    }
}

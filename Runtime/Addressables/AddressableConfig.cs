// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace com.thelegends.addressables.manager
{
    /// <summary>
    /// Configuration settings for the Addressables manager system, containing CDN download retries, delays, and fallback asset mappings.
    /// </summary>
    [CreateAssetMenu(fileName = "AddressableSettings", menuName = "DataAsset/AddressableSettings")]
    public sealed class AddressableConfig : ScriptableObject
    {
        public const string ResDir = "Assets/TripSoft/Addressables/Resources";
        public const string FileName = "AddressableSettings";
        public const string FileExtension = ".asset";

        private static AddressableConfig _instance;
        public static AddressableConfig Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _instance = Resources.Load<AddressableConfig>(FileName);
                return _instance;
            }
        }
        [Header("Retry Settings")]
        [SerializeField]
        [Tooltip("Number of retries for CDN download or asset loading when network errors occur.")]
        private int _maxRetryCount = 3;

        [SerializeField]
        [Tooltip("Initial delay in seconds before retrying.")]
        private float _retryDelaySeconds = 2.0f;

        [Header("Fallback Settings")]
        [SerializeField]
        [Tooltip("Whether to use fallback assets when loading failures occur.")]
        private bool _useFallbackOnFailure = true;

        [SerializeField]
        [Tooltip("List of fallback mappings mapping requested keys to fallback asset references.")]
        private List<FallbackMapping> _fallbackAssets = new List<FallbackMapping>();

        /// <summary>
        /// Gets the number of retries for CDN download or asset loading when network errors occur.
        /// </summary>
        public int MaxRetryCount => _maxRetryCount;

        /// <summary>
        /// Gets the initial delay in seconds before retrying.
        /// </summary>
        public float RetryDelaySeconds => _retryDelaySeconds;

        /// <summary>
        /// Gets a value indicating whether to use fallback assets when loading failures occur.
        /// </summary>
        public bool UseFallbackOnFailure => _useFallbackOnFailure;

        /// <summary>
        /// Gets the list of fallback mappings mapping requested keys to fallback asset references.
        /// </summary>
        public IReadOnlyList<FallbackMapping> FallbackAssets => _fallbackAssets;
    }

    /// <summary>
    /// Represents a mapping between a requested asset key and its fallback asset reference.
    /// </summary>
    [Serializable]
    public struct FallbackMapping
    {
#pragma warning disable 0649
        [SerializeField]
        [Tooltip("The requested asset key that might fail to load.")]
        private string _key;

        [SerializeField]
        [Tooltip("The fallback asset reference to use when the requested asset fails to load.")]
        private AssetReference _fallbackAsset;
#pragma warning restore 0649

        /// <summary>
        /// Gets the requested asset key.
        /// </summary>
        public string Key => _key;

        /// <summary>
        /// Gets the fallback asset reference.
        /// </summary>
        public AssetReference FallbackAsset => _fallbackAsset;
    }
}

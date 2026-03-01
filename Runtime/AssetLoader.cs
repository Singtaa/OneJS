using System;
using System.Threading.Tasks;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Provides async asset loading for JavaScript.
    /// Wraps Unity's Resources.LoadAsync to provide a Promise-based API.
    /// </summary>
    public static class AssetLoader {
        /// <summary>
        /// Load a resource asynchronously by path and type.
        /// Returns null if the resource is not found (matching Resources.Load behavior).
        /// </summary>
        /// <param name="path">Resource path (relative to Resources folder, no extension)</param>
        /// <param name="type">The type of asset to load</param>
        /// <returns>The loaded asset, or null if not found</returns>
        /// <exception cref="ArgumentException">If path is null or empty</exception>
        public static async Task<UnityEngine.Object> LoadResourceAsync(string path, Type type) {
            // Yield first to ensure Task is always pending when returned.
            // This guarantees a Promise is created on the JS side, even when
            // Resources.LoadAsync completes synchronously (cached resources).
            await Task.Yield();

            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            }

            var request = Resources.LoadAsync(path, type);
            while (!request.isDone) {
                await Task.Yield();
            }

            return request.asset;
        }

        /// <summary>
        /// Load a resource asynchronously by path (loads as UnityEngine.Object).
        /// Returns null if the resource is not found.
        /// </summary>
        /// <param name="path">Resource path (relative to Resources folder, no extension)</param>
        /// <returns>The loaded asset, or null if not found</returns>
        /// <exception cref="ArgumentException">If path is null or empty</exception>
        public static async Task<UnityEngine.Object> LoadResourceAsync(string path) {
            return await LoadResourceAsync(path, typeof(UnityEngine.Object));
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace OneJS {
    /// <summary>
    /// Caches images and coalesces multiple requests for the same image.
    /// </summary>
    public class WebApi {
        Dictionary<string, Texture2D> _imageCache = new();
        Dictionary<string, List<Action<Texture2D>>> _ongoingRequests = new Dictionary<string, List<Action<Texture2D>>>();

        public Coroutine getText(string uri, Action<string> callback) {
            return StaticCoroutine.Start(GetTextCo(uri, callback));
        }

        IEnumerator GetTextCo(string uri, Action<string> callback) {
            using (UnityWebRequest request = UnityWebRequest.Get(uri)) {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError) {
                    callback(request.error);
                } else {
                    callback(request.downloadHandler.text);
                }
            }
        }

        public Coroutine getImage(string url, Action<Texture2D> callback) {
            if (_imageCache.TryGetValue(url, out var value)) {
                callback(value);
                return null;
            }
            if (_ongoingRequests.ContainsKey(url)) {
                _ongoingRequests[url].Add(callback);
                return null;
            }
            _ongoingRequests[url] = new List<Action<Texture2D>> { callback };
            return StaticCoroutine.Start(GetImageCo(url));
        }

        IEnumerator GetImageCo(string url) {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url)) {
                yield return request.SendWebRequest();
                Texture2D texture = null;
                if (request.result == UnityWebRequest.Result.Success) {
                    texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
                    _imageCache[url] = texture;
                } else {
                    Debug.LogError(request.result);
                }

                if (_ongoingRequests.TryGetValue(url, out var callbacks)) {
                    _ongoingRequests.Remove(url);
                    foreach (var cb in callbacks) {
                        cb(texture);
                    }
                }
            }
        }
    }
}
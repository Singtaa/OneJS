using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace OneJS {
    public class WebApi {
        public static void getText(string uri, Action<string> callback) {
            StaticCoroutine.Start(GetTextCo(uri, callback));
        }

        static IEnumerator GetTextCo(string uri, Action<string> callback) {
            using (UnityWebRequest request = UnityWebRequest.Get(uri)) {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError) {
                    callback(request.error);
                } else {
                    callback(request.downloadHandler.text);
                }
            }
        }

        public static void getImage(string uri, Action<Texture2D> callback) {
            StaticCoroutine.Start(GetImageCo(uri, callback));
        }

        static IEnumerator GetImageCo(string uri, Action<Texture2D> callback) {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(uri)) {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success) {
                    callback(((DownloadHandlerTexture)request.downloadHandler).texture);
                } else {
                    Debug.Log(request.result);
                    callback(null);
                }
            }
        }
    }
}
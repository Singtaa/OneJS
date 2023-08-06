using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace OneJS.Engine {
    [RequireComponent(typeof(ScriptEngine))]
    public class WebApi : MonoBehaviour {
        ScriptEngine _engine;

        void Awake() {
            _engine = GetComponent<ScriptEngine>();
            _engine.OnPostInit += OnPostInit;
        }

        void OnDestroy() {
            _engine.OnPostInit -= OnPostInit;
        }

        void OnPostInit() {
            _engine.JintEngine.SetValue("webapi", (object)this);
        }

        public void getText(string uri, Action<string> callback) {
            StartCoroutine(GetTextCo(uri, callback));
        }

        IEnumerator GetTextCo(string uri, Action<string> callback) {
            using (UnityWebRequest request = UnityWebRequest.Get(uri)) {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError) { // Error
                    callback(request.error);
                } else { // Success
                    callback(request.downloadHandler.text);
                }
            }
        }

        public void getImage(string uri, Action<Texture2D> callback) {
            StartCoroutine(GetImageCo(uri, callback));
        }

        IEnumerator GetImageCo(string uri, Action<Texture2D> callback) {
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
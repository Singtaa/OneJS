using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OneJS.Editor {
    public class WeakSet<T> : IEnumerable<T> where T : class {
        private readonly HashSet<WeakReference<T>> _set = new HashSet<WeakReference<T>>();

        public void Add(T item) {
            // Remove dead references first
            Cleanup();

            if (!_set.Any(wr => wr.TryGetTarget(out var target) && ReferenceEquals(target, item))) {
                _set.Add(new WeakReference<T>(item));
            }
        }

        public bool Contains(T item) {
            Cleanup();
            return _set.Any(wr => wr.TryGetTarget(out var target) && ReferenceEquals(target, item));
        }

        public void Remove(T item) {
            Cleanup();
            _set.RemoveWhere(wr => wr.TryGetTarget(out var target) && ReferenceEquals(target, item));
        }

        public void Clear() {
            _set.Clear();
        }

        private void Cleanup() {
            // Remove entries where the target has been garbage collected
            _set.RemoveWhere(wr => !wr.TryGetTarget(out _));
        }

        public int Count {
            get {
                Cleanup();
                return _set.Count;
            }
        }

        // Implement IEnumerable<T>
        public IEnumerator<T> GetEnumerator() {
            Cleanup();
            foreach (var weakRef in _set) {
                if (weakRef.TryGetTarget(out var target)) {
                    yield return target;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
using UnityEngine;

namespace OneJS {
    public class PairMappingAttribute : PropertyAttribute {
        public readonly string from;
        public readonly string to;

        public PairMappingAttribute(string from, string to) {
            this.from = from;
            this.to = to;
        }
    }
}
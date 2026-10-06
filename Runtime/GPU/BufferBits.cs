using System;
using System.Text;

namespace OneJS.GPU {
    /// <summary>
    /// A buffer's 32-bit words as text: each word's int32 bit pattern, joined by
    /// commas ("1065353216,-1082130432"). How compute buffers cross the bridge (#107).
    ///
    /// JSON carried every float as decimal text, which costs a float formatting in
    /// JS and a float.Parse in C# per element, and rounds an Int32Array or
    /// Uint32Array through float. Integers format and parse far more cheaply, and
    /// the bits arrive exactly whatever the array type: about 5x faster than JSON
    /// to write 4096 floats and 2x to read them back (Tests/BufferTransportBenchmark.cs).
    ///
    /// Text rather than raw bytes because a string crossing the bridge ends at its
    /// first NUL, which raw float bytes are full of; a true binary path needs a
    /// native entry point that takes a typed array's backing store.
    /// </summary>
    internal static class BufferBits {
        /// <summary>The words <paramref name="text"/> spells, as floats with exactly those bits.</summary>
        public static float[] Parse(string text) {
            if (string.IsNullOrEmpty(text)) return Array.Empty<float>();
            int count = 1;
            foreach (var ch in text) if (ch == ',') count++;

            var words = new int[count];
            int k = 0;
            long value = 0;
            bool negative = false;
            for (int i = 0; i <= text.Length; i++) {
                char ch = i < text.Length ? text[i] : ',';
                if (ch == ',') {
                    words[k++] = unchecked((int)(negative ? -value : value));
                    value = 0;
                    negative = false;
                } else if (ch == '-') {
                    negative = true;
                } else if (ch >= '0' && ch <= '9') {
                    value = value * 10 + (ch - '0');
                }
            }

            var result = new float[count];
            Buffer.BlockCopy(words, 0, result, 0, count * 4);
            return result;
        }

        /// <summary>The bits of <paramref name="data"/> as text <see cref="Parse"/> reads back exactly.</summary>
        public static string Format(float[] data) {
            if (data == null || data.Length == 0) return "";
            var words = new int[data.Length];
            Buffer.BlockCopy(data, 0, words, 0, data.Length * 4);
            var sb = new StringBuilder(data.Length * 11);
            for (int i = 0; i < words.Length; i++) {
                if (i > 0) sb.Append(',');
                AppendInvariant(sb, words[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Digits and an ASCII minus whatever the culture: Append(int) formats
        /// with the current one, and some write the minus as U+2212.
        /// </summary>
        static unsafe void AppendInvariant(StringBuilder sb, int value) {
            if (value < 0) sb.Append('-');
            ulong magnitude = value < 0 ? (ulong)(-(long)value) : (ulong)value;
            char* digits = stackalloc char[10];
            int n = 0;
            do {
                digits[n++] = (char)('0' + (int)(magnitude % 10));
                magnitude /= 10;
            } while (magnitude != 0);
            while (n > 0) sb.Append(digits[--n]);
        }
    }
}

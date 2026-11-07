using System;
using UnityEngine;

namespace OneJS.Lighting {
    /// <summary>
    /// The one atlas a bake's pages are cut from (onejs-play's `src/light/atlas.ts`, sheetOf). One
    /// page is the atlas as it was before pages; 2 sit side by side and 4 two by two. A second
    /// column or row starts Seam texels before the first ends, so the texels either side of the
    /// split are on both pages, and a bilinear read on either side of it finds all it needs on one.
    ///
    /// A rectangle's uv scale and offset, and so a lightmap uv, are in pages: the first page runs 0
    /// to 1, and the second column from 1 less Seam texels. One page's are what they were before
    /// pages.
    /// </summary>
    public readonly struct LightSheet {
        public const int Seam = 2;
        // Past any uv: where a read never moves to a column or row that is not there.
        const float NoSplit = 1e6f;
        const float Slack = 1e-3f;

        public readonly int PageWidth, PageHeight, Pages;

        public LightSheet(int pageWidth, int pageHeight, int pages) {
            if (pages != 1 && pages != 2 && pages != 4) throw new ArgumentException($"[OneJS] a bake's atlas has 1, 2 or 4 pages, not {pages}");
            var least = pages == 1 ? 1 : Seam + 1;
            if (pageWidth < least || pageHeight < least) throw new ArgumentException($"[OneJS] a bake's pages are {pageWidth} by {pageHeight}");
            PageWidth = pageWidth;
            PageHeight = pageHeight;
            Pages = pages;
        }

        public int Columns => Pages == 1 ? 1 : 2;
        public int Rows => Pages == 4 ? 2 : 1;
        /// <summary>The sheet's size in texels.</summary>
        public int Width => Columns * PageWidth - (Columns - 1) * Seam;
        public int Height => Rows * PageHeight - (Rows - 1) * Seam;
        /// <summary>The sheet's size in pages: the most a uv reaches.</summary>
        public Vector2 Extent => new Vector2(Width / (float)PageWidth, Height / (float)PageHeight);

        /// <summary>A page's first texel in the sheet. Pages are numbered across, then down.</summary>
        public Vector2Int Origin(int page) => new Vector2Int(page % 2 * (PageWidth - Seam), page / 2 * (PageHeight - Seam));

        /// <summary>
        /// ModelLit's `_OjLightmapSeams`: x and y the uv, in pages, from which a read takes the second
        /// column and row, the middle of the texels they share (past any uv when there is none); z
        /// and w where the second starts.
        /// </summary>
        public Vector4 Seams => new Vector4(
            Columns == 2 ? (PageWidth - 1f) / PageWidth : NoSplit, Rows == 2 ? (PageHeight - 1f) / PageHeight : NoSplit,
            (PageWidth - (float)Seam) / PageWidth, (PageHeight - (float)Seam) / PageHeight);

        /// <summary>The page a uv in pages reads from, as ModelLit picks it.</summary>
        public int PageAt(Vector2 uv) {
            var seams = Seams;
            return (uv.x >= seams.x ? 1 : 0) + (uv.y >= seams.y ? 2 : 0);
        }

        /// <summary>
        /// The last page a rectangle reads from: its far corner's. Pages light in order, so once that
        /// one has light, all the rectangle reads has.
        /// </summary>
        public int LastPage(Vector4 st) => PageAt(new Vector2(st.z + st.x, st.w + st.y));

        /// <summary>Whether a rectangle, as a uv scale and offset in pages, lies on the sheet, past float noise.</summary>
        public bool Holds(Vector4 st) {
            var extent = Extent;
            return st.z >= -Slack && st.w >= -Slack && st.z + st.x <= extent.x + Slack && st.w + st.y <= extent.y + Slack;
        }

        /// <summary>Whether a uv in pages lies on the sheet, past float noise.</summary>
        public bool Holds(Vector2 uv) {
            var extent = Extent;
            return uv.x >= -Slack && uv.y >= -Slack && uv.x <= extent.x + Slack && uv.y <= extent.y + Slack;
        }
    }
}

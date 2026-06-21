using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Batched vector drawing: replays a flat numeric command buffer onto a
    /// MeshGenerationContext's Painter2D in a single JS->C# crossing.
    ///
    /// Without this, every Painter2D op (BeginPath/MoveTo/LineTo/Fill/...) plus
    /// every `new Vector2`/`new Color` argument inside an onGenerateVisualContent
    /// callback is its own reflection crossing. A path with N segments costs
    /// ~2N+ crossings per repaint, which is the dominant cost of custom vector
    /// drawing on the QuickJS interpreter.
    ///
    /// This mirrors what StyleBridge.ApplyStyles did for styles: the JS-side
    /// Painter recorder packs all ops into one Float32Array, sends it once, and
    /// C# replays them here with direct typed calls - zero reflection, with the
    /// Vector2/Color structs constructed C#-side.
    ///
    /// Buffer format: a self-describing opcode stream
    ///   [opcode, ...args, opcode, ...args, ...]
    /// The opcode integers and their arg layouts are the contract shared with
    /// onejs-react's painter.ts. Keep the two in sync.
    ///
    /// Called from JS via:
    ///   CS.OneJS.PainterBridge.Execute(mgc, floatBuffer)
    /// </summary>
    public static class PainterBridge {
        // Opcode contract - must match JSModules/onejs-react/src/painter.ts.
        const int OpBeginPath = 1;
        const int OpClosePath = 2;
        const int OpMoveTo = 3;
        const int OpLineTo = 4;
        const int OpArc = 5;
        const int OpArcTo = 6;
        const int OpBezierCurveTo = 7;
        const int OpQuadraticCurveTo = 8;
        const int OpFill = 9;
        const int OpStroke = 10;
        const int OpLineWidth = 11;
        const int OpFillColor = 12;
        const int OpStrokeColor = 13;
        const int OpLineCap = 14;
        const int OpLineJoin = 15;
        const int OpMiterLimit = 16;
        const int OpDashOffset = 17;
        const int OpDashPattern = 18;

        public static void Execute(MeshGenerationContext mgc, object bufferObj) {
            if (mgc == null || bufferObj == null) return;

            // The buffer arrives as the {__csArray, __csArrayType:"float"} marker
            // (a Float32Array on the JS side). Reuse the shared conversion that
            // StyleBridge relies on so we get a plain float[].
            var buffer = QuickJSNative.ConvertToTargetType(bufferObj, typeof(float[])) as float[];
            if (buffer == null || buffer.Length == 0) return;

            var p = mgc.painter2D;
            int n = buffer.Length;
            int i = 0;
            try {
                while (i < n) {
                    int op = (int)buffer[i++];
                    switch (op) {
                        case OpBeginPath: p.BeginPath(); break;
                        case OpClosePath: p.ClosePath(); break;
                        case OpMoveTo:
                            p.MoveTo(new Vector2(buffer[i], buffer[i + 1])); i += 2; break;
                        case OpLineTo:
                            p.LineTo(new Vector2(buffer[i], buffer[i + 1])); i += 2; break;
                        case OpArc:
                            p.Arc(new Vector2(buffer[i], buffer[i + 1]), buffer[i + 2],
                                Angle.Radians(buffer[i + 3]), Angle.Radians(buffer[i + 4]),
                                MapArcDirection(buffer[i + 5]));
                            i += 6; break;
                        case OpArcTo:
                            p.ArcTo(new Vector2(buffer[i], buffer[i + 1]),
                                new Vector2(buffer[i + 2], buffer[i + 3]), buffer[i + 4]);
                            i += 5; break;
                        case OpBezierCurveTo:
                            p.BezierCurveTo(new Vector2(buffer[i], buffer[i + 1]),
                                new Vector2(buffer[i + 2], buffer[i + 3]),
                                new Vector2(buffer[i + 4], buffer[i + 5]));
                            i += 6; break;
                        case OpQuadraticCurveTo:
                            p.QuadraticCurveTo(new Vector2(buffer[i], buffer[i + 1]),
                                new Vector2(buffer[i + 2], buffer[i + 3]));
                            i += 4; break;
                        case OpFill:
                            p.Fill(MapFillRule(buffer[i])); i += 1; break;
                        case OpStroke: p.Stroke(); break;
                        case OpLineWidth: p.lineWidth = buffer[i]; i += 1; break;
                        case OpFillColor:
                            p.fillColor = new Color(buffer[i], buffer[i + 1], buffer[i + 2], buffer[i + 3]);
                            i += 4; break;
                        case OpStrokeColor:
                            p.strokeColor = new Color(buffer[i], buffer[i + 1], buffer[i + 2], buffer[i + 3]);
                            i += 4; break;
                        case OpLineCap: p.lineCap = MapLineCap(buffer[i]); i += 1; break;
                        case OpLineJoin: p.lineJoin = MapLineJoin(buffer[i]); i += 1; break;
                        case OpMiterLimit: p.miterLimit = buffer[i]; i += 1; break;
                        case OpDashOffset: p.dashOffset = buffer[i]; i += 1; break;
                        case OpDashPattern: p.SetDashPattern(buffer[i], buffer[i + 1]); i += 2; break;
                        default:
                            Debug.LogWarning(
                                $"[PainterBridge] Unknown opcode {op} at index {i - 1}; aborting buffer.");
                            return;
                    }
                }
            } catch (Exception ex) {
                // A correct recorder never emits a truncated buffer; this guards
                // against corruption without tearing down the whole repaint.
                Debug.LogWarning($"[PainterBridge] Execute failed near index {i}: {ex.Message}");
            }
        }

        // JS sends stable opcode-local enum ints (see painter.ts); map them to
        // Unity enums explicitly so the buffer contract does not depend on Unity's
        // enum underlying values.
        static ArcDirection MapArcDirection(float v) =>
            (int)v == 1 ? ArcDirection.CounterClockwise : ArcDirection.Clockwise;

        static FillRule MapFillRule(float v) =>
            (int)v == 1 ? FillRule.OddEven : FillRule.NonZero;

        static LineCap MapLineCap(float v) =>
            (int)v == 1 ? LineCap.Round : LineCap.Butt;

        static LineJoin MapLineJoin(float v) {
            switch ((int)v) {
                case 1: return LineJoin.Bevel;
                case 2: return LineJoin.Round;
                default: return LineJoin.Miter;
            }
        }
    }
}

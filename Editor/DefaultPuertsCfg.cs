using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Puerts;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    [Configure]
    public class DefaultPuertsCfg {
        [Typing]
        static IEnumerable<Type> Typings {
            get {
                var systemTypes = new[] {
                    typeof(System.Object)
                };
                // var unityengineTypes = new[] {
                //     typeof(UnityEngine.Object), typeof(Behaviour), typeof(Component), typeof(GameObject), typeof(Transform),
                //     typeof(Camera), typeof(CameraClearFlags), typeof(Collider),
                //     typeof(MeshRenderer), typeof(Renderer), typeof(Material), typeof(MeshFilter), typeof(Mesh),
                //     typeof(PhysicMaterial), typeof(Physics), typeof(PrimitiveType), typeof(UnityEngine.Random), typeof(Rigidbody),
                // };
                var unityengineTypes = GetTypesFromAssemblies(typeof(GameObject).Assembly, typeof(Rigidbody).Assembly).Where(t => t.Namespace == "UnityEngine").ToArray();

                var uiElementTypes = new[] {
                    typeof(VisualElement), typeof(Button), typeof(Label), typeof(TextElement),

                    // JSX UnityEngine
                    typeof(Bounds), typeof(BoundsInt), typeof(Color), typeof(Rect), typeof(RectInt), typeof(ScaleMode),
                    typeof(Sprite), typeof(Texture), typeof(Texture2D), typeof(Vector2), typeof(Vector2Int), typeof(Vector3),
                    typeof(Vector3Int), typeof(Vector4),

                    // JSX UnityEngine/UIElements
                    typeof(EventBase), typeof(EventBase<ClickEvent>),
                    typeof(PointerEventBase<ClickEvent>), typeof(KeyboardEventBase<KeyDownEvent>), typeof(Focusable),
                    typeof(AttachToPanelEvent), typeof(BlurEvent), typeof(ClickEvent), typeof(InputEvent), typeof(FocusEvent),
                    typeof(WheelEvent), typeof(DetachFromPanelEvent), typeof(DragEnterEvent), typeof(DragExitedEvent),
                    typeof(DragLeaveEvent), typeof(DragPerformEvent), typeof(DragUpdatedEvent), typeof(ExecuteCommandEvent),
                    typeof(FocusInEvent), typeof(FocusOutEvent), typeof(GeometryChangedEvent), typeof(KeyDownEvent),
                    typeof(KeyUpEvent), typeof(MouseCaptureEvent), typeof(MouseCaptureOutEvent), typeof(MouseDownEvent),
                    typeof(MouseEnterEvent), typeof(MouseEnterWindowEvent), typeof(MouseLeaveEvent), typeof(MouseLeaveWindowEvent),
                    typeof(MouseMoveEvent), typeof(MouseOutEvent), typeof(MouseOverEvent), typeof(MouseUpEvent), typeof(PickingMode),
                    typeof(PointerCancelEvent), typeof(PointerCaptureEvent), typeof(PointerCaptureOutEvent), typeof(PointerDownEvent),
                    typeof(PointerEnterEvent), typeof(PointerLeaveEvent), typeof(PointerMoveEvent), typeof(PointerOutEvent),
                    typeof(PointerOverEvent), typeof(PointerStationaryEvent), typeof(PointerUpEvent), typeof(ScrollViewMode),
                    typeof(TooltipEvent), typeof(UxmlBoolAttributeDescription), typeof(UxmlDoubleAttributeDescription),
                    typeof(UxmlFloatAttributeDescription), typeof(UxmlIntAttributeDescription), typeof(UxmlLongAttributeDescription),
                    typeof(UxmlStringAttributeDescription), typeof(ValidateCommandEvent), typeof(ChangeEvent<>),
                    typeof(ScrollerVisibility), typeof(DropdownField), typeof(NavigationMoveEvent), typeof(NavigationCancelEvent),
                    typeof(NavigationSubmitEvent), typeof(TransitionCancelEvent), typeof(TransitionEndEvent),
                    typeof(TransitionRunEvent), typeof(TransitionStartEvent), typeof(VectorImage),
                    typeof(ScrollView), typeof(ScrollView.TouchScrollBehavior),

                    // Styling
                    typeof(Align), typeof(DisplayStyle), typeof(FlexDirection), typeof(Wrap), typeof(Justify), typeof(Position), typeof(TextOverflow),
                    typeof(TimeValue), typeof(StylePropertyName), typeof(EasingFunction), typeof(OverflowClipBox), typeof(TextOverflowPosition),
                    typeof(Visibility), typeof(WhiteSpace), typeof(StyleKeyword), typeof(StyleColor), typeof(StyleBackground), typeof(Background),
                    typeof(Length), typeof(LengthUnit), typeof(StyleLength), typeof(StyleFloat), typeof(StyleInt),
                    typeof(StyleEnum<>), typeof(IStyle), typeof(IResolvedStyle), typeof(UnityEngine.UIElements.Cursor),
                    typeof(StyleCursor), typeof(StyleRotate), typeof(Rotate), typeof(Angle), typeof(StyleScale), typeof(Scale), typeof(TextShadow),
                    typeof(StyleTextShadow), typeof(StyleTransformOrigin), typeof(TransformOrigin), typeof(StyleTranslate), typeof(Translate),
                    typeof(StyleFont), typeof(StyleFontDefinition), typeof(Overflow), typeof(EasingMode), typeof(FontDefinition),
                    typeof(VectorImage), typeof(AngleUnit), typeof(StyleBackgroundRepeat), typeof(BackgroundRepeat), typeof(Repeat),
                    typeof(StyleBackgroundSize), typeof(BackgroundSize), typeof(BackgroundPosition), typeof(StyleBackgroundPosition),
                    typeof(BackgroundPositionKeyword)
                };
                var mathematicsTypes = new[] {
                    typeof(float2), typeof(float3), typeof(float4), typeof(float2x2), typeof(float3x3), typeof(float4x4),
                    typeof(half), typeof(half2), typeof(half3), typeof(half4),
                    typeof(int2), typeof(int3), typeof(int4), typeof(int2x2), typeof(int3x3), typeof(int4x4),
                    typeof(uint2), typeof(uint3), typeof(uint4), typeof(uint2x2), typeof(uint3x3), typeof(uint4x4),
                    typeof(quaternion),
                };
                return ConcatTypes(systemTypes, unityengineTypes, uiElementTypes, mathematicsTypes);
            }
        }

        [Binding]
        static IEnumerable<Type> Bindings {
            get {
                return new List<Type>() {
                    typeof(UnityEngine.Debug),
                    typeof(UnityEngine.Time),
                    typeof(UnityEngine.Rect),
                    typeof(UnityEngine.Color),
                    typeof(UnityEngine.Color32),
                    typeof(UnityEngine.Vector2),
                    typeof(UnityEngine.Vector3),
                    typeof(UnityEngine.Quaternion),
                    typeof(Unity.Mathematics.math),
                    typeof(VisualElement),
                    // typeof(IStyle),
                    // typeof(IResolvedStyle),
                    typeof(MeshGenerationContext),
                    typeof(Painter2D),
                    typeof(OneJS.Utils.UIStyleUtil),
                    typeof(OneJS.Dom.Document),
                    typeof(OneJS.Dom.Dom),
                    typeof(OneJS.Dom.DomStyle),
                    typeof(OneJS.Resource),
                };
            }
        }

        /*
        // These require unsafe code to work
        [BlittableCopy]
        static IEnumerable<Type> Blittables {
            get {
                return new List<Type>() {
                    typeof(UnityEngine.Rect),
                    typeof(UnityEngine.Color),
                    typeof(UnityEngine.Color32),
                    typeof(UnityEngine.Vector2),
                    typeof(UnityEngine.Vector3),
                    typeof(UnityEngine.Quaternion),
                };
            }
        }
        */

        static Type[] ConcatTypes(params Type[][] types) {
            return types.SelectMany(t => t).ToArray();
        }

        static Type[] GetTypesFromAssemblies(params Assembly[] assemblies) {
            return assemblies.SelectMany(a => a.GetTypes()).ToArray();
        }
    }
}
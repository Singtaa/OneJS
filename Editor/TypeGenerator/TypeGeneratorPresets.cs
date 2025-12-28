using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Pre-configured type sets for common generation scenarios.
    /// These presets provide curated collections of types for specific Unity subsystems.
    /// </summary>
    public static class TypeGeneratorPresets {
        #region Unity Core

        /// <summary>
        /// Core Unity types commonly used in game development.
        /// Includes: Vector types, Quaternion, Transform, GameObject, Component, MonoBehaviour, etc.
        /// </summary>
        public static TypeGeneratorResult UnityCore() {
            return TypeGenerator.Create()
                // Math types
                .AddType<Vector2>()
                .AddType<Vector3>()
                .AddType<Vector4>()
                .AddType<Vector2Int>()
                .AddType<Vector3Int>()
                .AddType<Quaternion>()
                .AddType<Matrix4x4>()
                .AddType<Color>()
                .AddType<Color32>()
                .AddType<Rect>()
                .AddType<RectInt>()
                .AddType<Bounds>()
                .AddType<BoundsInt>()
                .AddType<Ray>()
                .AddType<Ray2D>()
                .AddType<Plane>()
                // Core components
                .AddType<UnityEngine.Object>()
                .AddType<GameObject>()
                .AddType<Component>()
                .AddType<Transform>()
                .AddType<RectTransform>()
                .AddType<MonoBehaviour>()
                .AddType<Behaviour>()
                .AddType<ScriptableObject>()
                // Time and application
                .AddType<Time>()
                .AddType<Application>()
                // Scene management
                .AddTypesFromNamespace("UnityEngine.SceneManagement")
                // Resources and assets
                .AddType<Resources>()
                .AddType<TextAsset>()
                .AddType<Texture>()
                .AddType<Texture2D>()
                .AddType<Sprite>()
                .AddType<Material>()
                .AddType<Shader>()
                .AddType<Mesh>()
                // Input (legacy)
                .AddType<Input>()
                .AddType<KeyCode>()
                // Rendering
                .AddType<Camera>()
                .AddType<Light>()
                .AddType<Renderer>()
                .AddType<MeshRenderer>()
                .AddType<SpriteRenderer>()
                .AddType<MeshFilter>()
                // Debug
                .AddType<Debug>()
                // Coroutines
                .AddType<Coroutine>()
                .AddType<WaitForSeconds>()
                .AddType<WaitForSecondsRealtime>()
                .AddType<WaitForEndOfFrame>()
                .AddType<WaitForFixedUpdate>()
                .AddType<WaitUntil>()
                .AddType<WaitWhile>()
                .Build();
        }

        #endregion

        #region UI Toolkit

        /// <summary>
        /// UI Toolkit types for building user interfaces.
        /// Includes: VisualElement, Label, Button, TextField, and common controls.
        /// </summary>
        public static TypeGeneratorResult UIToolkit() {
            return TypeGenerator.Create()
                .AddNamespace("UnityEngine.UIElements")
                .Build();
        }

        #endregion

        #region Physics

        /// <summary>
        /// Physics types for 3D and 2D physics.
        /// Includes: Rigidbody, Collider, Physics, RaycastHit, etc.
        /// </summary>
        public static TypeGeneratorResult Physics() {
            return TypeGenerator.Create()
                // 3D Physics
                .AddType<Rigidbody>()
                .AddType<Collider>()
                .AddType<BoxCollider>()
                .AddType<SphereCollider>()
                .AddType<CapsuleCollider>()
                .AddType<MeshCollider>()
                .AddType<CharacterController>()
                .AddType<UnityEngine.Physics>()
                .AddType<RaycastHit>()
                .AddType<Collision>()
                .AddType<ContactPoint>()
                .AddType<PhysicsMaterial>()
                .AddType<Joint>()
                .AddType<FixedJoint>()
                .AddType<HingeJoint>()
                .AddType<SpringJoint>()
                .AddType<ConfigurableJoint>()
                // 2D Physics
                .AddType<Rigidbody2D>()
                .AddType<Collider2D>()
                .AddType<BoxCollider2D>()
                .AddType<CircleCollider2D>()
                .AddType<CapsuleCollider2D>()
                .AddType<PolygonCollider2D>()
                .AddType<EdgeCollider2D>()
                .AddType<CompositeCollider2D>()
                .AddType<Physics2D>()
                .AddType<RaycastHit2D>()
                .AddType<Collision2D>()
                .AddType<ContactPoint2D>()
                .AddType<PhysicsMaterial2D>()
                .Build();
        }

        #endregion

        #region Animation

        /// <summary>
        /// Animation types for character and object animation.
        /// Includes: Animator, Animation, AnimationClip, etc.
        /// </summary>
        public static TypeGeneratorResult Animation() {
            return TypeGenerator.Create()
                .AddType<Animator>()
                .AddType<RuntimeAnimatorController>()
                .AddType<AnimatorOverrideController>()
                .AddTypeByName("UnityEditor.Animations.AnimatorController")
                .AddType<AnimatorStateInfo>()
                .AddType<AnimatorClipInfo>()
                .AddType<AnimatorControllerParameter>()
                .AddType<AnimatorControllerParameterType>()
                .AddType<Animation>()
                .AddType<AnimationClip>()
                .AddType<AnimationCurve>()
                .AddType<Keyframe>()
                .AddType<AnimationState>()
                .AddType<AnimationEvent>()
                .AddType<Avatar>()
                .AddType<AvatarMask>()
                .AddType<HumanBodyBones>()
                .Build();
        }

        #endregion

        #region Audio

        /// <summary>
        /// Audio types for sound playback and management.
        /// Includes: AudioSource, AudioClip, AudioListener, AudioMixer, etc.
        /// </summary>
        public static TypeGeneratorResult Audio() {
            return TypeGenerator.Create()
                .AddType<AudioSource>()
                .AddType<AudioClip>()
                .AddType<AudioListener>()
                .AddType<AudioMixer>()
                .AddType<AudioMixerGroup>()
                .AddType<AudioMixerSnapshot>()
                .AddType<AudioReverbZone>()
                .AddType<AudioHighPassFilter>()
                .AddType<AudioLowPassFilter>()
                .AddType<AudioEchoFilter>()
                .AddType<AudioDistortionFilter>()
                .AddType<AudioReverbFilter>()
                .AddType<AudioChorusFilter>()
                .AddType<AudioSettings>()
                .AddType<AudioConfiguration>()
                .AddType<AudioRolloffMode>()
                .Build();
        }

        #endregion

        #region Input System

        /// <summary>
        /// Input System types (requires Unity Input System package).
        /// Includes: InputAction, InputActionMap, InputDevice, etc.
        /// </summary>
        public static TypeGeneratorResult InputSystem() {
            // Check if Input System is available
            var inputSystemAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.InputSystem");

            if (inputSystemAssembly == null) {
                Debug.LogWarning("[TypeGenerator] Input System package not found. Returning empty result.");
                return new TypeGeneratorResult("// Input System package not installed", Array.Empty<Type>(), Array.Empty<TsTypeInfo>());
            }

            return TypeGenerator.Create()
                .AddAssemblyByName("Unity.InputSystem")
                .Build();
        }

        #endregion

        #region All Unity Types

        /// <summary>
        /// Combines all Unity presets into a single comprehensive result.
        /// </summary>
        public static TypeGeneratorResult All() {
            return TypeGeneratorResult.Combine(
                UnityCore(),
                UIToolkit(),
                Physics(),
                Animation(),
                Audio()
            );
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Adds types from a namespace if the namespace exists.
        /// </summary>
        private static TypeGeneratorBuilder AddTypesFromNamespace(this TypeGeneratorBuilder builder, string namespaceName) {
            try {
                return builder.AddNamespace(namespaceName);
            } catch {
                // Namespace might not exist in this Unity version
                return builder;
            }
        }

        /// <summary>
        /// Creates a custom preset from specific types.
        /// </summary>
        /// <param name="name">Preset name (for logging)</param>
        /// <param name="types">Types to include</param>
        /// <returns>Generated result</returns>
        public static TypeGeneratorResult CreateCustom(string name, params Type[] types) {
            return TypeGenerator.Create()
                .AddTypes(types)
                .Build();
        }

        /// <summary>
        /// Creates a custom preset from a namespace.
        /// </summary>
        /// <param name="namespaceName">Namespace to include</param>
        /// <returns>Generated result</returns>
        public static TypeGeneratorResult FromNamespace(string namespaceName) {
            return TypeGenerator.Create()
                .AddNamespace(namespaceName)
                .Build();
        }

        /// <summary>
        /// Creates a custom preset from an assembly.
        /// </summary>
        /// <param name="assemblyPattern">Assembly name pattern</param>
        /// <returns>Generated result</returns>
        public static TypeGeneratorResult FromAssembly(string assemblyPattern) {
            return TypeGenerator.Create()
                .AddAssemblyByName(assemblyPattern)
                .Build();
        }

        #endregion
    }
}

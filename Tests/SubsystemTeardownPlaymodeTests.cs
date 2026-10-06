using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using OneJS.Audio;
using OneJS.GPU;
using OneJS.Input;
using OneJS.SL;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// What a context makes through Audio, GPU, SL and Input must go when that
    /// context is torn down (a hot reload, a stopped or destroyed JSRunner), and
    /// only that context's: the same contract PerContextResourcePlaymodeTests
    /// holds Particles, Physics2D, ShaderFX and Fx to (architecture review A1).
    /// Each test makes the same thing in two contexts, disposes one, then the
    /// other, the last bridge.
    /// </summary>
    [TestFixture]
    public class SubsystemTeardownPlaymodeTests {
        QuickJSUIBridge _a, _b;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _a = new QuickJSUIBridge(new VisualElement());
            _b = new QuickJSUIBridge(new VisualElement());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _b?.Dispose();
            _a?.Dispose();
            _a = _b = null;
            yield return null;
        }

        void DisposeB() {
            _b.Dispose();
            _b = null;
        }

        void DisposeA() {
            _a.Dispose();
            _a = null;
        }

        [UnityTest]
        public IEnumerator Audio_ClipsAndVoicesGoWithTheirContext() {
            var url = new Uri(WriteSilentWav()).AbsoluteUri;
            int clips = AudioBridge.GetClipCount();
            // As onejs-unity's audio.load awaits it. Volume 0: the voice plays,
            // and nothing is heard.
            var load = $@"(async function () {{
                const A = CS.OneJS.Audio.AudioBridge
                globalThis.__voice = A.PlayLooping(await A.LoadClip('{url}'), 0, 1)
            }})()";
            _a.Eval(load);
            _b.Eval(load);
            for (int i = 0; i < 300 && !(Playing(_a) && Playing(_b)); i++) {
                _a.Tick();
                _b.Tick();
                yield return null;
            }
            Assert.IsTrue(Playing(_a) && Playing(_b), "both contexts loaded a clip and started a voice");
            Assert.AreEqual(clips + 2, AudioBridge.GetClipCount());
            Assert.AreEqual(2, AudioBridge.GetActiveVoiceCount());

            DisposeB();
            Assert.AreEqual(clips + 1, AudioBridge.GetClipCount(), "B's clip goes, A's stays");
            Assert.AreEqual(1, AudioBridge.GetActiveVoiceCount(), "B's looping voice stops, A's plays on");

            DisposeA();
            Assert.AreEqual(clips, AudioBridge.GetClipCount());
            Assert.AreEqual(0, AudioBridge.GetActiveVoiceCount());
            Assert.IsFalse(AudioBridge.HasVoicePool, "the last context takes the voice pool's host with it");
        }

        static bool Playing(QuickJSUIBridge bridge) => bridge.Eval("String(globalThis.__voice || 0)") != "0";

        /// <summary>A tenth of a second of 16-bit mono silence, as a WAV file.</summary>
        static string WriteSilentWav() {
            const int rate = 8000, samples = 800;
            var path = Path.Combine(Application.temporaryCachePath, "onejs-teardown-silence.wav");
            using (var w = new BinaryWriter(File.Create(path))) {
                w.Write("RIFF".ToCharArray());
                w.Write(36 + samples * 2);
                w.Write("WAVEfmt ".ToCharArray());
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write("data".ToCharArray());
                w.Write(samples * 2);
                w.Write(new byte[samples * 2]);
            }
            return path;
        }

        [UnityTest]
        public IEnumerator GPU_BuffersAndTargetsGoWithTheirContext() {
            int buffers = GPUBridge.LiveBufferCount;
            int targets = GPUBridge.LiveRenderTextureCount;
            const string make = @"CS.OneJS.GPU.GPUBridge.CreateBuffer(16, 4); CS.OneJS.GPU.GPUBridge.CreateRenderTexture(8, 8, true)";
            _a.Eval(make);
            _b.Eval(make);
            Assert.AreEqual(buffers + 2, GPUBridge.LiveBufferCount);
            Assert.AreEqual(targets + 2, GPUBridge.LiveRenderTextureCount);

            DisposeB();
            Assert.AreEqual(buffers + 1, GPUBridge.LiveBufferCount, "B's buffer goes, A's stays");
            Assert.AreEqual(targets + 1, GPUBridge.LiveRenderTextureCount, "B's render texture goes, A's stays");

            DisposeA();
            Assert.AreEqual(buffers, GPUBridge.LiveBufferCount);
            Assert.AreEqual(targets, GPUBridge.LiveRenderTextureCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SL_ProgramsGoWithTheirContext() {
            int programs = SLProgramBridge.LiveProgramCount;
            _a.Eval("CS.OneJS.SL.SLProgramBridge.Upload('teardown-a', null)");
            _b.Eval("CS.OneJS.SL.SLProgramBridge.Upload('teardown-b', null)");
            Assert.AreEqual(programs + 2, SLProgramBridge.LiveProgramCount);

            DisposeB();
            Assert.AreEqual(programs + 1, SLProgramBridge.LiveProgramCount, "B's program goes, A's stays");

            DisposeA();
            Assert.AreEqual(programs, SLProgramBridge.LiveProgramCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Input_MapsActionsAndWatchesGoWithTheirContext() {
            int maps = InputBridge.LiveActionMapCount;
            int actions = InputBridge.LiveActionCount;
            int watched = InputBridge.WatchedActionCount;
            const string make = @"(function () {
                const I = CS.OneJS.Input.InputBridge
                const map = I.CreateActionMap('teardown')
                const fire = I.AddButtonAction(map, 'fire')
                I.AddBinding(fire, '<Keyboard>/space')
                I.EnableDynamicMap(map)
                I.WatchActionEvents(fire)
            })()";
            _a.Eval(make);
            _b.Eval(make);
            Assert.AreEqual(maps + 2, InputBridge.LiveActionMapCount);
            Assert.AreEqual(actions + 2, InputBridge.LiveActionCount);
            Assert.AreEqual(watched + 2, InputBridge.WatchedActionCount);

            DisposeB();
            Assert.AreEqual(maps + 1, InputBridge.LiveActionMapCount, "B's map goes, A's stays");
            Assert.AreEqual(actions + 1, InputBridge.LiveActionCount, "B's action goes, A's stays");
            Assert.AreEqual(watched + 1, InputBridge.WatchedActionCount, "B's watch goes, A's stays");

            DisposeA();
            Assert.AreEqual(maps, InputBridge.LiveActionMapCount);
            Assert.AreEqual(actions, InputBridge.LiveActionCount);
            Assert.AreEqual(watched, InputBridge.WatchedActionCount);
            yield return null;
        }
    }
}

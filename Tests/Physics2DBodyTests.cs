using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// How a wire body becomes a Rigidbody2D, and what the engine has to say
    /// about it while it happens.
    ///
    /// Both cases here are one defect seen from two sides. `AddBody` used to
    /// assign velocity to every body and density to every collider, including
    /// the bodies those properties cannot apply to. Unity does not ignore
    /// those quietly: it warns, once per assignment, per body, per world. A
    /// sketch on play.onejs.com with five static ramps and eighty shapes
    /// printed 190 warnings before it drew a frame, which is enough to bury
    /// anything a game logs on purpose.
    ///
    /// The other side is that `density` then did nothing. A collider's density
    /// only applies to a dynamic body deriving mass from its colliders, so the
    /// field parsed, validated, warned, and left every body weighing the same.
    /// MassFollowsDensity is the half that would still pass if the warnings
    /// were merely silenced, which is why it is here: quiet and correct are
    /// different claims and each needs its own check.
    /// </summary>
    [TestFixture]
    public class Physics2DBodyTests {
        VisualElement _host;
        readonly List<PhysicsWorld2D> _worlds = new();
        SimulationMode2D _simulationMode;

        [SetUp]
        public void SetUp() {
            _host = new VisualElement();
            // The world's constructor puts the project into script-driven
            // simulation and nothing puts it back, so a test that built one
            // would otherwise decide how the rest of the suite simulates.
            _simulationMode = UnityEngine.Physics2D.simulationMode;
        }

        [TearDown]
        public void TearDown() {
            foreach (var world in _worlds) world?.Dispose();
            _worlds.Clear();
            UnityEngine.Physics2D.simulationMode = _simulationMode;
        }

        PhysicsWorld2D World(string bodies) {
            var json = $@"{{""v"":1,""pixelsPerUnit"":100,""gravityY"":0,""bounds"":false,""bodies"":[{bodies}]}}";
            var world = new PhysicsWorld2D(_host, Physics2DWire.Parse(json));
            _worlds.Add(world);
            return world;
        }

        /// <summary>
        /// Every warning the engine emitted while `build` ran.
        ///
        /// Reads the log rather than the bodies on purpose. The symptom being
        /// guarded is console output, so the check is on console output: an
        /// assertion about `useAutoMass` would pass against any future shape of
        /// this code that happens to warn for some other reason.
        /// </summary>
        static List<string> WarningsDuring(Action build) {
            var warnings = new List<string>();
            void OnLog(string message, string stack, LogType type) {
                if (type == LogType.Warning) warnings.Add(message);
            }
            Application.logMessageReceived += OnLog;
            try { build(); } finally { Application.logMessageReceived -= OnLog; }
            return warnings;
        }

        const string StaticRamp =
            @"{""type"":2,""shape"":0,""w"":300,""h"":16,""x"":200,""y"":300,""rotation"":15,""friction"":0.4,""density"":1}";
        const string DynamicBall =
            @"{""type"":0,""shape"":1,""w"":18,""x"":100,""y"":50,""density"":1,""friction"":0.35}";

        /// <summary>
        /// Proves the instrument before trusting it.
        ///
        /// Every "no warnings" test below is worthless if engine warnings do
        /// not reach Application.logMessageReceived at all, and the failure
        /// looks exactly like success: an empty list. So provoke one on
        /// purpose. `SetVelocity` writes linearVelocity whatever the body is,
        /// which is the same engine complaint AddBody used to cause, reached
        /// by a path a game only takes deliberately.
        /// </summary>
        [Test]
        public void EngineWarningsReachTheLog() {
            var world = World(StaticRamp);
            var warnings = WarningsDuring(() => world.SetVelocity(0, 60f, 0f));
            Assert.IsNotEmpty(warnings,
                "Engine warnings are not reaching Application.logMessageReceived, so every " +
                "no-warning assertion in this fixture is measuring nothing.");
            CollectionAssert.IsNotEmpty(
                warnings.FindAll(w => w.IndexOf("static", StringComparison.OrdinalIgnoreCase) >= 0),
                "Expected the engine's static-body complaint, got: " + string.Join("\n", warnings));
        }

        [Test]
        public void StaticBody_BuildsWithoutWarnings() {
            var warnings = WarningsDuring(() => World(StaticRamp));
            CollectionAssert.IsEmpty(warnings,
                "A static body must not be told about velocity or density:\n" + string.Join("\n", warnings));
        }

        [Test]
        public void DynamicBody_BuildsWithoutWarnings() {
            var warnings = WarningsDuring(() => World(DynamicBall));
            CollectionAssert.IsEmpty(warnings,
                "A dynamic body's density must be applied where it can take effect:\n" + string.Join("\n", warnings));
        }

        /// <summary>
        /// A dynamic sensor is the case auto-mass could have made worse.
        ///
        /// Turning auto-mass on asks the engine to weigh the colliders, and a
        /// body whose only collider is a trigger may have nothing to weigh. If
        /// Unity has anything to say about the mass that produces, it would be
        /// a new warning introduced by the fix for the old ones.
        /// </summary>
        [Test]
        public void DynamicSensor_BuildsWithoutWarnings() {
            const string sensor =
                @"{""type"":0,""shape"":0,""w"":40,""h"":40,""x"":50,""y"":50,""sensor"":true,""density"":1}";
            var warnings = WarningsDuring(() => World(sensor));
            CollectionAssert.IsEmpty(warnings,
                "A dynamic sensor must build quietly too:\n" + string.Join("\n", warnings));
        }

        /// <summary>
        /// The shape of the world that produced the 190 lines: five static
        /// ramps and a crowd of dynamic shapes, built in one go.
        /// </summary>
        [Test]
        public void MixedWorld_BuildsWithoutWarnings() {
            var bodies = new List<string>();
            for (int i = 0; i < 5; i++) bodies.Add(StaticRamp);
            for (int i = 0; i < 20; i++) bodies.Add(DynamicBall);
            var warnings = WarningsDuring(() => World(string.Join(",", bodies)));
            Assert.AreEqual(0, warnings.Count,
                $"25 bodies produced {warnings.Count} warnings, first: {(warnings.Count > 0 ? warnings[0] : "")}");
        }

        /// <summary>
        /// Density has to mean something, not merely stop complaining.
        ///
        /// Measured through the public surface: the same impulse on a light
        /// body and a heavy one, and the light one travels further. Before
        /// auto-mass every body weighed exactly 1 whatever its size or
        /// density, so the two moved identically and this fails.
        /// </summary>
        [Test]
        public void MassFollowsDensity() {
            // Same radius, different density. Keeping the size equal means the
            // only thing that can separate them is the field under test.
            var light = @"{""type"":0,""shape"":1,""w"":20,""x"":100,""y"":100,""density"":1}";
            var heavy = @"{""type"":0,""shape"":1,""w"":20,""x"":300,""y"":100,""density"":8}";
            var world = World(light + "," + heavy);

            world.ApplyImpulse(0, 50f, 0f);
            world.ApplyImpulse(1, 50f, 0f);
            for (int i = 0; i < 10; i++) world.Tick(1f / 60f);

            var moved = Displacements(world, new[] { 100f, 300f });
            Assert.Greater(moved[0], 0.5f, "the light body did not move at all, so nothing here was measured");
            Assert.Greater(moved[0], moved[1] * 1.5f,
                $"a denser body must be harder to shift: light moved {moved[0]:F2}, heavy moved {moved[1]:F2}");
        }

        /// <summary>How far each body travelled along X, in panel units.</summary>
        static float[] Displacements(PhysicsWorld2D world, float[] startX) {
            // ReadTransforms packs as "[x,y,r,x,y,r,...]".
            var parts = world.ReadTransforms().Trim('[', ']').Split(',');
            var moved = new float[startX.Length];
            for (int i = 0; i < startX.Length; i++) {
                var x = float.Parse(parts[i * 3], System.Globalization.CultureInfo.InvariantCulture);
                moved[i] = Mathf.Abs(x - startX[i]);
            }
            return moved;
        }
    }
}

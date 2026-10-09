using NUnit.Framework;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The clock shader effects, particles and 2D physics tick by. Driven by the
    /// virtual clock, whose Begin seeds from engine realtime, so ending and
    /// beginning it again after running ahead steps time backwards, as leaving
    /// play mode does. SLSteppedProgramTests in the container holds the same
    /// through a real play session.
    /// </summary>
    public class TickClockTests {
        bool _wasVirtual;

        [SetUp]
        public void TakeTheClock() {
            _wasVirtual = VirtualClock.IsActive;
            VirtualClock.End();
            VirtualClock.Begin();
        }

        [TearDown]
        public void GiveItBack() {
            VirtualClock.End();
            if (_wasVirtual) VirtualClock.Begin();
        }

        [Test]
        public void ATickReportsTheTimeSinceThePreviousOne() {
            var clock = new TickClock();
            VirtualClock.Advance(1);
            clock.Next(out _);
            VirtualClock.Advance(0.25);
            Assert.IsTrue(clock.Next(out var dt));
            Assert.AreEqual(0.25f, dt, 1e-6f);
        }

        [Test]
        public void ASecondTickInTheSameFrameIsRefused() {
            var clock = new TickClock();
            VirtualClock.Advance(1);
            clock.Next(out _);
            Assert.IsFalse(clock.Next(out _));
            VirtualClock.Advance(0.1);
            Assert.IsTrue(clock.Next(out var dt));
            Assert.AreEqual(0.1f, dt, 1e-6f, "the refused tick lost time");
        }

        [Test]
        public void AClockThatWentBackwardsTicksWithoutAdvancing() {
            var clock = new TickClock();
            VirtualClock.Advance(100);
            clock.Next(out _);
            VirtualClock.End();
            VirtualClock.Begin();
            VirtualClock.Advance(0.1);
            Assert.IsTrue(clock.Next(out var dt), "the first tick after the clock stepped back was dropped");
            Assert.AreEqual(0f, dt);
            VirtualClock.Advance(0.1);
            Assert.IsTrue(clock.Next(out dt));
            Assert.AreEqual(0.1f, dt, 1e-6f);
        }
    }
}

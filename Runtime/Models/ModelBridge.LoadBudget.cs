using System;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace OneJS.Models {
    // How much of a frame loading models may take.
    //
    // glTFast's default agent, which a load in Play mode used until 16 Nov 2026 and still uses
    // while no budget is set, lets an import run until half of a 30 fps frame has passed since
    // the frame's Update began: up to 16.7 ms a frame on top of the frame's own work, so a game's
    // loading screen stepped at 35 to 77 ms a frame while its people loaded (PrimoEden). A budget
    // counts only the loads' own work, from the first step any load takes in a frame, shared by
    // every load in flight, and covers the skins' reach sampled after the import (PrepareSkins).
    public static partial class ModelBridge {
        static float _loadBudget;
        static LoadBudgetAgent _loadAgent;

        /// <summary>
        /// Milliseconds a frame that loading models may take, shared by every load in flight; 0, the
        /// default, leaves glTFast's own agent in charge, as before. A load started under a budget
        /// keeps reading it as it changes, and takes glTFast's 16.7 ms once it is set back to 0, so a
        /// game can load under 4 ms behind its loading screen and go back to the default after.
        /// Each frame takes at least one step of a load, so a step longer than the budget still
        /// runs, in a frame of its own. Ignored in edit mode, which loads in one go. DisposeAll sets
        /// it back to 0.
        /// </summary>
        public static float LoadBudget {
            get => _loadBudget;
            set {
                if (!(value >= 0) || float.IsInfinity(value)) throw new ArgumentException($"a load budget is 0 or more milliseconds, not {value}");
                _loadBudget = value;
            }
        }

        // The agent a load started now takes: glTFast's default (null) unless a budget is set.
        static IDeferAgent LoadAgent() {
            if (!Playing) return new UninterruptedDeferAgent();
            if (_loadBudget <= 0) return null;
            return _loadAgent ??= new LoadBudgetAgent();
        }

        // glTFast's default agent's budget: half of a frame at 30 fps.
        const float DefaultLoadBudget = 1000f * 0.5f / 30;

        /// <summary>
        /// Defers a load's next step to the next frame once the loads have taken the budget this
        /// frame, measured from the first step any of them took in it, which always runs.
        /// </summary>
        internal sealed class LoadBudgetAgent : IDeferAgent {
            int _frame = -1;
            float _started;

            // Starts this frame's clock, once a frame: at its first ask, or where a deferred step resumes.
            void Stamp() {
                if (Time.frameCount == _frame) return;
                _frame = Time.frameCount;
                _started = Time.realtimeSinceStartup;
            }

            // Whether `seconds` more fit in this frame. A frame's first ask is never deferred.
            bool Fits(float seconds) {
                if (Time.frameCount != _frame) {
                    Stamp();
                    return true;
                }
                float budget = (_loadBudget > 0 ? _loadBudget : DefaultLoadBudget) / 1000f;
                return seconds <= budget - (Time.realtimeSinceStartup - _started);
            }

            public bool ShouldDefer() => !Fits(0);

            public bool ShouldDefer(float duration) => !Fits(duration);

            public async Task BreakPoint() {
                if (!ShouldDefer()) return;
                await Task.Yield();
                Stamp();
            }

            public async Task BreakPoint(float duration) {
                if (!ShouldDefer(duration)) return;
                await Task.Yield();
                Stamp();
            }
        }
    }
}

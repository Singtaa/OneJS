namespace OneJS.Models {
    // Measuring only, on this branch only: switches a timing run flips between rounds, so every arm
    // runs on one build in one visit. Never merged.
    public static partial class ModelBridge {
        public static bool ArmOldClearDraws;
        public static bool ArmNoCrc;
        public static bool ArmNoClearCamera;
    }
}

using System;

namespace NaughtyAttributes {
    public enum EButtonEnableMode {
        /// <summary>
        /// Button should be active always
        /// </summary>
        Always,
        /// <summary>
        /// Button should be active only in editor
        /// </summary>
        Editor,
        /// <summary>
        /// Button should be active only in playmode
        /// </summary>
        Playmode
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ButtonAttribute : SpecialCaseDrawerAttribute {
        public string Text { get; private set; }
        public EButtonEnableMode SelectedEnableMode { get; private set; }
        public int Height { get; private set; }

        public ButtonAttribute(string text = null, int height = 30,
            EButtonEnableMode enabledMode = EButtonEnableMode.Always) {
            this.Text = text;
            this.SelectedEnableMode = enabledMode;
            this.Height = height;
        }
    }
}
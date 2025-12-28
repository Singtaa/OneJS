using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor
{
    /// <summary>
    /// Test window for CodeField syntax highlighting.
    /// Open via Window > OneJS > CodeField Test
    /// </summary>
    public class CodeFieldTestWindow : EditorWindow
    {
        [MenuItem("Window/OneJS/CodeField Test")]
        public static void ShowWindow()
        {
            var window = GetWindow<CodeFieldTestWindow>();
            window.titleContent = new GUIContent("CodeField Test");
            window.minSize = new Vector2(500, 400);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            // Dark background
            root.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;

            // Title
            var title = new Label("CodeField Syntax Highlighting Test");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.marginBottom = 10;
            root.Add(title);

            // Instructions
            var instructions = new Label("Type JavaScript/TypeScript code below to see syntax highlighting:");
            instructions.style.color = new Color(0.7f, 0.7f, 0.7f);
            instructions.style.marginBottom = 10;
            root.Add(instructions);

            // CodeField
            var codeField = new CodeField();
            codeField.multiline = true;
            codeField.style.flexGrow = 1;
            codeField.style.minHeight = 200;

            // Style the text input
            var textInput = codeField.Q<VisualElement>("unity-text-input");
            if (textInput != null)
            {
                textInput.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
                textInput.style.borderTopWidth = 1;
                textInput.style.borderBottomWidth = 1;
                textInput.style.borderLeftWidth = 1;
                textInput.style.borderRightWidth = 1;
                textInput.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
                textInput.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
                textInput.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);
                textInput.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
                textInput.style.paddingTop = 8;
                textInput.style.paddingBottom = 8;
                textInput.style.paddingLeft = 8;
                textInput.style.paddingRight = 8;
            }

            // Set sample code
            codeField.value = @"// Sample JavaScript code
function fibonacci(n) {
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

const result = fibonacci(10);
console.log(`Fibonacci(10) = ${result}`);

/* Multi-line comment
   This demonstrates various syntax elements */
class Calculator {
    constructor(value = 0) {
        this.value = value;
    }

    add(x) {
        this.value += x;
        return this;
    }
}

let calc = new Calculator(42);
";

            root.Add(codeField);

            // Color legend
            var legend = new VisualElement();
            legend.style.flexDirection = FlexDirection.Row;
            legend.style.marginTop = 10;
            legend.style.flexWrap = Wrap.Wrap;

            AddLegendItem(legend, "Keywords", new Color32(197, 134, 192, 255));
            AddLegendItem(legend, "Strings", new Color32(206, 145, 120, 255));
            AddLegendItem(legend, "Numbers", new Color32(181, 206, 168, 255));
            AddLegendItem(legend, "Comments", new Color32(106, 153, 85, 255));
            AddLegendItem(legend, "Default", new Color32(212, 212, 212, 255));

            root.Add(legend);
        }

        private void AddLegendItem(VisualElement parent, string text, Color32 color)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginRight = 15;
            container.style.marginTop = 5;

            var colorBox = new VisualElement();
            colorBox.style.width = 12;
            colorBox.style.height = 12;
            colorBox.style.backgroundColor = (Color)color;
            colorBox.style.marginRight = 5;
            colorBox.style.borderTopLeftRadius = 2;
            colorBox.style.borderTopRightRadius = 2;
            colorBox.style.borderBottomLeftRadius = 2;
            colorBox.style.borderBottomRightRadius = 2;

            var label = new Label(text);
            label.style.color = new Color(0.7f, 0.7f, 0.7f);
            label.style.fontSize = 11;

            container.Add(colorBox);
            container.Add(label);
            parent.Add(container);
        }
    }
}

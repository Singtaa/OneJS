using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    /// <summary>
    /// The legacy inspector's offer to move an app that is a cart to the project root
    /// (Specs/ROOT_CARTS.md 5.9, the legacy mockup). Shown only for an app whose ~ holds an
    /// oj.json; every other app sees the legacy inspector as it was.
    /// </summary>
    public partial class JSRunnerEditor {
        VisualElement _moveOffer;
        Label _moveOfferLabel;
        Button _moveOfferButton;
        string _moveOfferKey;
        double _moveOfferCheckedAt = double.NegativeInfinity;
        bool _moving;

        /// <summary>How often the offer re-reads the app's oj.json and the root, in seconds.</summary>
        const double MoveOfferInterval = 1.0;

        VisualElement CreateMoveOffer() {
            _moveOffer = CreateInfoStyleContainer();
            _moveOffer.style.marginTop = 6;
            _moveOffer.style.display = DisplayStyle.None;
            var row = CreateRow();
            _moveOfferLabel = new Label();
            _moveOfferLabel.style.whiteSpace = WhiteSpace.Normal;
            _moveOfferLabel.style.color = OneJSEditorDesign.Colors.TextInfo;
            _moveOfferLabel.style.flexGrow = 1;
            _moveOfferLabel.style.flexShrink = 1;
            row.Add(_moveOfferLabel);
            _moveOfferButton = new Button(ConfirmMove) { text = OneJSEditorDesign.Texts.MoveToProjectRoot };
            _moveOfferButton.style.flexShrink = 0;
            _moveOfferButton.style.marginLeft = 8;
            row.Add(_moveOfferButton);
            _moveOffer.Add(row);
            return _moveOffer;
        }

        /// <summary>Shows the offer when the app is a cart and nothing else is going on.</summary>
        void UpdateMoveOffer(bool validApp) {
            if (_moveOffer == null) return;
            if (!validApp || Application.isPlaying) {
                _moveOffer.style.display = DisplayStyle.None;
                _moveOfferKey = null;
                return;
            }
            if (EditorApplication.timeSinceStartup - _moveOfferCheckedAt < MoveOfferInterval) return;
            _moveOfferCheckedAt = EditorApplication.timeSinceStartup;

            var plan = MoveToProjectRoot.PlanFor(_target);
            var key = plan == null ? "none" : $"{plan.Name}|{plan.Problem}|{_moving}";
            if (key == _moveOfferKey) return;
            _moveOfferKey = key;
            _moveOffer.style.display = plan == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (plan == null) return;
            _moveOfferLabel.text = plan.Problem == null
                ? OneJSEditorDesign.Texts.MoveOffer
                : OneJSEditorDesign.Texts.MoveOffer + " " + plan.Problem;
            _moveOfferButton.SetEnabled(plan.Problem == null && !_moving);
            _moveOfferButton.text = _moving ? OneJSEditorDesign.Texts.Moving : OneJSEditorDesign.Texts.MoveToProjectRoot;
        }

        void ConfirmMove() {
            var plan = MoveToProjectRoot.PlanFor(_target);
            if (plan == null || plan.Problem != null) return;
            var ok = EditorUtility.DisplayDialog(OneJSEditorDesign.Texts.MoveToProjectRoot,
                $"Move {plan.Name} from {plan.Shown} to {plan.Folder}/ at the project root?\n\n" +
                "It moves with git mv when git tracks it. The files OneJS's setup wrote into it are removed where nobody changed them, " +
                "and the ones somebody did are left and listed in the Console. The runner then runs the cart by name, and the app's " +
                $"bundle in {plan.AssetFolder} is deleted. The Panel Settings and the prefab stay.",
                "Move", "Cancel");
            if (!ok) return;
            _moving = true;
            _moveOfferCheckedAt = double.NegativeInfinity;
            var started = MoveToProjectRoot.Move(_target, _ => _moving = false);
            if (!started) _moving = false;
        }
    }
}

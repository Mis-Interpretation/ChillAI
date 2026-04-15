using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChillAI.View.UI
{
    /// <summary>
    /// Reusable UI Toolkit chat-bubble component with a downward-pointing tail and
    /// pop-in / pop-out animation. Spawner owns the lifecycle: create via constructor,
    /// then <see cref="Attach"/> or <see cref="AnchorAbove"/> to show, and
    /// <see cref="Dismiss"/> to animate out and remove.
    /// </summary>
    public class TextBubble : VisualElement
    {
        const long DefaultShowDelayMs = 3000;
        const long ExitAnimationMs = 280;
        const float TransitionDurationSec = 0.22f;

        static readonly Color BgColor = new(30f / 255, 30f / 255, 30f / 255, 0.92f);
        static readonly Color BorderColor = new(1f, 1f, 1f, 0.15f);
        static readonly Color TextColor = new(240f / 255, 240f / 255, 240f / 255, 1f);

        readonly VisualElement _body;
        readonly Label _label;
        readonly VisualElement _tail;
        readonly long _showDelayMs;
        bool _dismissed;
        bool _shown;

        public TextBubble(string text, long showDelayMs = DefaultShowDelayMs, bool useAbsolutePosition = true)
        {
            pickingMode = PickingMode.Ignore;
            _showDelayMs = Math.Max(0L, showDelayMs);

            // Root — no background, just a positioning container
            style.position = useAbsolutePosition ? Position.Absolute : Position.Relative;
            style.maxWidth = 320;
            style.opacity = 0;
            style.scale = new StyleScale(new Scale(new Vector3(0.6f, 0.6f, 1f)));

            // Transition on opacity + scale
            style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new("opacity"), new("scale") });
            style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new(TransitionDurationSec, TimeUnit.Second), new(TransitionDurationSec, TimeUnit.Second) });
            style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new(EasingMode.EaseOut), new(EasingMode.EaseOut) });

            // Body — rounded rectangle
            _body = new VisualElement { pickingMode = PickingMode.Ignore };
            _body.style.paddingTop = 6;
            _body.style.paddingBottom = 6;
            _body.style.paddingLeft = 12;
            _body.style.paddingRight = 12;
            _body.style.backgroundColor = BgColor;
            _body.style.borderTopLeftRadius = 10;
            _body.style.borderTopRightRadius = 10;
            _body.style.borderBottomLeftRadius = 10;
            _body.style.borderBottomRightRadius = 10;
            _body.style.borderTopWidth = 1;
            _body.style.borderBottomWidth = 1;
            _body.style.borderLeftWidth = 1;
            _body.style.borderRightWidth = 1;
            _body.style.borderTopColor = BorderColor;
            _body.style.borderBottomColor = BorderColor;
            _body.style.borderLeftColor = BorderColor;
            _body.style.borderRightColor = BorderColor;
            Add(_body);

            // Label
            _label = new Label(text) { pickingMode = PickingMode.Ignore };
            _label.style.color = TextColor;
            _label.style.fontSize = 16;
            _label.style.whiteSpace = WhiteSpace.Normal;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _body.Add(_label);

            // Tail — small rotated square, top half hidden behind body
            _tail = new VisualElement { pickingMode = PickingMode.Ignore };
            _tail.style.position = Position.Absolute;
            _tail.style.width = 10;
            _tail.style.height = 10;
            _tail.style.backgroundColor = BgColor;
            _tail.style.rotate = new StyleRotate(new Rotate(45f));
            _tail.style.bottom = -5;
            _tail.style.left = Length.Percent(50);
            _tail.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), 0));
            _tail.style.borderRightWidth = 1;
            _tail.style.borderBottomWidth = 1;
            _tail.style.borderRightColor = BorderColor;
            _tail.style.borderBottomColor = BorderColor;
            _tail.style.borderTopWidth = 0;
            _tail.style.borderLeftWidth = 0;
            Add(_tail);
        }

        public string Text
        {
            get => _label.text;
            set => _label.text = value;
        }

        /// <summary>Add the bubble to an arbitrary parent and play the enter animation.</summary>
        public void Attach(VisualElement parent)
        {
            if (parent == null || _dismissed) return;
            parent.Add(this);
            DeferShow();
        }

        /// <summary>
        /// Anchor the bubble centered above <paramref name="anchor"/>.
        /// The bubble becomes a child of the anchor so it moves together with it.
        /// </summary>
        public TextBubble AnchorAbove(VisualElement anchor, float gap = 8f)
        {
            if (anchor == null) return this;

            anchor.Add(this);
            style.left = Length.Percent(50);
            style.bottom = Length.Percent(100);
            style.marginBottom = gap;
            style.translate = new StyleTranslate(new Translate(Length.Percent(-50), 0));

            DeferShow();
            return this;
        }

        /// <summary>Play exit animation then remove from hierarchy. Idempotent.</summary>
        public void Dismiss()
        {
            if (_dismissed) return;
            _dismissed = true;
            style.opacity = 0;
            style.scale = new StyleScale(new Scale(new Vector3(0.6f, 0.6f, 1f)));
            schedule.Execute(() => RemoveFromHierarchy()).StartingIn(ExitAnimationMs);
        }

        void DeferShow()
        {
            if (_shown) return;
            _shown = true;
            schedule.Execute(() =>
            {
                if (!_dismissed)
                {
                    style.opacity = 1;
                    style.scale = new StyleScale(new Scale(Vector3.one));
                }
            }).StartingIn(_showDelayMs);
        }
    }
}

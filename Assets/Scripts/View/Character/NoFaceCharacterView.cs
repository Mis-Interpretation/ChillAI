using System;
using System.Collections.Generic;
using ChillAI.Core;
using ChillAI.Core.Signals;
using UnityEngine;
using Zenject;

namespace ChillAI.View.Character
{
    public class NoFaceCharacterView : MonoBehaviour
    {
        [Header("Expression Sprites (assign in Inspector)")]
        [SerializeField] Sprite neutralSprite;
        [SerializeField] Sprite focusedSprite;
        [SerializeField] Sprite excitedSprite;
        [SerializeField] Sprite happySprite;
        [SerializeField] Sprite sleepySprite;

        [Header("References")]
        [SerializeField] SpriteRenderer spriteRenderer;

        SignalBus _signalBus;
        Dictionary<ExpressionType, Sprite> _spriteMap;

        [Inject]
        public void Construct(SignalBus signalBus)
        {
            _signalBus = signalBus;
        }

        void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            _spriteMap = new Dictionary<ExpressionType, Sprite>
            {
                { ExpressionType.Neutral, neutralSprite },
                { ExpressionType.Focused, focusedSprite },
                { ExpressionType.Excited, excitedSprite },
                { ExpressionType.Happy, happySprite },
                { ExpressionType.Sleepy, sleepySprite }
            };
        }

        void OnEnable()
        {
            _signalBus?.Subscribe<ExpressionChangedSignal>(OnExpressionChanged);
        }

        void OnDisable()
        {
            _signalBus?.TryUnsubscribe<ExpressionChangedSignal>(OnExpressionChanged);
        }

        void OnExpressionChanged(ExpressionChangedSignal signal)
        {
            if (spriteRenderer == null) return;

            if (_spriteMap.TryGetValue(signal.Expression, out var sprite) && sprite != null)
            {
                spriteRenderer.sprite = sprite;
            }

            Debug.Log($"[ChillAI] Character expression -> {signal.Expression}");
        }
    }
}

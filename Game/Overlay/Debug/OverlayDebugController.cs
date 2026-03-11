#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal sealed class OverlayDebugController : MonoBehaviour
{
    private const float RefreshInterval = 0.2f;
    private const float DefaultMoveStep = 0.5f;
    private const float FineMoveStep = 0.1f;
    private const float RotationStep = 5f;
    private const float SizeStep = 0.25f;
    private const float ThicknessStep = 0.02f;
    private const float ScaleStep = 0.05f;

    internal struct DebugState
    {
        public string DataSource;
        public string EncounterId;
        public string MonsterTitle;
        public bool Visible;
        public Vector3 AnchorPosition;
        public Vector3 AnchorRotationEuler;
        public Vector3 LocalOffset;
        public Vector2 BoardSize;
        public float CardSpacingX;
        public float CardScale;
        public float BoardThickness;
        public float BorderThickness;
        public float BorderHeight;
    }

    private MonsterPreviewOverlayController _overlayController;
    private FixedWorldAnchorSource _anchorSource;
    private PreviewBoardLayout _layout;
    private string _lastCardSignature = string.Empty;
    private float _nextRefreshTime;
    private bool _anchorSeeded;
    private bool _useMonsterDatabase = true;
    private readonly Guid _defaultEncounterId = Guid.Parse("1d24717f-7bfb-48e9-9320-6a69d72e233e");
    private Guid? _activeEncounterId;
    private string _activeMonsterTitle = string.Empty;

    private void Awake()
    {
        _overlayController = GetComponent<MonsterPreviewOverlayController>();
        _anchorSource = new FixedWorldAnchorSource();
        _layout = new PreviewBoardLayout();

        if (_overlayController != null)
        {
            _overlayController.SetAnchorSource(_anchorSource);
            _overlayController.SetLayout(CloneLayout(_layout));
            _overlayController.SetVisible(false);
        }
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || _overlayController == null)
            return;

        if (keyboard.f3Key.wasPressedThisFrame)
        {
            if (!_anchorSeeded)
                SeedAnchorFromCamera();

            _overlayController.SetVisible(!_overlayController.Visible);
            if (_overlayController.Visible)
                SyncPreviewData();
            ModState.Logger?.LogInfo(
                $"[OverlayDebugController] Preview visible={_overlayController.Visible}"
            );
        }

        if (keyboard.f4Key.wasPressedThisFrame)
        {
            _useMonsterDatabase = !_useMonsterDatabase;
            _lastCardSignature = string.Empty;
            SyncPreviewData();
            ModState.Logger?.LogInfo(
                $"[OverlayDebugController] Preview data source={(_useMonsterDatabase ? "monster_db" : "player_hand")}"
            );
        }

        if (!_overlayController.Visible)
            return;

        HandleAnchorControls(keyboard);
        HandleLayoutControls(keyboard);

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            SyncPreviewData();
        }
    }

    public bool TryGetDebugState(out DebugState state)
    {
        if (_overlayController == null || _anchorSource == null || _layout == null)
        {
            state = default;
            return false;
        }

        state = new DebugState
        {
            DataSource = _useMonsterDatabase ? "monster_db" : "player_hand",
            EncounterId = _activeEncounterId?.ToString() ?? "-",
            MonsterTitle = string.IsNullOrEmpty(_activeMonsterTitle) ? "-" : _activeMonsterTitle,
            Visible = _overlayController.Visible,
            AnchorPosition = _anchorSource.Position,
            AnchorRotationEuler = _anchorSource.Rotation.eulerAngles,
            LocalOffset = _layout.LocalOffset,
            BoardSize = _layout.BoardSize,
            CardSpacingX = _layout.CardSpacing.x,
            CardScale = _layout.CardScale.x,
            BoardThickness = _layout.BoardThickness,
            BorderThickness = _layout.BorderThickness,
            BorderHeight = _layout.BorderHeight,
        };
        return true;
    }

    private void HandleAnchorControls(Keyboard keyboard)
    {
        var moved = false;
        var moveStep = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
            ? FineMoveStep
            : DefaultMoveStep;

        var position = _anchorSource.Position;
        if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            position.x -= moveStep;
            moved = true;
        }
        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            position.x += moveStep;
            moved = true;
        }
        if (keyboard.upArrowKey.wasPressedThisFrame)
        {
            position.z += moveStep;
            moved = true;
        }
        if (keyboard.downArrowKey.wasPressedThisFrame)
        {
            position.z -= moveStep;
            moved = true;
        }
        if (keyboard.pageUpKey.wasPressedThisFrame)
        {
            position.y += moveStep;
            moved = true;
        }
        if (keyboard.pageDownKey.wasPressedThisFrame)
        {
            position.y -= moveStep;
            moved = true;
        }
        if (moved)
            _anchorSource.Position = position;

        var rotation = _anchorSource.Rotation;
        if (keyboard.commaKey.wasPressedThisFrame)
        {
            rotation = Quaternion.Euler(0f, -RotationStep, 0f) * rotation;
            moved = true;
        }
        if (keyboard.periodKey.wasPressedThisFrame)
        {
            rotation = Quaternion.Euler(0f, RotationStep, 0f) * rotation;
            moved = true;
        }
        if (rotation != _anchorSource.Rotation)
            _anchorSource.Rotation = rotation;

        if (keyboard.rKey.wasPressedThisFrame)
        {
            SeedAnchorFromCamera();
            moved = true;
        }

        if (moved)
        {
            ModState.Logger?.LogInfo(
                $"[OverlayDebugController] Anchor pos={_anchorSource.Position} rot={_anchorSource.Rotation.eulerAngles}"
            );
        }
    }

    private void HandleLayoutControls(Keyboard keyboard)
    {
        var layoutChanged = false;

        if (keyboard.digit1Key.wasPressedThisFrame)
        {
            _layout.BoardSize.x = Mathf.Max(1f, _layout.BoardSize.x - SizeStep);
            layoutChanged = true;
        }
        if (keyboard.digit2Key.wasPressedThisFrame)
        {
            _layout.BoardSize.x += SizeStep;
            layoutChanged = true;
        }
        if (keyboard.digit3Key.wasPressedThisFrame)
        {
            _layout.BoardSize.y = Mathf.Max(1f, _layout.BoardSize.y - SizeStep);
            layoutChanged = true;
        }
        if (keyboard.digit4Key.wasPressedThisFrame)
        {
            _layout.BoardSize.y += SizeStep;
            layoutChanged = true;
        }
        if (keyboard.digit5Key.wasPressedThisFrame)
        {
            _layout.CardSpacing.x = Mathf.Max(0.2f, _layout.CardSpacing.x - SizeStep);
            layoutChanged = true;
        }
        if (keyboard.digit6Key.wasPressedThisFrame)
        {
            _layout.CardSpacing.x += SizeStep;
            layoutChanged = true;
        }
        if (keyboard.minusKey.wasPressedThisFrame)
        {
            _layout.CardScale = Vector3.one
                * Mathf.Max(0.1f, _layout.CardScale.x - ScaleStep);
            layoutChanged = true;
        }
        if (keyboard.equalsKey.wasPressedThisFrame)
        {
            _layout.CardScale = Vector3.one * (_layout.CardScale.x + ScaleStep);
            layoutChanged = true;
        }
        if (keyboard.kKey.wasPressedThisFrame)
        {
            _layout.BoardThickness = Mathf.Max(0.01f, _layout.BoardThickness - ThicknessStep);
            layoutChanged = true;
        }
        if (keyboard.lKey.wasPressedThisFrame)
        {
            _layout.BoardThickness += ThicknessStep;
            layoutChanged = true;
        }
        if (keyboard.semicolonKey.wasPressedThisFrame)
        {
            _layout.BorderThickness = Mathf.Max(0.01f, _layout.BorderThickness - ThicknessStep);
            layoutChanged = true;
        }
        if (keyboard.quoteKey.wasPressedThisFrame)
        {
            _layout.BorderThickness += ThicknessStep;
            layoutChanged = true;
        }
        if (keyboard.nKey.wasPressedThisFrame)
        {
            _layout.BorderHeight = Mathf.Max(0.01f, _layout.BorderHeight - ThicknessStep);
            layoutChanged = true;
        }
        if (keyboard.mKey.wasPressedThisFrame)
        {
            _layout.BorderHeight += ThicknessStep;
            layoutChanged = true;
        }

        if (!layoutChanged)
            return;

        ApplyLayout();
        ModState.Logger?.LogInfo(
            $"[OverlayDebugController] Layout size={_layout.BoardSize} offset={_layout.LocalOffset} spacingX={_layout.CardSpacing.x:F2} scale={_layout.CardScale.x:F2} boardT={_layout.BoardThickness:F2} borderT={_layout.BorderThickness:F2} borderH={_layout.BorderHeight:F2}"
        );
    }

    private void SyncPreviewData()
    {
        if (_useMonsterDatabase)
        {
            SyncCardsFromMonsterDatabase();
            return;
        }

        SyncCardsFromHand();
    }

    private void SyncCardsFromMonsterDatabase()
    {
        _activeEncounterId = _defaultEncounterId;

        if (!MonsterDatabase.TryGetByEncounterId(_defaultEncounterId, out var monster))
        {
            _activeMonsterTitle = string.Empty;
            ModState.Logger?.LogWarning(
                $"[OverlayDebugController] Monster DB miss encounterId={_defaultEncounterId}"
            );
            _overlayController.SetCards(new List<PreviewCardSpec>());
            return;
        }

        _activeMonsterTitle = monster.Title;
        var specs = MonsterPreviewSpecBuilder.Build(monster);
        ModState.Logger?.LogInfo(
            $"[OverlayDebugController] Monster DB hit encounterId={_defaultEncounterId} title={monster.Title} boardCards={monster.BoardCards.Count} previewCards={specs.Count}"
        );
        var signature = BuildSignature(specs);
        if (signature == _lastCardSignature)
        {
            ModState.Logger?.LogDebug(
                $"[OverlayDebugController] Monster preview signature unchanged encounterId={_defaultEncounterId}"
            );
            return;
        }

        _lastCardSignature = signature;
        _overlayController.SetCards(specs);
    }

    private void SyncCardsFromHand()
    {
        _activeEncounterId = null;
        _activeMonsterTitle = string.Empty;

        var handCards = GameDataReader.GetItemsAsCards(Data.Run?.Player?.Hand);
        var specs = BuildCardSpecs(handCards);
        var signature = BuildSignature(specs);
        if (signature == _lastCardSignature)
        {
            ModState.Logger?.LogDebug("[OverlayDebugController] Player hand preview signature unchanged");
            return;
        }

        _lastCardSignature = signature;
        ModState.Logger?.LogInfo(
            $"[OverlayDebugController] Player hand preview cards={specs.Count}"
        );
        _overlayController.SetCards(specs);
    }

    private void SeedAnchorFromCamera()
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        _anchorSource.Position = camera.transform.position + camera.transform.forward * 12f;
        _anchorSource.Rotation = Quaternion.identity;
        _anchorSeeded = true;
        ModState.Logger?.LogInfo(
            $"[OverlayDebugController] Seeded anchor from camera: {_anchorSource.Position}"
        );
    }

    private void ApplyLayout()
    {
        _overlayController?.SetLayout(CloneLayout(_layout));
    }

    private static List<PreviewCardSpec> BuildCardSpecs(List<Card> cards)
    {
        var specs = new List<PreviewCardSpec>();
        if (cards == null)
            return specs;

        foreach (var card in cards)
        {
            if (card == null || card.Type != ECardType.Item)
                continue;

            specs.Add(
                new PreviewCardSpec
                {
                    TemplateId = card.TemplateId.ToString(),
                    Tier = (int)card.Tier,
                    Enchant = (card as ItemCard)?.Enchantment?.ToString() ?? "None",
                    Attributes = card.Attributes?.ToDictionary(kv => (int)kv.Key, kv => kv.Value)
                        ?? new Dictionary<int, int>(),
                }
            );
        }

        return specs;
    }

    private static string BuildSignature(IReadOnlyList<PreviewCardSpec> cards)
    {
        return string.Join(
            "|",
            cards.Select(card =>
                string.Join(
                    ";",
                    card.TemplateId,
                    card.Tier.ToString(),
                    card.Enchant ?? "None",
                    string.Join(
                        ",",
                        card.Attributes
                            .OrderBy(kv => kv.Key)
                            .Select(kv => $"{kv.Key}:{kv.Value}")
                    )
                )
            )
        );
    }

    private static PreviewBoardLayout CloneLayout(PreviewBoardLayout layout)
    {
        return new PreviewBoardLayout
        {
            LocalOffset = layout.LocalOffset,
            CardSpacing = layout.CardSpacing,
            CardScale = layout.CardScale,
            BoardSize = layout.BoardSize,
            BoardThickness = layout.BoardThickness,
            BorderThickness = layout.BorderThickness,
            BorderHeight = layout.BorderHeight,
        };
    }
}

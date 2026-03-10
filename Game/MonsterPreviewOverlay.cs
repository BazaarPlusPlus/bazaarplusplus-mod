#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using Newtonsoft.Json;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

/// <summary>
/// Renders a horizontal row of 3D card previews from a JSON list.
/// Input format: [{"t":"templateGuid","r":tier,"e":"enchant","a":{attrId:val}}]
/// Toggle visibility with F3.
/// </summary>

internal class MonsterPreviewOverlay : MonoBehaviour
{
    private const string OpponentDeckName = "Player_Deck";
    private const string OpponentDeckCloneName = "Player_Deck_clone";
    private const string PlayerDeckPath = "Game/=== BoardAnchor ===/BoardBase(Clone)/Board_Common_CenterPanels/Player_Deck";

    // How far "forward" (toward opponent) from the leftmost storage socket to place cards.
    // Positive = toward opponent, negative = toward player. Tune this value in-game.
    private const float ForwardOffset = 2.0f;

    private bool _visible = true;
    private string _inputJson = "[]";
    private string _appliedJson = "";
    private bool _syncInFlight;
    private int _syncVersion;

    private readonly List<GameObject> _entities = new List<GameObject>();
    private readonly List<GameObject> _anchors = new List<GameObject>();

    private MethodInfo _instantiateCardMethod;
    private MethodInfo _itemControllerOnDisable;
    private object _spawnSection;
    private Transform _opponentDeckClone;
    private bool _ownsOpponentDeckClone;
    private bool _deckLayoutLocked;
    private int _lockedDeckInstanceId;
    private Vector3 _lockedDeckLeftEdgeLocal;
    private Quaternion _lockedDeckLocalRotation;

    private sealed class CardEntry
    {
        [JsonProperty("t")]
        public string TemplateId { get; set; } = string.Empty;

        [JsonProperty("r")]
        public int Tier { get; set; }

        [JsonProperty("e")]
        public string Enchant { get; set; } = "None";

        [JsonProperty("a")]
        public Dictionary<int, int> Attributes { get; set; } = new Dictionary<int, int>();
    }
    /// <summary>
    /// Feed a JSON array of card descriptors to render.
    /// </summary>
    public void SetJson(string json)
    {
        _inputJson = json ?? "[]";
    }

    private void Update()
    {
        if (Keyboard.current?.f3Key.wasPressedThisFrame == true)
        {
            _visible = !_visible;
            SetOverlayVisible(_visible);
            if (_visible)
                ApplyLayout();
        }

        if (_visible)
            EnsureOpponentDeckCloneExists();

        if (
            _visible
            && !string.Equals(_inputJson, _appliedJson, StringComparison.Ordinal)
            && !_syncInFlight
        )
        {
            _syncVersion++;
            _ = SyncAsync(_syncVersion);
        }
    }

    private async Task SyncAsync(int version)
    {
        _syncInFlight = true;
        try
        {
            HideAll();
            _appliedJson = _inputJson;
            ModState.Logger?.LogInfo(
                $"[MonsterPreviewOverlay] SyncAsync start: jsonLen={_appliedJson.Length}"
            );

            List<CardEntry> cards;
            try
            {
                cards =
                    JsonConvert.DeserializeObject<List<CardEntry>>(_appliedJson)
                    ?? new List<CardEntry>();
            }
            catch
            {
                cards = new List<CardEntry>();
            }

            ModState.Logger?.LogInfo(
                $"[MonsterPreviewOverlay] Parsed {cards.Count} card entries"
            );
            if (cards.Count == 0)
                return;

            Services.TryGet<AssetLoader>(out var loader);
            if (loader == null || !EnsureApi(loader))
            {
                ModState.Logger?.LogWarning(
                    $"[MonsterPreviewOverlay] API not ready: loader={loader != null}, api={_instantiateCardMethod != null}"
                );
                return;
            }

            var staticData = await Data.GetStatic();
            ModState.Logger?.LogInfo(
                $"[MonsterPreviewOverlay] staticData={staticData != null}"
            );

            for (var i = 0; i < cards.Count; i++)
            {
                if (version != _syncVersion)
                    return;

                var runtime = BuildCard(cards[i], staticData);
                if (runtime == null)
                {
                    ModState.Logger?.LogWarning(
                        $"[MonsterPreviewOverlay] BuildCard returned null for index {i}, templateId={cards[i]?.TemplateId}"
                    );
                    continue;
                }

                var anchor = new GameObject($"BPP_MonsterPreview_{i}");
                var overlayParent = EnsureOpponentDeckCloneExists();
                if (overlayParent != null)
                    anchor.transform.SetParent(overlayParent, worldPositionStays: false);
                anchor.transform.localScale = Vector3.one * 0.6f;
                _anchors.Add(anchor);

                GameObject spawned = null;
                try
                {
                    spawned = await InstantiateAsync(loader, runtime, anchor);
                }
                catch (Exception ex)
                {
                    ModState.Logger?.LogWarning(
                        $"[MonsterPreviewOverlay] Instantiate failed: {ex.Message}"
                    );
                }

                if (spawned == null)
                {
                    // Remove the empty anchor so it doesn't mess up layout
                    _anchors.RemoveAt(_anchors.Count - 1);
                    Destroy(anchor);
                    continue;
                }

                if (version != _syncVersion)
                {
                    spawned.PoolObject();
                    return;
                }

                spawned.AddComponent<ShowcaseCardMarker>();
                ConfigureSpawned(spawned);
                _entities.Add(spawned);
                ModState.Logger?.LogInfo(
                    $"[MonsterPreviewOverlay] Spawned card {i}: {spawned.name}, pos={spawned.transform.position}"
                );
            }

            ModState.Logger?.LogInfo(
                $"[MonsterPreviewOverlay] Spawn done: entities={_entities.Count}, anchors={_anchors.Count}"
            );
            ApplyLayout();
        }
        finally
        {
            _syncInFlight = false;
        }
    }

    private void ConfigureSpawned(GameObject go)
    {
        if (go.TryGetComponent<ItemController>(out var ic))
        {
            ic.ShowCard(true);
            ic.EnableMovement(false);
            // Invoke OnDisable via reflection to remove Events/VFX subscriptions
            // while keeping pointer/tooltip events alive.
            _itemControllerOnDisable ??= typeof(ItemController).GetMethod(
                "OnDisable",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            _itemControllerOnDisable?.Invoke(ic, null);
        }
        else if (go.TryGetComponent<CardController>(out var cc))
        {
            cc.EnableMovement(false);
            cc.ShowCard(true);
        }
    }

    #region Card building

    private static ItemCard BuildCard(CardEntry entry, object staticData)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.TemplateId))
            return null;

        if (!Guid.TryParse(entry.TemplateId, out var templateId))
            return null;

        var template = GetTemplate(staticData, templateId) as ITCard;
        if (template == null)
            return null;

        var card = new ItemCard
        {
            InstanceId = InstanceId.New("ppmon"),
            TemplateId = templateId,
            Template = template,
            Tier = (ETier)Mathf.Clamp(entry.Tier, 0, 5),
            Size = template.Size,
            Type = ECardType.Item,
            Attributes = new Dictionary<ECardAttributeType, int>(),
            Tags = new HashSet<ECardTag>(),
            HiddenTags = new HashSet<EHiddenTag>(),
            Heroes = new HashSet<EHero>(),
            Owner = null,
            Section = null,
            LeftSocketId = null,
        };

        if (
            !string.IsNullOrWhiteSpace(entry.Enchant)
            && !string.Equals(entry.Enchant, "None", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(entry.Enchant, out EEnchantmentType enchantType)
        )
        {
            card.Enchantment = enchantType;
        }

        if (entry.Attributes != null)
        {
            foreach (var kv in entry.Attributes)
            {
                if (Enum.IsDefined(typeof(ECardAttributeType), kv.Key))
                    card.Attributes[(ECardAttributeType)kv.Key] = kv.Value;
            }
        }

        return card;
    }

    private static object GetTemplate(object staticData, Guid templateId)
    {
        if (staticData == null)
            return null;

        var method = staticData
            .GetType()
            .GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        return method?.Invoke(staticData, new object[] { templateId });
    }

    #endregion

    #region Asset instantiation

    private bool EnsureApi(AssetLoader loader)
    {
        if (_instantiateCardMethod != null && _spawnSection != null)
            return true;

        _instantiateCardMethod = loader
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, "InstantiateCardAsync", StringComparison.Ordinal))
                    return false;
                var ps = m.GetParameters();
                return ps.Length == 3
                    && ps[0].ParameterType == typeof(Card)
                    && ps[1].ParameterType == typeof(GameObject)
                    && ps[2].ParameterType.IsEnum;
            });

        if (_instantiateCardMethod == null)
            return false;

        var sectionType = _instantiateCardMethod.GetParameters()[2].ParameterType;
        if (!sectionType.IsEnum)
            return false;

        var names = Enum.GetNames(sectionType);
        if (names.Contains("Opponent"))
            _spawnSection = Enum.Parse(sectionType, "Opponent");
        else if (names.Contains("Storage"))
            _spawnSection = Enum.Parse(sectionType, "Storage");
        else
            _spawnSection = Enum.ToObject(sectionType, 0);

        return _spawnSection != null;
    }

    private async Task<GameObject> InstantiateAsync(
        AssetLoader loader,
        Card card,
        GameObject parent
    )
    {
        if (_instantiateCardMethod == null || _spawnSection == null)
            return null;

        var taskObj = _instantiateCardMethod.Invoke(
            loader,
            new object[] { card, parent, _spawnSection }
        );
        if (taskObj is Task<GameObject> task)
            return await task;

        if (taskObj is Task t)
        {
            await t;
            return t.GetType().GetProperty("Result")?.GetValue(t) as GameObject;
        }

        return null;
    }

    #endregion


    #region Opponent Deck Clone

        private Transform EnsureOpponentDeckCloneExists()
    {
        if (_opponentDeckClone != null)
        {
            _opponentDeckClone.gameObject.SetActive(true);
            return _opponentDeckClone;
        }

        var original = FindOpponentDeckTransform();
        if (original == null || original.parent == null)
            return null;

        var parent = original.parent;
        var existing = parent.Find(OpponentDeckCloneName);
        if (existing != null)
        {
            _opponentDeckClone = existing;
            _opponentDeckClone.gameObject.SetActive(true);
            _ownsOpponentDeckClone = false;
            return _opponentDeckClone;
        }

        var clone = new GameObject(OpponentDeckCloneName);
        clone.transform.SetParent(parent, worldPositionStays: false);
        clone.transform.SetLocalPositionAndRotation(original.localPosition, original.localRotation);
        clone.transform.localScale = original.localScale;
        clone.SetActive(true);

        _opponentDeckClone = clone.transform;
        _ownsOpponentDeckClone = true;

        ModState.Logger?.LogInfo(
            $"[MonsterPreviewOverlay] Created {OpponentDeckCloneName} (from Player_Deck) under parent={parent.name}"
        );
        return _opponentDeckClone;
    }
        private static Transform FindOpponentDeckTransform()
    {
        var byPath = GameObject.Find(PlayerDeckPath);
        if (byPath != null)
            return byPath.transform;

        var board = Singleton<BoardManager>.Instance;
        if (board == null)
            return null;

        return board
            .GetComponentsInChildren<Transform>(includeInactive: true)
            .FirstOrDefault(t => string.Equals(t.name, OpponentDeckName, StringComparison.Ordinal));
    }
    #endregion
    #region Layout

        private void ApplyLayout()
    {
        if (_anchors.Count == 0)
            return;

        if (!TryGetDeckRegion(out var leftEdge, out var rotation) && !TryGetTopRegion(out leftEdge, out rotation))
        {
            ModState.Logger?.LogWarning(
                "[MonsterPreviewOverlay] TryGetTopRegion failed - board sockets not available"
            );
            return;
        }

        ModState.Logger?.LogInfo(
            $"[MonsterPreviewOverlay] Layout: leftEdge={leftEdge}, rotation={rotation.eulerAngles}, anchors={_anchors.Count}"
        );

        var right = (rotation * Vector3.right).normalized;
        var overlayParent = EnsureOpponentDeckCloneExists();
        var useLocalLayout = overlayParent != null;
        var leftEdgeLocal = useLocalLayout
            ? overlayParent.InverseTransformPoint(leftEdge)
            : Vector3.zero;
        var rightLocal = useLocalLayout
            ? overlayParent.InverseTransformDirection(right).normalized
            : Vector3.right;
        var localRotation = useLocalLayout
            ? Quaternion.Inverse(overlayParent.rotation) * rotation
            : rotation;
        var edge = 0f;

        foreach (var anchor in _anchors)
        {
            if (anchor == null || anchor.transform.childCount == 0)
                continue;

            if (useLocalLayout && anchor.transform.parent == overlayParent)
                anchor.transform.localRotation = localRotation;
            else
                anchor.transform.rotation = rotation;

            var cardObj = anchor.transform.GetChild(0).gameObject;
            GetCardProjection(cardObj, right, anchor.transform.position, out var cardLeft, out var cardWidth);

            if (cardWidth <= 0.001f)
            {
                cardLeft = -0.18f;
                cardWidth = 0.36f;
            }

            if (useLocalLayout && anchor.transform.parent == overlayParent)
                anchor.transform.localPosition = leftEdgeLocal + rightLocal * (edge - cardLeft);
            else
                anchor.transform.position = leftEdge + right * (edge - cardLeft);

            edge += cardWidth;
        }
    }
        private void LockDeckLayout(
        Transform overlay,
        int deckInstanceId,
        Vector3 leftEdgeWorld,
        Quaternion rotationWorld
    )
    {
        _deckLayoutLocked = true;
        _lockedDeckInstanceId = deckInstanceId;
        _lockedDeckLeftEdgeLocal = overlay.InverseTransformPoint(leftEdgeWorld);
        _lockedDeckLocalRotation = Quaternion.Inverse(overlay.rotation) * rotationWorld;
    }

    private bool TryGetDeckRegion(out Vector3 leftEdge, out Quaternion rotation)
    {
        leftEdge = Vector3.zero;
        rotation = Quaternion.identity;

        var overlay = EnsureOpponentDeckCloneExists();
        if (overlay == null)
            return false;

        var originalDeck = FindOpponentDeckTransform();
        if (originalDeck == null)
            return false;

        var deckInstanceId = originalDeck.GetInstanceID();
        if (_deckLayoutLocked && deckInstanceId == _lockedDeckInstanceId)
        {
            leftEdge = overlay.TransformPoint(_lockedDeckLeftEdgeLocal);
            rotation = overlay.rotation * _lockedDeckLocalRotation;
            return true;
        }

        rotation = overlay.rotation;
        var right = (rotation * Vector3.right).normalized;

        var renderers = originalDeck.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers == null || renderers.Length == 0)
        {
            leftEdge = overlay.position;
            LockDeckLayout(overlay, deckInstanceId, leftEdge, rotation);
            return true;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var ext = bounds.extents;
        var halfProj =
            Mathf.Abs(Vector3.Dot(right, Vector3.right)) * ext.x
            + Mathf.Abs(Vector3.Dot(right, Vector3.up)) * ext.y
            + Mathf.Abs(Vector3.Dot(right, Vector3.forward)) * ext.z;

        leftEdge = bounds.center - right * halfProj;
        LockDeckLayout(overlay, deckInstanceId, leftEdge, rotation);
        return true;
    }
    /// <summary>
    /// Coordinate system: x=horizontal, y=camera distance, z=screen up/down.
    /// Offset Z from leftmost storage socket to move cards up on screen.
    /// </summary>
    private static bool TryGetTopRegion(out Vector3 leftEdge, out Quaternion rotation)
    {
        leftEdge = Vector3.zero;
        rotation = Quaternion.identity;

        var board = Singleton<BoardManager>.Instance;
        if (board?.playerStorageSockets == null || board.playerStorageSockets.Length == 0)
            return false;

        var sockets = board.playerStorageSockets;

        // Find leftmost socket (smallest x) as the starting anchor
        ItemSocketController leftmost = null;
        var minX = float.MaxValue;
        foreach (var s in sockets)
        {
            if (s == null)
                continue;
            if (s.transform.position.x < minX)
            {
                minX = s.transform.position.x;
                leftmost = s;
            }
        }
        if (leftmost == null)
            return false;

        // Start at leftmost socket, shift up on screen by ForwardOffset (Z axis)
        var pos = leftmost.transform.position;
        leftEdge = new Vector3(pos.x, pos.y, pos.z + ForwardOffset);
        return true;
    }

        private static void GetCardProjection(
        GameObject cardObj,
        Vector3 right,
        Vector3 originWorld,
        out float cardLeft,
        out float cardWidth
    )
    {
        cardLeft = 0f;
        cardWidth = 0f;

        if (cardObj == null)
            return;

        var box = cardObj.GetComponentInChildren<BoxCollider>(includeInactive: true);
        if (box == null)
            return;

        var t = box.transform;
        var hs = box.size * 0.5f;

        var halfProj =
            Mathf.Abs(Vector3.Dot(right, t.right)) * hs.x * t.lossyScale.x
            + Mathf.Abs(Vector3.Dot(right, t.up)) * hs.y * t.lossyScale.y
            + Mathf.Abs(Vector3.Dot(right, t.forward)) * hs.z * t.lossyScale.z;

        var centerProj = Vector3.Dot(t.TransformPoint(box.center) - originWorld, right);

        cardLeft = centerProj - halfProj;
        cardWidth = halfProj * 2f;
    }
    #endregion

    private void HideAll()
    {
        foreach (var entity in _entities)
        {
            if (entity == null)
                continue;

            var marker = entity.GetComponent<ShowcaseCardMarker>();
            if (marker != null)
                Destroy(marker);

            entity.transform.SetParent(null);
            entity.PoolObject();
        }
        _entities.Clear();

        foreach (var anchor in _anchors)
        {
            if (anchor != null)
                Destroy(anchor);
        }
        _anchors.Clear();
    }


        private void SetOverlayVisible(bool visible)
    {
        if (_opponentDeckClone != null)
            _opponentDeckClone.gameObject.SetActive(visible);

        foreach (var anchor in _anchors)
        {
            if (anchor != null)
                anchor.SetActive(visible);
        }
    }
    private void OnDestroy()
    {
        HideAll();

        if (_ownsOpponentDeckClone && _opponentDeckClone != null)
            Destroy(_opponentDeckClone.gameObject);
    }
}















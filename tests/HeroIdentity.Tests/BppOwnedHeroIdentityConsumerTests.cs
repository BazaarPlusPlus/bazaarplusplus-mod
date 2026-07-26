using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;
using BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;
using BazaarPlusPlus.GameInterop.Heroes;
using Xunit;

namespace HeroIdentity.Tests;

public sealed class BppOwnedHeroIdentityConsumerTests
{
    [Fact]
    public void Random_hero_selection_matches_runtime_and_stored_aliases()
    {
        Assert.True(RandomHeroPoolHeroIdentity.Matches("Hero8", "TheDragons"));
        Assert.True(RandomHeroPoolHeroIdentity.Matches("TheDragons", "Hero8"));
        Assert.False(RandomHeroPoolHeroIdentity.Matches("Vanessa", "TheDragons"));
    }

    [Fact]
    public void Skin_preference_canonical_key_wins_without_touching_legacy()
    {
        var values = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["canonical"] = ["canonical-skin"],
            ["legacy"] = ["legacy-skin"],
        };
        var writes = new List<string>();
        var deletes = new List<string>();

        var selected = RandomHeroSkinPoolPreferenceMigration.LoadCanonicalFirst(
            ["canonical", "legacy"],
            values.ContainsKey,
            key => values[key],
            (key, _) => writes.Add(key),
            deletes.Add
        );

        Assert.Equal(["canonical-skin"], selected);
        Assert.Empty(writes);
        Assert.Empty(deletes);
    }

    [Fact]
    public void Skin_preference_legacy_value_migrates_after_canonical_save()
    {
        var values = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["legacy"] = ["skin-a", "skin-b"],
        };
        var operations = new List<string>();

        var selected = RandomHeroSkinPoolPreferenceMigration.LoadCanonicalFirst(
            ["canonical", "legacy"],
            values.ContainsKey,
            key => values[key],
            (key, ids) =>
            {
                operations.Add($"save:{key}");
                values[key] = ids.ToArray();
            },
            key =>
            {
                operations.Add($"delete:{key}");
                values.Remove(key);
            }
        );

        Assert.Equal(["skin-a", "skin-b"], selected);
        Assert.Equal(["save:canonical", "delete:legacy"], operations);
        Assert.Equal(["skin-a", "skin-b"], values["canonical"]);
        Assert.False(values.ContainsKey("legacy"));
    }

    [Fact]
    public void Skin_preference_failed_canonical_save_keeps_legacy_recovery_copy()
    {
        var values = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["legacy"] = ["skin-a"],
        };
        var deletes = new List<string>();

        Assert.Throws<InvalidOperationException>(() =>
            RandomHeroSkinPoolPreferenceMigration.LoadCanonicalFirst(
                ["canonical", "legacy"],
                values.ContainsKey,
                key => values[key],
                (_, _) => throw new InvalidOperationException("save failed"),
                deletes.Add
            )
        );

        Assert.Empty(deletes);
        Assert.Equal(["skin-a"], values["legacy"]);
    }

    [Theory]
    [InlineData("Hero8")]
    [InlineData("TheDragons")]
    public void Replay_manifest_aliases_resolve_to_current_runtime_hero(string heroId)
    {
        Assert.True(CombatReplayHeroIdentity.TryParse(heroId, out var hero));
        Assert.True(TheDragonsHeroIdentity.IsTheDragons(hero));
    }

    [Fact]
    public void Replay_manifest_alias_degrades_when_runtime_exposes_neither_name()
    {
        Assert.False(CombatReplayHeroIdentity.TryParse("TheDragons", _ => null, out _));
    }

    [Fact]
    public void Replay_manifest_preserves_existing_case_insensitive_non_alias_parsing()
    {
        Assert.True(CombatReplayHeroIdentity.TryParse("vanessa", out var hero));
        Assert.Equal(EHero.Vanessa, hero);
        Assert.False(CombatReplayHeroIdentity.TryParse("UnknownHero", out _));
    }
}

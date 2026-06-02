#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.EncounterOffers;

namespace BazaarPlusPlus.Game.CollectionPanel.Encounters;

internal static class CollectionSourceRuleOfferPoolResolver
{
    public static EncounterOfferPoolResult Resolve(
        MerchantTrainerEntry source,
        IReadOnlyList<EHero> uiHeroFilters,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (catalogCards == null)
            throw new ArgumentNullException(nameof(catalogCards));
        if (catalogCards.Count == 0)
            return EncounterOfferPoolResult.Loading("collection-catalog-empty");

        var rule = SourceRule.TryCreate(source);
        if (rule == null)
            return EncounterOfferPoolResult.Unavailable("source-rule-unavailable");

        var result = new HashSet<Guid>();
        foreach (var card in catalogCards)
        {
            if (rule.Matches(card, uiHeroFilters))
                result.Add(card.Id);
        }
        return EncounterOfferPoolResult.Ready(result);
    }

    private sealed class SourceRule
    {
        private SourceRule(ECardType type)
        {
            Type = type;
        }

        private ECardType Type { get; }

        private HeroRule HeroRule { get; set; } = HeroRule.ApplyUi;

        private EHero SourceHero { get; set; }

        private bool NeutralOnly { get; set; }

        private ETier? MaxStartingTier { get; set; }

        private bool EnchantableOnly { get; set; }

        private readonly HashSet<ECardSize> _sizes = new();
        private readonly HashSet<ECardTag> _anyTags = new();
        private readonly HashSet<ECardTag> _excludedTags = new();
        private readonly HashSet<EHiddenTag> _anyHiddenTags = new();

        public static SourceRule? TryCreate(MerchantTrainerEntry source)
        {
            var rule = new SourceRule(
                source.Kind == EncounterPortraitKind.Trainer ? ECardType.Skill : ECardType.Item
            );

            var description = Normalize(source.Description);
            switch (description)
            {
                case "sells items":
                case "sells items. always sells discounted items":
                case "sells skills":
                    break;

                case "sells items from this hero":
                    if (!TryResolveHero(source.Name, out var sourceHero))
                        return null;
                    rule.HeroRule = HeroRule.SourceHero;
                    rule.SourceHero = sourceHero;
                    break;

                case "sells crit items from any hero":
                case "sells economic items from any hero":
                case "sells flying items from any hero":
                case "sells tech items from any hero":
                case "sells apparel from any hero":
                case "sells toys from any hero":
                case "sells relics from any hero":
                    rule.HeroRule = HeroRule.AnyHero;
                    ApplyAnyHeroMerchantRule(rule, description);
                    break;

                case "sells bronze-tier neutral items":
                    rule.NeutralOnly = true;
                    rule.MaxStartingTier = ETier.Bronze;
                    break;

                case "sells bronze-tier skills":
                case "gives you 2 gold and teaches bronze-tier skills":
                    rule.MaxStartingTier = ETier.Bronze;
                    break;

                case "sells silver-tier items":
                case "teaches silver-tier skills":
                    rule.MaxStartingTier = ETier.Silver;
                    break;

                case "sells gold-tier items":
                case "teaches gold-tier skills":
                    rule.MaxStartingTier = ETier.Gold;
                    break;

                case "sells diamond-tier items":
                case "teaches diamond-tier skills":
                    rule.MaxStartingTier = ETier.Diamond;
                    break;

                case "sells small items":
                    rule._sizes.Add(ECardSize.Small);
                    break;

                case "sells medium items":
                    rule._sizes.Add(ECardSize.Medium);
                    break;

                case "sells large items":
                    rule._sizes.Add(ECardSize.Large);
                    break;

                case "sells medium and large items. buys your small items at +1 value":
                    rule._sizes.Add(ECardSize.Medium);
                    rule._sizes.Add(ECardSize.Large);
                    break;

                case "sells small and large items. buys your medium items at +2 value":
                    rule._sizes.Add(ECardSize.Small);
                    rule._sizes.Add(ECardSize.Large);
                    break;

                case "sells small and medium items. buys your large items at +3 value":
                    rule._sizes.Add(ECardSize.Small);
                    rule._sizes.Add(ECardSize.Medium);
                    break;

                case "sells non-weapon items":
                    rule._excludedTags.Add(ECardTag.Weapon);
                    break;

                case "sells enchanted items":
                    rule.EnchantableOnly = true;
                    break;

                case "sells weapons":
                case "teaches weapon skills":
                    rule._anyTags.Add(ECardTag.Weapon);
                    break;

                case "sells vehicles or drones":
                    rule._anyTags.Add(ECardTag.Vehicle);
                    rule._anyTags.Add(ECardTag.Drone);
                    break;

                case "sells ammo items":
                case "teaches ammo skills":
                    AddHidden(rule, EHiddenTag.Ammo, EHiddenTag.AmmoReference);
                    break;

                case "sells burn items":
                case "teaches burn skills":
                    AddHidden(rule, EHiddenTag.Burn, EHiddenTag.BurnReference);
                    break;

                case "sells freeze items":
                case "teaches freeze skills":
                    AddHidden(rule, EHiddenTag.Freeze, EHiddenTag.FreezeReference);
                    break;

                case "sells haste items":
                case "teaches haste and charge skills":
                    AddHidden(rule, EHiddenTag.Haste, EHiddenTag.HasteReference);
                    if (description.IndexOf("charge", StringComparison.Ordinal) >= 0)
                        rule._anyHiddenTags.Add(EHiddenTag.Charge);
                    break;

                case "sells slow items":
                case "teaches slow skills":
                    AddHidden(rule, EHiddenTag.Slow, EHiddenTag.SlowReference);
                    break;

                case "sells haste, slow and cooldown items":
                    AddHidden(
                        rule,
                        EHiddenTag.Haste,
                        EHiddenTag.HasteReference,
                        EHiddenTag.Slow,
                        EHiddenTag.SlowReference,
                        EHiddenTag.Cooldown,
                        EHiddenTag.CooldownReference
                    );
                    break;

                case "sells poison items":
                case "teaches poison skills":
                    AddHidden(rule, EHiddenTag.Poison, EHiddenTag.PoisonReference);
                    break;

                case "sells health and shield items":
                    AddHidden(
                        rule,
                        EHiddenTag.Health,
                        EHiddenTag.HealthReference,
                        EHiddenTag.Shield,
                        EHiddenTag.ShieldReference
                    );
                    break;

                case "teaches shield skills":
                    AddHidden(rule, EHiddenTag.Shield, EHiddenTag.ShieldReference);
                    break;

                case "sells max health items":
                    AddHidden(rule, EHiddenTag.Health, EHiddenTag.HealthReference);
                    break;

                case "sells heal and regen items":
                case "teaches health, heal and regen skills":
                    AddHidden(
                        rule,
                        EHiddenTag.Health,
                        EHiddenTag.HealthReference,
                        EHiddenTag.Heal,
                        EHiddenTag.HealReference,
                        EHiddenTag.Regen,
                        EHiddenTag.RegenReference,
                        EHiddenTag.HealthRegen
                    );
                    break;

                case "teaches crit skills":
                    AddHidden(rule, EHiddenTag.Crit, EHiddenTag.CritReference);
                    break;

                case "sells foods":
                    rule._anyTags.Add(ECardTag.Food);
                    break;

                case "sells potions":
                    rule._anyTags.Add(ECardTag.Potion);
                    break;

                case "sells property items":
                    rule._anyTags.Add(ECardTag.Property);
                    break;

                case "sells aquatic items":
                    rule._anyTags.Add(ECardTag.Aquatic);
                    break;

                case "sells friend items":
                    rule._anyTags.Add(ECardTag.Friend);
                    break;

                case "sells tools":
                    rule._anyTags.Add(ECardTag.Tool);
                    break;

                case "sells monster skills":
                    rule._anyTags.Add(ECardTag.Dinosaur);
                    break;

                case "teaches skills unique to dooley":
                case "teaches skills unique to karnok":
                case "teaches skills unique to mak":
                case "teaches skills unique to pygmalien":
                case "teaches skills unique to stelle":
                case "teaches skills unique to vanessa":
                case "teaches jules skills":
                    if (!TryResolveHero(source.Name, out var trainerHero))
                        return null;
                    rule.HeroRule = HeroRule.SourceHero;
                    rule.SourceHero = trainerHero;
                    break;

                default:
                    return null;
            }

            return rule;
        }

        public bool Matches(CollectionCardVm card, IReadOnlyList<EHero> uiHeroFilters)
        {
            if (card.Type != Type)
                return false;
            if (!MatchesHero(card, uiHeroFilters))
                return false;
            if (NeutralOnly && !Contains(card.Heroes, EHero.Common))
                return false;
            if (
                MaxStartingTier.HasValue
                && TierRank(card.StartingTier) > TierRank(MaxStartingTier.Value)
            )
                return false;
            if (_sizes.Count > 0 && !Contains(_sizes, card.Size))
                return false;
            foreach (var excludedTag in _excludedTags)
            {
                if (Contains(card.Tags, excludedTag))
                    return false;
            }
            if (_anyTags.Count > 0 && !Overlaps(card.Tags, _anyTags))
                return false;
            if (_anyHiddenTags.Count > 0 && !Overlaps(card.HiddenTags, _anyHiddenTags))
                return false;
            if (EnchantableOnly && !card.IsEnchantable)
                return false;
            return true;
        }

        public void AddAnyTag(ECardTag tag) => _anyTags.Add(tag);

        public void AddAnyHiddenTag(EHiddenTag tag) => _anyHiddenTags.Add(tag);

        private bool MatchesHero(CollectionCardVm card, IReadOnlyList<EHero> uiHeroFilters)
        {
            if (HeroRule == HeroRule.AnyHero)
                return true;
            if (HeroRule == HeroRule.SourceHero)
                return Contains(card.Heroes, SourceHero);
            if (uiHeroFilters.Count == 0)
                return true;
            return Overlaps(card.Heroes, uiHeroFilters);
        }
    }

    private enum HeroRule
    {
        ApplyUi,
        AnyHero,
        SourceHero,
    }

    private static void ApplyAnyHeroMerchantRule(SourceRule rule, string description)
    {
        switch (description)
        {
            case "sells crit items from any hero":
                AddHidden(rule, EHiddenTag.Crit, EHiddenTag.CritReference);
                break;

            case "sells economic items from any hero":
                AddHidden(
                    rule,
                    EHiddenTag.EconomyReference,
                    EHiddenTag.Gold,
                    EHiddenTag.Income,
                    EHiddenTag.Value,
                    EHiddenTag.BuyPrice,
                    EHiddenTag.SellPrice
                );
                break;

            case "sells flying items from any hero":
                AddHidden(rule, EHiddenTag.Flying, EHiddenTag.FlyingReference);
                break;

            case "sells tech items from any hero":
                rule.AddAnyTag(ECardTag.Tech);
                break;

            case "sells apparel from any hero":
                rule.AddAnyTag(ECardTag.Apparel);
                break;

            case "sells toys from any hero":
                rule.AddAnyTag(ECardTag.Toy);
                break;

            case "sells relics from any hero":
                rule.AddAnyTag(ECardTag.Relic);
                break;
        }
    }

    private static void AddHidden(SourceRule rule, params EHiddenTag[] tags)
    {
        foreach (var tag in tags)
            rule.AddAnyHiddenTag(tag);
    }

    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static bool TryResolveHero(string name, out EHero hero)
    {
        switch (Normalize(name))
        {
            case "dooley":
            case "cymon":
                hero = EHero.Dooley;
                return true;

            case "jules":
            case "nonna":
                hero = EHero.Jules;
                return true;

            case "karnok":
            case "kelsa":
                hero = EHero.Karnok;
                return true;

            case "mak":
            case "zosima":
                hero = EHero.Mak;
                return true;

            case "pygmalien":
            case "mr. tuskari":
                hero = EHero.Pygmalien;
                return true;

            case "stelle":
            case "uncle odi":
                hero = EHero.Stelle;
                return true;

            case "vanessa":
            case "old zane":
                hero = EHero.Vanessa;
                return true;

            default:
                hero = EHero.Common;
                return false;
        }
    }

    private static bool Contains<T>(IReadOnlyCollection<T> values, T target)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var value in values)
        {
            if (comparer.Equals(value, target))
                return true;
        }
        return false;
    }

    private static bool Overlaps<T>(IReadOnlyCollection<T> left, IReadOnlyCollection<T> right)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var leftValue in left)
        {
            foreach (var rightValue in right)
            {
                if (comparer.Equals(leftValue, rightValue))
                    return true;
            }
        }
        return false;
    }

    private static int TierRank(ETier tier) =>
        tier switch
        {
            ETier.Bronze => 0,
            ETier.Silver => 1,
            ETier.Gold => 2,
            ETier.Diamond => 3,
            ETier.Legendary => 4,
            _ => 99,
        };
}

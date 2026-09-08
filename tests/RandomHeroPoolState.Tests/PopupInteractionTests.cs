using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;
using TheBazaar;
using TheBazaar.UI.Menu;

internal static class PopupInteractionTests
{
    internal static void Run()
    {
        ClientCache.OwnedHeroes.Value = [new("Vanessa"), new("Pygmalien"), new("Mak")];
        PlayerPreferences.Data.RandomHeroEnabled = true;
        RandomHeroPoolPlayerPrefs.Selected = ["Vanessa", "Pygmalien"];
        var vanessa = Card(EHero.Vanessa);
        var pygmalien = Card(EHero.Pygmalien);
        var mak = Card(EHero.Mak);
        var random = Card(EHero.Common);
        var locked = Card(EHero.Stelle, owned: false);
        foreach (var card in new[] { vanessa, pygmalien, mak, random, locked })
            RandomHeroPoolNativeController.Project(card);
        Check(
            vanessa._button.Selected && pygmalien._button.Selected && !mak._button.Selected,
            "The popup must show all saved members, not only the randomly chosen hero."
        );
        Check(
            vanessa._button.group == null && pygmalien._button.group == null,
            "Pool cards must be detached from the native single-selection group."
        );

        Check(
            RandomHeroPoolNativeController.TryHandleClick(vanessa),
            "A selected card click must edit the pool."
        );
        Check(
            !vanessa._button.Selected && pygmalien._button.Selected,
            "Removing one member must preserve the others."
        );
        Check(
            RandomHeroPoolNativeController.TryHandleClick(pygmalien),
            "The last member click must be intercepted."
        );
        Check(pygmalien._button.Selected, "The last member must remain visibly selected.");
        // The child toggle may register its own onValueChanged listener after the parent card.
        // Its callback still receives the original false value after the pool guard ran.
        pygmalien._button.SetSelected(false);
        var owner = pygmalien.GetComponent<RandomHeroPoolNativeController>()!;
        typeof(RandomHeroPoolNativeController)
            .GetMethod(
                "LateUpdate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            )
            ?.Invoke(owner, null);
        Check(
            pygmalien._button.Selected,
            "Native listener ordering must not visually remove the last pool member after the guard."
        );
        Check(
            PlayerPreferences.Data.RandomHeroEnabled,
            "Editing the pool must preserve random mode."
        );
        Check(
            !RandomHeroPoolNativeController.TryHandleClick(locked),
            "Locked heroes must retain the native interaction."
        );
        Check(!locked._button.Selected, "Locked heroes must never become pool members.");

        for (var index = 0; index < 20; index++)
        {
            Check(
                RandomHeroPoolNativeController.TrySelectRandomHero(),
                "The pool must provide the next hero."
            );
            Check(
                Data.SelectedHero == EHero.Pygmalien,
                "Opening selection must not escape a singleton pool."
            );
        }
        Check(
            RandomHeroPoolNativeController.TryHandleClick(mak),
            "An unselected owned card must join the pool."
        );
        Check(
            pygmalien._button.Selected && mak._button.Selected,
            "Adding a member must preserve all current members."
        );
        UnityEngine.Random.NextIndex = 1;
        Check(
            RandomHeroPoolNativeController.TrySelectRandomHero() && Data.SelectedHero == EHero.Mak,
            "All selected heroes must be reachable by the random selector."
        );

        Check(
            RandomHeroPoolNativeController.TryHandleClick(random),
            "The random card must let the user exit pool mode."
        );
        Check(
            !PlayerPreferences.Data.RandomHeroEnabled,
            "Exiting pool mode must restore ordinary hero selection."
        );
        Check(
            mak._button.Selected && !pygmalien._button.Selected && mak._button.group != null,
            "Manual mode must restore the native single-selection visuals and group."
        );
        Check(
            !RandomHeroPoolNativeController.TryHandleClick(vanessa),
            "Manual hero clicks must use the native path."
        );
        ClientCache.OwnedHeroes.HasData = false;
        Check(
            !RandomHeroPoolNativeController.TrySelectRandomHero(),
            "Unavailable ownership must fall through to native handling."
        );
    }

    private static HeroOptionController Card(EHero hero, bool owned = true) =>
        new()
        {
            Data = new HeroData { Hero = hero, Owned = owned },
        };

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class SeededRng : IRng
{
    private readonly Random _random;

    public SeededRng(int seed)
    {
        _random = new Random(seed);
    }

    public double NextDouble() => _random.NextDouble();

    public int NextInt(int exclusiveMax) => _random.Next(exclusiveMax);
}

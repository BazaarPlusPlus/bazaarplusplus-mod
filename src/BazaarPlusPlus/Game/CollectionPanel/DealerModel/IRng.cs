#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal interface IRng
{
    double NextDouble();
    int NextInt(int exclusiveMax);
}

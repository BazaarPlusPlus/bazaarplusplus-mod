#pragma warning disable CS0436
namespace BazaarPlusPlus;

internal interface IBoardRenderTarget
{
    void Render(BoardRenderModel renderModel);

    void SetVisible(bool visible);
}

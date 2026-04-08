#pragma warning disable CS0436
using System;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal interface IBoardRenderTarget : IDisposable
{
    void Render(BoardRenderModel renderModel);

    void SetVisible(bool visible);
}

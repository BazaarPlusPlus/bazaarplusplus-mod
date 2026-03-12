#pragma warning disable CS0436
namespace BazaarPlusPlus;

internal interface IPreviewDataSource
{
    bool TryBuild(out PreviewBoardModel model);
}

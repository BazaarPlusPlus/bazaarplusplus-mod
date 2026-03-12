#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal sealed class CompositePreviewDataSource : IPreviewDataSource
{
    private readonly IReadOnlyList<IPreviewDataSource> _sources;

    public CompositePreviewDataSource(params IPreviewDataSource[] sources)
    {
        _sources = sources ?? new IPreviewDataSource[0];
    }

    public bool TryBuild(out PreviewBoardModel model)
    {
        foreach (var source in _sources)
        {
            if (source != null && source.TryBuild(out model) && model != null)
                return true;
        }

        model = null;
        return false;
    }
}

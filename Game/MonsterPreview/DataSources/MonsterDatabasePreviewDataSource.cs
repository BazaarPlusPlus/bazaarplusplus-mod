#pragma warning disable CS0436
using System.Collections.Generic;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PreviewSurface;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterDatabasePreviewDataSource : IPreviewDataSource
{
    private readonly string _encounterId;
    private readonly string _logContext;

    public MonsterDatabasePreviewDataSource(
        string encounterId,
        string logContext = "monster_preview"
    )
    {
        _encounterId = encounterId ?? string.Empty;
        _logContext = string.IsNullOrWhiteSpace(logContext) ? "monster_preview" : logContext;
    }

    public bool TryBuild(out PreviewBoardModel model)
    {
        model = null;
        if (string.IsNullOrWhiteSpace(_encounterId))
        {
            BppLog.Warn(
                "MonsterDatabasePreviewDataSource",
                $"TryBuild aborted context={_logContext} because encounterId is empty"
            );
            return false;
        }

        if (!BppRuntimeHost.MonsterCatalog.TryGetByEncounterId(_encounterId, out var monster))
        {
            BppLog.Warn(
                "MonsterDatabasePreviewDataSource",
                $"TryBuild aborted context={_logContext} because encounterId={_encounterId} was not found in MonsterCatalog"
            );
            return false;
        }

        var sourceModel = MonsterPreviewProjector.BuildModel(monster, "monster_db");
        var filteredItemCards = PreviewCardSpecFilter.FilterLocallyRenderable(
            sourceModel.ItemCards
        );
        var filteredSkillCards = PreviewCardSpecFilter.FilterLocallyRenderable(
            sourceModel.SkillCards
        );
        model = new PreviewBoardModel
        {
            Title = sourceModel.Title ?? string.Empty,
            ItemCards = filteredItemCards,
            SkillCards = filteredSkillCards,
            Metadata = new Dictionary<string, string>(
                sourceModel.Metadata ?? new Dictionary<string, string>()
            ),
        };
        model.Signature = PreviewBoardSignature.Build(model);
        var hasRenderableCards = filteredItemCards.Count > 0 || filteredSkillCards.Count > 0;
        BppLog.Info(
            "MonsterDatabasePreviewDataSource",
            $"TryBuild context={_logContext} encounterId={_encounterId} encounter={monster.EncounterShortId} title={monster.Title ?? "-"} items={filteredItemCards.Count}/{sourceModel.ItemCards?.Count ?? 0} skills={filteredSkillCards.Count}/{sourceModel.SkillCards?.Count ?? 0} signature={model.Signature} success={hasRenderableCards}"
        );
        return hasRenderableCards;
    }
}

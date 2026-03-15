var sourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs"
    )
);
var source = File.ReadAllText(sourcePath);

AssertContains(
    source,
    "private static readonly Color ItemBoardFillColor = new Color(0.34f, 0.29f, 0.24f, 0.12f);",
    "Item board background should use the agreed warm gray fill."
);
AssertContains(
    source,
    "private static readonly Color SkillBoardFillColor = new Color(0.24f, 0.29f, 0.33f, 0.16f);",
    "Skill board background should use the agreed clean gray-blue fill."
);
AssertContains(
    source,
    "private static readonly Color BrandingBoardFillColor = new Color(0.17f, 0.20f, 0.23f, 0.62f);",
    "Branding background should use the agreed slate gray fill."
);

Console.WriteLine("MonsterPreviewBoardColors checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertContains(string source, string expected, string message)
{
    Assert(source.Contains(expected, StringComparison.Ordinal), message);
}

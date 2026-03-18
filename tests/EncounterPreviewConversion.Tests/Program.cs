using System.Collections;
using System.Reflection;
using BazaarPlusPlus;

var converterType = RequireType("BazaarPlusPlus.EncounterPreviewSpecConverter");
var conversionMethod = converterType.GetMethod(
    "BuildCachedSpecs",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    conversionMethod != null,
    "EncounterPreviewSpecConverter should expose BuildCachedSpecs for structured cache conversion."
);

var previewCardType = typeof(RunInfo).GetNestedType("MonsterPreviewCard", BindingFlags.Public);
Assert(
    previewCardType != null,
    "RunInfo.MonsterPreviewCard should exist for structured cached preview data."
);

var cards = CreateList(previewCardType!);
cards.Add(
    CreateCard(previewCardType!, "11111111-1111-1111-1111-111111111111", 2, 3, "Crusher", "None")
);
cards.Add(
    CreateCard(previewCardType!, "22222222-2222-2222-2222-222222222222", 1, 1, "Howl", "None")
);

var specs = (IEnumerable?)conversionMethod!.Invoke(null, [cards])!;
Assert(specs != null, "Structured cached cards should convert into preview specs.");

var converted = specs!.Cast<object>().ToList();
Assert(converted.Count == 2, "Structured cached cards should preserve item count.");
Assert(
    (string)GetProperty(converted[0], "TemplateId")! == "11111111-1111-1111-1111-111111111111",
    "Template id should be preserved."
);
Assert((int)GetProperty(converted[0], "Tier")! == 2, "Tier should be preserved.");
Assert((int)GetProperty(converted[0], "Size")! == 3, "Size should be preserved.");
Assert(
    (string)GetProperty(converted[1], "SourceName")! == "Howl",
    "Source name should be preserved."
);

var previewDataSourceType = RequireType(
    "BazaarPlusPlus.Game.MonsterPreview.MonsterDatabasePreviewDataSource"
);
var buildModelMethod = previewDataSourceType.GetMethod(
    "BuildModel",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    buildModelMethod != null,
    "MonsterDatabasePreviewDataSource should expose BuildModel for monster preview projection."
);

var monsterInfoType = RequireType("BazaarPlusPlus.MonsterInfo");
var monsterBoardCardType = RequireType("BazaarPlusPlus.MonsterBoardCardInfo");
var monster = Activator.CreateInstance(monsterInfoType)!;
SetProperty(monster, "Title", "Poison Peddler");

var boardCards = CreateList(monsterBoardCardType);
var boardCard = Activator.CreateInstance(monsterBoardCardType)!;
SetProperty(boardCard, "CardId", Guid.Parse("33333333-3333-3333-3333-333333333333"));
SetProperty(boardCard, "Tier", "Gold");
SetProperty(boardCard, "Size", "Small");
SetProperty(boardCard, "Type", "Item");
SetProperty(boardCard, "Enchant", "Toxic");
boardCards.Add(boardCard);
SetProperty(monster, "BoardCards", boardCards);

var previewModel = buildModelMethod!.Invoke(null, [monster, "monster_db"])!;
var itemCards = (IEnumerable?)GetProperty(previewModel, "ItemCards");
Assert(itemCards != null, "Monster preview model should expose item cards.");

var projectedCards = itemCards!.Cast<object>().ToList();
Assert(projectedCards.Count == 1, "Monster preview projection should preserve board cards.");
Assert(
    (string)GetProperty(projectedCards[0], "Enchant")! == "Toxic",
    "Monster preview projection should preserve board enchant overrides."
);

Console.WriteLine("EncounterPreviewConversion checks passed.");

static IList CreateList(Type itemType)
{
    var listType = typeof(List<>).MakeGenericType(itemType);
    return (IList)Activator.CreateInstance(listType)!;
}

static object CreateCard(
    Type cardType,
    string templateId,
    int tier,
    int size,
    string sourceName,
    string enchant
)
{
    var card = Activator.CreateInstance(cardType)!;
    SetProperty(card, "TemplateId", templateId);
    SetProperty(card, "Tier", tier);
    SetProperty(card, "Size", size);
    SetProperty(card, "SourceName", sourceName);
    SetProperty(card, "Enchant", enchant);
    return card;
}

static object? GetProperty(object target, string name)
{
    return target
        .GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?.GetValue(target);
}

static void SetProperty(object target, string name, object value)
{
    var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    Assert(property != null, $"Property not found: {name}");
    property!.SetValue(target, value);
}

static Type RequireType(string fullName)
{
    var assembly = Assembly.Load("BazaarPlusPlus");
    return assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

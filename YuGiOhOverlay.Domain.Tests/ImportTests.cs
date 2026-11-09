using System.Text.Json;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.Infrastructure;

namespace YuGiOhOverlay.Domain.Tests;

public sealed class ImportTests
{
    private const string Deck = "61ebdf8e0a39c54503c45337";
    private const string CardA = "aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CardB = "bbbbbbbbbbbbbbbbbbbbbbbb";
    private static JsonElement Entry(string id, string connector = "") => JsonSerializer.SerializeToElement(new
    {
        card = new { _id = id, name = "Synthetic " + id[0] },
        connector
    });
    private static JsonElement Row(JsonElement[] starters, object? steps = null) => JsonSerializer.SerializeToElement(new
    {
        _id = "cccccccccccccccccccccccc",
        title = "Synthetic fixture",
        deckType = new { _id = Deck, name = "Demo" },
        startingCards = starters,
        steps = steps ?? new[] { new { entries = new[] { Entry(CardA) }, condition = new { branches = Array.Empty<object>() } } },
        requiredCards = new[] { Entry(CardB) },
        upvotes = new[] { "voter1", "voter2" },
        downvotes = new[] { "voter3" }
    });
    private static ComboCatalog Parse(JsonElement row) => new MasterDuelMetaParser().Parse([row], Deck, RecommendationTests.Now);

    [Fact]
    public void ObservedApiShapeConvertsWithoutRequiringDeckCardsInHand()
    {
        var catalog = Parse(Row([Entry(CardA)]));
        var rule = Assert.Single(catalog.Rules);
        Assert.Equal(1, rule.Priority);
        Assert.Equal(CardA, Assert.Single(Assert.Single(rule.Conditions.Alternatives)).CardId);
        Assert.Contains("Synthetic b", Assert.Single(rule.RequiredCards));
        Assert.Contains("Synthetic a", Assert.Single(rule.Actions));
    }
    [Fact]
    public void RepeatedStartersRetainQuantity()
    {
        var rule = Assert.Single(Parse(Row([Entry(CardA, "plus"), Entry(CardA)])).Rules);
        Assert.Equal(2, Assert.Single(Assert.Single(rule.Conditions.Alternatives)).Count);
    }
    [Fact]
    public void PureOrRetainsAlternatives() =>
        Assert.Equal(2, Assert.Single(Parse(Row([Entry(CardA, "or"), Entry(CardB)])).Rules).Conditions.Alternatives.Count);
    [Fact]
    public void GenericStarterDoesNotBecomeOneCardCombo()
    {
        var catalog = Parse(Row([Entry(CardA, "plus"), JsonSerializer.SerializeToElement(new { generic = "Any card" })]));
        Assert.Empty(catalog.Rules); Assert.Single(catalog.ImportWarnings);
    }
    [Fact]
    public void MixedAndOrIsNotFlattened()
    {
        var catalog = Parse(Row([Entry(CardA, "or"), Entry(CardB, "plus"), Entry(CardA)]));
        Assert.Empty(catalog.Rules); Assert.Single(catalog.ImportWarnings);
    }
    [Fact]
    public void BranchesRemainConditionalInGuide()
    {
        var steps = new[]
        {
            new
            {
                entries = new[] { Entry(CardA) },
                condition = new
                {
                    label = "Choose",
                    branches = new[]
                    {
                        new { label = "condition fulfilled", steps = new[] { new { entries = new[] { Entry(CardB) } } } }
                    }
                }
            }
        };
        var action = Assert.Single(Assert.Single(Parse(Row([Entry(CardA)], steps)).Rules).Actions);
        Assert.Contains("Si condition fulfilled", action); Assert.Contains("Synthetic b", action);
    }
    [Theory]
    [InlineData("http://www.masterduelmeta.com/combos-and-counters?combosDeckType=61ebdf8e0a39c54503c45337")]
    [InlineData("https://evil.example/combos-and-counters?combosDeckType=61ebdf8e0a39c54503c45337")]
    [InlineData("https://www.masterduelmeta.com/combos-and-counters?combosDeckType=../../file")]
    [InlineData("https://www.masterduelmeta.com/combos-and-counters")]
    public void InvalidSourceRejected(string url) => Assert.Throws<ArgumentException>(() => MasterDuelMetaClient.ParseDeckId(url));
    [Fact]
    public void UserLinkAccepted() => Assert.Equal(Deck, MasterDuelMetaClient.ParseDeckId(
        "https://www.masterduelmeta.com/combos-and-counters?combosDeckType=" + Deck + "#combos"));
    [Fact]
    public void LegacyVersionDoesNotSilentlyProduceRecommendations() => Assert.Throws<InvalidDataException>(() =>
        CatalogValidator.Validate(RecommendationTests.Catalog(RecommendationTests.Rule()) with { Version = 1 }));
    [Fact]
    public void DuplicateRuleIdRejected() => Assert.Throws<InvalidDataException>(() =>
        CatalogValidator.Validate(RecommendationTests.Catalog(RecommendationTests.Rule(), RecommendationTests.Rule())));
    [Fact]
    public void DuplicateRequirementRejected() => Assert.Throws<InvalidDataException>(() =>
        CatalogValidator.Validate(RecommendationTests.Catalog(RecommendationTests.Rule("x", 0, new("a", 1), new("a", 1)))));
    [Fact]
    public void UnknownCardIdRejected() => Assert.Throws<InvalidDataException>(() =>
        CatalogValidator.Validate(RecommendationTests.Catalog(RecommendationTests.Rule("x", 0, new CardRequirement("unknown", 1)))));
    [Fact]
    public async Task RoundTripAndInvalidJsonPreservePreviousCatalog()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "catalog.json");
        try
        {
            var repository = new JsonComboRepository(path);
            var catalog = RecommendationTests.Catalog(RecommendationTests.Rule());
            await repository.SaveAsync(catalog, default);
            Assert.Single((await repository.LoadAsync(default))!.Rules);
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(catalog with { Version = 99 }, default));
            Assert.Equal(3, (await repository.LoadAsync(default))!.Version);
            await File.WriteAllTextAsync(path, "{ invalid }");
            await Assert.ThrowsAsync<JsonException>(() => repository.LoadAsync(default));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Theory]
    [InlineData("search", "Add from deck", ObservationArea.Hand)]
    [InlineData("summon", "Normal summon", ObservationArea.AlliedField)]
    public void TerminalOutcomeKeepsStructuredActionAndVisualCue(string symbol, string label, ObservationArea area)
    {
        var entry = JsonSerializer.SerializeToElement(new { card = new { _id = CardA, name = "Synthetic a" }, symbol, label });
        var rule = Assert.Single(Parse(Row([Entry(CardA)], new[] { new { entries = new[] { entry } } })).Rules);
        var step = Assert.Single(rule.GuideSteps!);
        Assert.Equal(area, step.Cue!.Area);
        Assert.Equal(CardA, Assert.Single(step.Cue.Cards).CardId);
        Assert.Equal(label, Assert.Single(step.Instructions).Detail);
    }
    [Theory]
    [InlineData("effect", "Activate")]
    [InlineData("summon", "Activate on summon")]
    [InlineData("search", "If available")]
    public void AmbiguousOutcomeDoesNotGetAutoCue(string symbol, string label)
    {
        var entry = JsonSerializer.SerializeToElement(new { card = new { _id = CardA, name = "Synthetic a" }, symbol, label });
        var rule = Assert.Single(Parse(Row([Entry(CardA)], new[] { new { entries = new[] { entry } } })).Rules);
        Assert.Null(Assert.Single(rule.GuideSteps!).Cue);
    }
    [Fact]
    public void V2MigrationPreservesTextAndDoesNotInventVisualChecks()
    {
        var old = RecommendationTests.Catalog(RecommendationTests.Rule());
        var current = CatalogMigration.Upgrade(old);
        Assert.Equal(3, current.Version); Assert.Equal(2, old.Version);
        Assert.Equal(old.Rules[0].Actions[0], current.Rules[0].GuideSteps![0].Instructions[0].Detail);
        Assert.Null(current.Rules[0].GuideSteps![0].Cue);
        CatalogValidator.Validate(current);
    }
    [Fact]
    public void UnknownVisualCardIsRejected()
    {
        var upgraded = CatalogMigration.Upgrade(RecommendationTests.Catalog(RecommendationTests.Rule()));
        var step = upgraded.Rules[0].GuideSteps![0] with { Cue = new(ObservationArea.Hand, [new("unknown", 1)]) };
        var rule = upgraded.Rules[0] with { GuideSteps = [step] };
        Assert.Throws<InvalidDataException>(() => CatalogValidator.Validate(upgraded with { Rules = [rule] }));
    }
    [Fact]
    public void NullCollectionsRejected() => Assert.Throws<InvalidDataException>(() =>
        CatalogValidator.Validate(RecommendationTests.Catalog() with { Cards = null! }));
    [Fact]
    public void AlternateArtworkIdsShareIdentityAndKeepCopyCount()
    {
        var alternative = JsonSerializer.SerializeToElement(new { card = new { _id = "dddddddddddddddddddddddd", name = "Synthetic a" } });
        var catalog = Parse(Row([Entry(CardA, "plus"), alternative]));
        var required = Assert.Single(Assert.Single(Assert.Single(catalog.Rules).Conditions.Alternatives));
        Assert.Equal(CardA, required.CardId);
        Assert.Equal(2, required.Count);
    }

}

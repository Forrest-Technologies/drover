namespace Drover.Client.Tests.Cards;

/// <summary>
/// The card contracts are one shape the server checks, the client reads and the tests share. Every kind writes the
/// names of section 7.1, and a kind this app does not know still reads as a card with words to show.
/// </summary>
public sealed class CardSpecTests
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Public Methods

    [Fact]
    public void Every_kind_round_trips_through_json()
    {
        var replies = new RepliesSpec(["Summary", "Numbers"]) { Title = "T", Fallback = "F" };
        var choices = new ChoicesSpec(["One", "Two"], false) { Title = "T", Fallback = "F" };
        var form = new FormSpec([new CardField("when", "Date", "date", true, null, null, null)])
        {
            Title = "T",
            Fallback = "F",
        };
        var checklist = new ChecklistSpec([new CardItem("Copy the files", CardKinds.ItemDone)])
        {
            Title = "T",
            Fallback = "F",
        };
        var table = new TableSpec(["Name", "Count"], [["One", "1"]]) { Title = "T", Fallback = "F" };
        var board = new BoardSpec(
            [new CardFigure("37", "Returns", null)],
            [new CardItem("Payments", CardKinds.BoardOk)],
            [new CardRow("Top product", "Blue mug", "https://shop.example")])
        {
            Title = "T",
            Fallback = "F",
        };
        (CardSpec Card, string Kind)[] cards =
        [
            (replies, CardKinds.Replies),
            (choices, CardKinds.Choices),
            (form, CardKinds.Form),
            (checklist, CardKinds.Checklist),
            (table, CardKinds.Table),
            (board, CardKinds.Board),
        ];

        foreach (var (card, kind) in cards)
        {
            var written = JsonSerializer.Serialize(card, json);
            written.Should().Contain($"\"kind\":\"{kind}\"");

            var read = JsonSerializer.Deserialize<CardSpec>(written, json);
            read.Should().NotBeNull();
            read!.GetType().Should().Be(card.GetType());
            JsonSerializer.Serialize(read, json).Should().Be(written);
        }
    }

    [Fact]
    public void An_unknown_kind_reads_as_a_card_with_its_title_and_fallback()
    {
        var card = JsonSerializer.Deserialize<CardSpec>(
            """{"kind":"later","title":"Which report?","fallback":"Which report?\n1. Returns\n2. Something else"}""",
            json);

        card.Should().NotBeNull();
        card.Should().BeOfType<CardSpec>();
        card.Title.Should().Be("Which report?");
        card.Fallback.Should().Be("Which report?\n1. Returns\n2. Something else");
    }

    #endregion
}

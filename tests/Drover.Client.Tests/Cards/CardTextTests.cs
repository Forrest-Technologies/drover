namespace Drover.Client.Tests.Cards;

/// <summary>
/// The fallback and the answer's words come from the stored card. Both stay whole for any number of options.
/// </summary>
public sealed class CardTextTests
{
    #region Public Methods

    [Fact]
    public void The_fallback_lists_every_option_and_something_else_last()
    {
        var card = new ChoicesSpec(["Sales by region", "Returns", "Stock levels"], false)
        {
            Title = "Which report should I start with?",
            Fallback = "written by the server",
        };

        CardText.Plain(card).Should().Be(
            "Which report should I start with?\n1. Sales by region\n2. Returns\n3. Stock levels\n4. Something else");

        var withText = card with { Text = "Pick one" };
        CardText.Plain(withText).Should().Be(
            "Which report should I start with?\nPick one\n1. Sales by region\n2. Returns\n3. Stock levels\n4. Something else");
    }

    [Fact]
    public void Words_join_chosen_labels_then_typed_text_in_order()
    {
        var callId = Guid.CreateVersion7();
        var replies = new RepliesSpec(["The summary", "The full numbers"]) { Title = "T", Fallback = "F" };
        var choices = new ChoicesSpec(["Sales by region", "Returns", "Stock levels"], true) { Title = "T", Fallback = "F" };

        CardText.Words(replies, new CardAnswer(callId, Choices: [1])).Should().Be("The full numbers");
        CardText.Words(choices, new CardAnswer(callId, Choices: [1])).Should().Be("Returns");
        CardText.Words(choices, new CardAnswer(callId, Choices: [2, 0])).Should().Be("Stock levels, Sales by region");
        CardText.Words(choices, new CardAnswer(callId, Choices: [2, 0], Text: "Everything"))
            .Should().Be("Stock levels, Sales by region, Everything");
    }

    [Fact]
    public void A_form_answer_is_one_line_per_field()
    {
        var card = new FormSpec(
        [
            new CardField("when", "Date", "date", true, null, null, null),
            new CardField("people", "People", "number", true, null, null, null),
        ])
        {
            Title = "Book the meeting room",
            Fallback = "written by the server",
        };
        Dictionary<string, string> values = [];
        values["people"] = "6";
        values["when"] = "2026-12-03";

        CardText.Words(card, new CardAnswer(Guid.CreateVersion7(), Values: values))
            .Should().Be("Date: 2026-12-03\nPeople: 6");
    }

    [Fact]
    public void The_fallback_has_no_ceiling_on_the_number_of_options()
    {
        var choices = Enumerable.Range(0, 100).Select(index => $"Option {index + 1}").ToList();
        var card = new ChoicesSpec(choices, false) { Title = "T", Fallback = "F" };

        var plain = CardText.Plain(card);
        plain.Split('\n').Should().HaveCount(102);
        plain.Should().EndWith("100. Option 100\n101. Something else");
    }

    #endregion
}

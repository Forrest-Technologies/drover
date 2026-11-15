namespace Drover.Ui.Starters;

/// <summary>
/// One bot on the Crew screen. <see cref="Role"/> is the first line of what the bot is for.
/// <see cref="StateWords"/> says what it is doing when it is not at rest ("Thinking", "Using a tool", "Paused").
/// <see cref="Receiving"/> is true while work is passing down to it or its answer is passing back.
/// <see cref="Cap"/> is how many bots it has beneath it out of how many it may have, when a cap exists.
/// </summary>
public sealed record CrewBot(
    Guid Id,
    string Name,
    string Role,
    IReadOnlyList<CrewBot> Children,
    AvatarState State = AvatarState.Rest,
    string? StateWords = null,
    Attention Attention = Attention.None,
    bool Receiving = false,
    string? MadeBy = null,
    (int Has, int Most)? Cap = null);

/// <summary>
/// A bot placed on the canvas. Columns are counted in half slots, so a parent can sit centered over an even
/// number of children: a bot spans two columns starting at <see cref="Column"/>. <see cref="Level"/>,
/// <see cref="Position"/>, and <see cref="Siblings"/> are what a tree view tells assistive technology.
/// </summary>
public sealed record TreePlace(CrewBot Bot, Guid? ParentId, int Column, int Depth, int Level, int Position, int Siblings);

/// <summary>A connector from a bot to one beneath it, between the centers of their cells, in half slots and rows.</summary>
public sealed record TreeEdge(Guid ParentId, Guid ChildId, double FromX, double FromY, double ToX, double ToY, bool Pulsing)
{
    /// <summary>
    /// The connector as a path: down from the parent's center to the line between the rows, across, and down to
    /// the child's center. The cells cover both ends, so only the part between the nodes shows.
    /// </summary>
    public string Path
    {
        get
        {
            var middle = (FromY + ToY) / 2;
            return string.Create(CultureInfo.InvariantCulture, $"M{FromX} {FromY}V{middle}H{ToX}V{ToY}");
        }
    }
}

/// <summary>The crew laid out as a top-down tree.</summary>
public sealed record TreePlan(IReadOnlyList<TreePlace> Places, IReadOnlyList<TreeEdge> Edges, int Columns, int Rows)
{
    public static TreePlan Empty { get; } = new([], [], 0, 0);
}

/// <summary>
/// Lays the bot hierarchy out as a tidy tree: every bot with nothing beneath it gets the next slot from the left,
/// and every other bot sits centered over the bots beneath it. Also where the arrow keys go.
/// </summary>
public static class TreeLayout
{
    #region Public Methods

    public static TreePlan Plan(IReadOnlyList<CrewBot> crew)
    {
        List<TreePlace> places = [];
        List<TreeEdge> edges = [];
        var nextSlot = 0;
        var deepest = -1;

        for (var index = 0; index < crew.Count; index++)
        {
            Place(crew[index], null, 0, index + 1, crew.Count);
        }

        // Depth first: each bot followed by the bots beneath it. Assistive technology works out who belongs to
        // whom from this order and each item's level, so it is the order the tree is written in the page.
        return new([.. DepthFirst(places)], edges, nextSlot * 2, deepest + 1);

        // Returns the column the bot starts at, in half slots.
        int Place(CrewBot bot, Guid? parentId, int depth, int position, int siblings)
        {
            deepest = Math.Max(deepest, depth);
            int column;
            if (bot.Children.Count is 0)
            {
                column = nextSlot++ * 2;
            }
            else
            {
                int[] columns = [.. bot.Children.Select((child, index) => Place(child, bot.Id, depth + 1, index + 1, bot.Children.Count))];
                column = (columns[0] + columns[^1]) / 2;
                foreach (var (child, childColumn) in bot.Children.Zip(columns))
                {
                    edges.Add(new(bot.Id, child.Id, column + 1, depth + 0.5, childColumn + 1, depth + 1.5, child.Receiving));
                }
            }

            places.Add(new(bot, parentId, column, depth, depth + 1, position, siblings));
            return column;
        }
    }

    /// <summary>
    /// Where a key moves the selection, as a tree view does: down and up to the next and previous bot in reading
    /// order (each bot, then the bots beneath it), right to the first bot beneath, left to the bot above, Home and
    /// End to the first and last. The tree is always fully open. Any other key, or a move with nowhere to go,
    /// stays. A bot that is not in the plan moves from the first.
    /// </summary>
    public static Guid? Move(TreePlan plan, Guid? current, string key)
    {
        if (plan.Places.Count is 0)
        {
            return null;
        }

        var index = Math.Max(plan.Places.ToList().FindIndex(place => place.Bot.Id == current), 0);
        var here = plan.Places[index];
        var next = key switch
        {
            "ArrowDown" => plan.Places[Math.Min(index + 1, plan.Places.Count - 1)],
            "ArrowUp" => plan.Places[Math.Max(index - 1, 0)],
            "ArrowRight" => plan.Places.FirstOrDefault(place => place.ParentId == here.Bot.Id),
            "ArrowLeft" => plan.Places.FirstOrDefault(place => place.Bot.Id == here.ParentId),
            "Home" => plan.Places[0],
            "End" => plan.Places[^1],
            _ => here,
        };
        return (next ?? here).Bot.Id;
    }

    /// <summary>True for a bot with bots beneath it: a tree view says it is expanded.</summary>
    public static bool HasBeneath(TreePlan plan, TreePlace place) => plan.Places.Any(other => other.ParentId == place.Bot.Id);

    /// <summary>How full a bot's cap is, as the quiet counter on its node: "3 of 5".</summary>
    public static string Cap((int Has, int Most) cap) => $"{cap.Has} of {cap.Most}";

    /// <summary>A node as assistive technology reads it: name, what it is doing, what it has for you, its cap.</summary>
    public static string Announce(CrewBot bot)
    {
        List<string> parts = [bot.Name];
        if (bot.StateWords is { Length: > 0 } state)
        {
            parts.Add(state.ToLowerInvariant());
        }

        if (AttentionWords.Word(bot.Attention) is { } word)
        {
            parts.Add(word.ToLowerInvariant());
        }

        if (bot.Cap is { } cap)
        {
            parts.Add($"{Cap(cap)} bots beneath it");
        }

        return string.Join(", ", parts);
    }

    #endregion

    #region Private Methods

    private static IEnumerable<TreePlace> DepthFirst(IReadOnlyCollection<TreePlace> places)
    {
        var beneath = places.ToLookup(place => place.ParentId);
        return Walk(null);

        IEnumerable<TreePlace> Walk(Guid? parent) =>
            beneath[parent].OrderBy(place => place.Column).SelectMany(place => Walk(place.Bot.Id).Prepend(place));
    }

    #endregion
}

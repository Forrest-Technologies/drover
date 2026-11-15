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

        // Top row first, left to right, so the order in the page is the order the tree is read in.
        return new([.. places.OrderBy(place => place.Depth).ThenBy(place => place.Column)], edges, nextSlot * 2, deepest + 1);

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
    /// Where a key moves the selection on the canvas, by position: up to the bot above, down to the first bot
    /// beneath, left and right to the nearest bot on the same row. Home goes to the first bot at the top, End to
    /// the last bot on the lowest row. Any other key, or a move with nowhere to go, stays.
    /// </summary>
    public static Guid? Move(TreePlan plan, Guid? current, string key)
    {
        if (plan.Places.Count is 0)
        {
            return null;
        }

        var here = plan.Places.FirstOrDefault(place => place.Bot.Id == current) ?? plan.Places.OrderBy(place => place.Depth).ThenBy(place => place.Column).First();
        var row = plan.Places.Where(place => place.Depth == here.Depth).OrderBy(place => place.Column).ToList();
        var next = key switch
        {
            "ArrowUp" => plan.Places.FirstOrDefault(place => place.Bot.Id == here.ParentId),
            "ArrowDown" => plan.Places.Where(place => place.ParentId == here.Bot.Id).OrderBy(place => place.Column).FirstOrDefault(),
            "ArrowLeft" => row.LastOrDefault(place => place.Column < here.Column),
            "ArrowRight" => row.FirstOrDefault(place => place.Column > here.Column),
            "Home" => plan.Places.OrderBy(place => place.Depth).ThenBy(place => place.Column).First(),
            "End" => plan.Places.OrderByDescending(place => place.Depth).ThenByDescending(place => place.Column).First(),
            _ => here,
        };
        return (next ?? here).Bot.Id;
    }

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
}

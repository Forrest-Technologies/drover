namespace Drover.Ui.Theme;

/// <summary>
/// Reads a theme file in the closed format: every key required, no other key, the types and bounds exact, colors
/// only as <c>#RRGGBB</c>. These checks are small and local on purpose. The shared checker of issue 778 replaces
/// them with the one both the device and the server run, and the accessibility gates of issue 423 add the contrast
/// rules this reader does not know. A file that passes here is well formed, not yet known to be accessible.
/// </summary>
public static partial class ThemeFileReader
{
    #region Private Fields

    private static readonly string[] colorTokens =
    [
        "bg-0", "bg-1", "bg-2", "bg-3", "line", "text", "text-2", "text-3", "ember", "ion", "violet", "ok", "warn", "danger",
        "on-accent", .. Enumerable.Range(0, 12).Select(step => $"hue-{step}"), .. Enumerable.Range(0, 12).Select(step => $"head-{step}"),
        "on-head", "visor", "visor-lit", "visor-failed",
    ];

    private static readonly JsonDocumentOptions strict = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    #endregion

    #region Public Methods

    /// <summary>Reads a theme file. <paramref name="expectedId"/> is the file's name without <c>.json</c>, when it has one.</summary>
    /// <exception cref="ThemeFormatException">The text is not JSON, or does not follow the format.</exception>
    public static ThemeFile Read(string json, string? expectedId = null)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, strict);
        }
        catch (JsonException exception)
        {
            throw new ThemeFormatException([$"The text is not JSON: {exception.Message}"]);
        }

        using (document)
        {
            var reading = new ThemeReading();
            var file = reading.ReadFile(document.RootElement, expectedId);

            return reading.Problems.Count > 0 || file is null ? throw new ThemeFormatException(reading.Problems) : file;
        }
    }

    #endregion

    #region Reading

    private sealed class ThemeReading
    {
        #region Properties

        public List<string> Problems { get; } = [];

        #endregion

        #region Public Methods

        public ThemeFile? ReadFile(JsonElement root, string? expectedId)
        {
            var top = Keys(root, "the file", "format", "id", "name", "intent", "firstMode", "suits", "type", "density", "shape", "surfaces", "motion", "characters", "dark", "light");
            if (top is null)
            {
                return null;
            }

            Whole(top, "format", "format", ThemeFile.Format, ThemeFile.Format);
            var id = Text(top, "id", "id", 1, 40, IdPattern());
            if (expectedId is not null && id != expectedId)
            {
                Problems.Add($"id is '{id}', and the file is named '{expectedId}'");
            }

            var name = Text(top, "name", "name", 1, 40);
            var intent = Text(top, "intent", "intent", 1, 120);
            var firstMode = Choice(top, "firstMode", "firstMode", new Dictionary<string, ColorMode> { ["dark"] = ColorMode.Dark, ["light"] = ColorMode.Light });

            var before = Problems.Count;
            var motion = ReadMotion(top);
            var characters = ReadCharacters(top);

            // A bloom that could not be read is already a problem, and reads as 0: it is not also a missing visor edge.
            if (Problems.Count == before && motion.Bloom == 0 && characters.Visor is not VisorStyle.Outlined)
            {
                Problems.Add("motion.bloom is 0, and the visor must then be 'outlined' so it reads without bloom");
            }

            return new(
                id, name, intent, firstMode, Suits(top), ReadType(top), ReadDensity(top), ReadShape(top), ReadSurfaces(top),
                motion, characters, ReadMode(top, "dark"), ReadMode(top, "light"));
        }

        #endregion

        #region Groups

        private List<DeviceRequest> Suits(Dictionary<string, JsonElement> parent)
        {
            var names = new Dictionary<string, DeviceRequest> { ["large-text"] = DeviceRequest.LargeText, ["reduced-motion"] = DeviceRequest.ReducedMotion };
            List<DeviceRequest> found = [];
            if (parent["suits"].ValueKind is not JsonValueKind.Array)
            {
                Problems.Add("suits is not a list");
                return found;
            }

            foreach (var item in parent["suits"].EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.String && names.TryGetValue(item.GetString()!, out var request) && !found.Contains(request))
                {
                    found.Add(request);
                }
                else
                {
                    Problems.Add("suits holds a name that is not 'large-text' or 'reduced-motion', or holds one twice");
                }
            }

            return found;
        }

        private ThemeType ReadType(Dictionary<string, JsonElement> top)
        {
            var type = Keys(top["type"], "type", "interface", "machine", "reading", "headings", "machineNumerals", "scale", "weights", "leading", "tracking", "headingTracking", "measure");
            if (type is null)
            {
                return Empty.Type;
            }

            var face = Text(type, "interface", "type.interface", 1, 40, IdPattern());
            var machine = Text(type, "machine", "type.machine", 1, 40, IdPattern());
            var reading = type["reading"].ValueKind is JsonValueKind.Null ? null : Text(type, "reading", "type.reading", 1, 40, IdPattern());
            CheckFace(face, "type.interface");
            CheckFace(machine, "type.machine");
            CheckFace(reading, "type.reading");
            if (machine == face || machine == reading)
            {
                Problems.Add("type.machine is the same face as type.interface or type.reading");
            }

            var weights = Keys(type["weights"], "type.weights", "body", "label", "heading");
            var leading = Keys(type["leading"], "type.leading", "text", "heading");
            var (body, label, heading) = weights is null ? (400, 500, 600) : (Weight(weights, "body"), Weight(weights, "label"), Weight(weights, "heading"));
            if (!(body < label && label <= heading))
            {
                Problems.Add("type.weights must have body below label and label at most heading");
            }

            return new(
                face,
                machine,
                reading,
                Choice(type, "headings", "type.headings", new Dictionary<string, HeadingFace> { ["interface"] = HeadingFace.Interface, ["machine"] = HeadingFace.Machine }),
                Flag(type, "machineNumerals", "type.machineNumerals"),
                Scale(type["scale"]),
                body,
                label,
                heading,
                leading is null ? 1.5 : Number(leading, "text", "type.leading.text", 1.3, 1.7),
                leading is null ? 1.25 : Number(leading, "heading", "type.leading.heading", 1.1, 1.4),
                Number(type, "tracking", "type.tracking", -0.02, 0.04),
                Number(type, "headingTracking", "type.headingTracking", -0.02, 0.04),
                Whole(type, "measure", "type.measure", 55, 80));
        }

        private ThemeDensity ReadDensity(Dictionary<string, JsonElement> top)
        {
            var density = Keys(top["density"], "density", "spacing", "control", "row", "composerLines");
            if (density is null)
            {
                return Empty.Density;
            }

            var control = Keys(density["control"], "density.control", "pointer", "touch");
            var row = Keys(density["row"], "density.row", "pointer", "touch");

            return new(
                Number(density, "spacing", "density.spacing", 0.85, 1.25),
                control is null ? new(32, 44) : new(Whole(control, "pointer", "density.control.pointer", 32, 48), Whole(control, "touch", "density.control.touch", 44, 56)),
                row is null ? new(44, 44) : new(Whole(row, "pointer", "density.row.pointer", 32, 56), Whole(row, "touch", "density.row.touch", 44, 64)),
                Whole(density, "composerLines", "density.composerLines", 4, 12));
        }

        private ThemeShape ReadShape(Dictionary<string, JsonElement> top)
        {
            var shape = Keys(top["shape"], "shape", "corners", "cut", "registration", "radius", "line", "iconStroke");
            if (shape is null)
            {
                return Empty.Shape;
            }

            var corners = Choice(shape, "corners", "shape.corners", new Dictionary<string, CornerLanguage> { ["faceted"] = CornerLanguage.Faceted, ["square"] = CornerLanguage.Square, ["ticked"] = CornerLanguage.Ticked });
            var cut = Keys(shape["cut"], "shape.cut", "control", "frame");
            var radius = Keys(shape["radius"], "shape.radius", "control", "card", "sheet");
            var registration = Flag(shape, "registration", "shape.registration");
            if (registration && corners is not CornerLanguage.Ticked)
            {
                Problems.Add("shape.registration is true, and only ticked corners have registration marks");
            }

            var cuts = cut is null ? new SizePair(0, 0) : corners is CornerLanguage.Square
                ? new(Whole(cut, "control", "shape.cut.control", 0, 0), Whole(cut, "frame", "shape.cut.frame", 0, 0))
                : new(Whole(cut, "control", "shape.cut.control", 4, 10), Whole(cut, "frame", "shape.cut.frame", 8, 16));
            var (control, card, sheet) = radius is null ? (0, 0, 0) : corners switch
            {
                CornerLanguage.Square => (Whole(radius, "control", "shape.radius.control", 2, 8), Whole(radius, "card", "shape.radius.card", 2, 12), Whole(radius, "sheet", "shape.radius.sheet", 4, 16)),
                CornerLanguage.Ticked => (Whole(radius, "control", "shape.radius.control", 0, 0), Whole(radius, "card", "shape.radius.card", 0, 0), Whole(radius, "sheet", "shape.radius.sheet", 0, 0)),
                _ => (Whole(radius, "control", "shape.radius.control", 0, 16), Whole(radius, "card", "shape.radius.card", 0, 16), Whole(radius, "sheet", "shape.radius.sheet", 0, 16)),
            };

            return new(corners, cuts, registration, control, card, sheet, Whole(shape, "line", "shape.line", 1, 2), Number(shape, "iconStroke", "shape.iconStroke", 1.5, 2));
        }

        private ThemeSurfaces ReadSurfaces(Dictionary<string, JsonElement> top)
        {
            var surfaces = Keys(top["surfaces"], "surfaces", "levels", "yard");
            var yard = surfaces is null ? null : Keys(surfaces["yard"], "surfaces.yard", "texture", "strength", "step");
            if (surfaces is null || yard is null)
            {
                return Empty.Surfaces;
            }

            var levels = Whole(surfaces, "levels", "surfaces.levels", 2, 3);
            var texture = Choice(yard, "texture", "surfaces.yard.texture", new Dictionary<string, YardTexture> { ["dots"] = YardTexture.Dots, ["lines"] = YardTexture.Lines, ["grid"] = YardTexture.Grid, ["none"] = YardTexture.None });
            var strength = Number(yard, "strength", "surfaces.yard.strength", 0, 0.7);
            if (texture is YardTexture.None && strength != 0)
            {
                Problems.Add("surfaces.yard.strength must be 0 when the texture is 'none'");
            }

            return new(levels, texture, strength, Whole(yard, "step", "surfaces.yard.step", 12, 32));
        }

        private ThemeMotion ReadMotion(Dictionary<string, JsonElement> top)
        {
            var motion = Keys(top["motion"], "motion", "scale", "bloom", "breath", "travel", "live", "travelLine");
            if (motion is null)
            {
                return Empty.Motion;
            }

            return new(
                Number(motion, "scale", "motion.scale", 0.8, 1.3),
                Whole(motion, "bloom", "motion.bloom", 0, 8),
                Whole(motion, "breath", "motion.breath", 2080, 3380),
                Whole(motion, "travel", "motion.travel", 1040, 1690),
                Choice(motion, "live", "motion.live", new Dictionary<string, LiveStyle> { ["loop"] = LiveStyle.Loop, ["fade"] = LiveStyle.Fade }),
                Whole(motion, "travelLine", "motion.travelLine", 2, 3));
        }

        private ThemeCharacters ReadCharacters(Dictionary<string, JsonElement> top)
        {
            var characters = Keys(top["characters"], "characters", "head", "facet", "line", "visor");
            var head = characters is null ? null : Keys(characters["head"], "characters.head", "lightness", "chroma");
            if (characters is null || head is null)
            {
                return Empty.Characters;
            }

            return new(
                Number(head, "lightness", "characters.head.lightness", 0.4, 0.6),
                Number(head, "chroma", "characters.head.chroma", 0.04, 0.12),
                Number(characters, "facet", "characters.facet", 0, 0.15),
                Number(characters, "line", "characters.line", 2, 3.5),
                Choice(characters, "visor", "characters.visor", new Dictionary<string, VisorStyle> { ["band"] = VisorStyle.Band, ["outlined"] = VisorStyle.Outlined }));
        }

        private ModeColors ReadMode(Dictionary<string, JsonElement> top, string mode)
        {
            var section = Keys(top[mode], mode, "hues", "color");
            if (section is null)
            {
                return Empty.Mode;
            }

            var hues = Keys(section["hues"], $"{mode}.hues", "lightness", "chroma");
            var colors = Keys(section["color"], $"{mode}.color", colorTokens);
            Dictionary<string, Rgb> read = [];
            foreach (var token in colorTokens.Where(token => colors?.ContainsKey(token) is true))
            {
                var value = colors![token];
                if (value.ValueKind is JsonValueKind.String && HexColor().IsMatch(value.GetString()!))
                {
                    read[token] = Rgb.Parse(value.GetString()!);
                }
                else
                {
                    Problems.Add($"{mode}.color.{token} is not a color written as #RRGGBB");
                }
            }

            return new(
                hues is null ? 0 : Number(hues, "lightness", $"{mode}.hues.lightness", 0, 1),
                hues is null ? 0 : Number(hues, "chroma", $"{mode}.hues.chroma", 0, 0.4),
                read);
        }

        #endregion

        #region Values

        // The object must be an object holding exactly these keys, each once. Anything else is a problem, and null.
        private Dictionary<string, JsonElement>? Keys(JsonElement element, string path, params string[] expected)
        {
            if (element.ValueKind is not JsonValueKind.Object)
            {
                Problems.Add($"{path} is not an object");
                return null;
            }

            Dictionary<string, JsonElement> found = [];
            foreach (var property in element.EnumerateObject())
            {
                if (!found.TryAdd(property.Name, property.Value))
                {
                    Problems.Add($"{path} holds '{property.Name}' twice");
                }
            }

            var missing = expected.Where(key => !found.ContainsKey(key)).ToList();
            var unknown = found.Keys.Where(key => !expected.Contains(key)).ToList();
            missing.ForEach(key => Problems.Add($"{path} has no '{key}'"));
            unknown.ForEach(key => Problems.Add($"{path} holds '{key}', which the format does not have"));

            return missing.Count > 0 ? null : found;
        }

        private string Text(Dictionary<string, JsonElement> parent, string key, string path, int shortest, int longest, Regex? pattern = null)
        {
            var value = parent[key];
            var text = value.ValueKind is JsonValueKind.String ? value.GetString()! : null;
            if (text is null || text.Length < shortest || text.Length > longest || pattern?.IsMatch(text) is false || text.Any(char.IsControl))
            {
                Problems.Add($"{path} is not plain text of {shortest} to {longest} characters{(pattern is null ? string.Empty : " in the form the format asks for")}");
                return text ?? string.Empty;
            }

            return text;
        }

        private double Number(Dictionary<string, JsonElement> parent, string key, string path, double least, double most)
        {
            var value = parent[key];
            if (value.ValueKind is not JsonValueKind.Number || !value.TryGetDouble(out var number) || number < least || number > most)
            {
                Problems.Add($"{path} is not a number from {least.ToString(CultureInfo.InvariantCulture)} to {most.ToString(CultureInfo.InvariantCulture)}");
                return least;
            }

            return number;
        }

        private int Whole(Dictionary<string, JsonElement> parent, string key, string path, int least, int most)
        {
            var value = parent[key];
            if (value.ValueKind is not JsonValueKind.Number || !value.TryGetInt32(out var number) || number < least || number > most)
            {
                Problems.Add($"{path} is not a whole number from {least} to {most}");
                return least;
            }

            return number;
        }

        private int Weight(Dictionary<string, JsonElement> parent, string key)
        {
            var weight = Whole(parent, key, $"type.weights.{key}", 400, 700);
            if (weight % 100 != 0)
            {
                Problems.Add($"type.weights.{key} is not a step of 100");
            }

            return weight;
        }

        private bool Flag(Dictionary<string, JsonElement> parent, string key, string path)
        {
            var value = parent[key];
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                Problems.Add($"{path} is not true or false");
            }

            return value.ValueKind is JsonValueKind.True;
        }

        private T Choice<T>(Dictionary<string, JsonElement> parent, string key, string path, Dictionary<string, T> names)
        {
            var value = parent[key];
            if (value.ValueKind is JsonValueKind.String && names.TryGetValue(value.GetString()!, out var found))
            {
                return found;
            }

            Problems.Add($"{path} is not one of {string.Join(", ", names.Keys.Select(name => $"'{name}'"))}");
            return names.Values.First();
        }

        private List<int> Scale(JsonElement value)
        {
            var steps = value.ValueKind is JsonValueKind.Array ? value.EnumerateArray().ToList() : [];
            if (steps.Count != 6 || steps.Any(step => step.ValueKind is not JsonValueKind.Number || !step.TryGetInt32(out _)))
            {
                Problems.Add("type.scale is not six whole numbers");
                return [12, 13, 14, 16, 20, 28];
            }

            var sizes = steps.Select(step => step.GetInt32()).ToList();
            if (!sizes.Zip(sizes.Skip(1), (smaller, larger) => smaller < larger).All(ascending => ascending) || sizes[0] < 11 || sizes[2] is < 13 or > 16)
            {
                Problems.Add("type.scale must ascend, start at 11 or more, and have a third step from 13 to 16");
            }

            return sizes;
        }

        private void CheckFace(string? face, string path)
        {
            if (face is not null && !FontFaces.IsKnown(face))
            {
                Problems.Add($"{path} names a face that Drover does not bundle");
            }
        }

        #endregion
    }

    // What a group reads as when it could not be read at all. The problem is already recorded and the file is refused.
    private static class Empty
    {
        #region Properties

        public static readonly ThemeType Type = new("inter", "jetbrains-mono", null, HeadingFace.Interface, false, [12, 13, 14, 16, 20, 28], 400, 500, 600, 1.5, 1.25, 0, 0, 65);

        public static readonly ThemeDensity Density = new(1, new(32, 44), new(44, 44), 8);

        public static readonly ThemeShape Shape = new(CornerLanguage.Faceted, new(6, 10), false, 6, 10, 16, 1, 1.75);

        public static readonly ThemeSurfaces Surfaces = new(2, YardTexture.None, 0, 16);

        public static readonly ThemeMotion Motion = new(1, 8, 2600, 1300, LiveStyle.Loop, 2);

        public static readonly ThemeCharacters Characters = new(0.5, 0.1, 0.08, 2.5, VisorStyle.Band);

        public static readonly ModeColors Mode = new(0, 0, new Dictionary<string, Rgb>());

        #endregion
    }

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    #endregion
}

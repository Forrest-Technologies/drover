namespace Drover.Native.Controls;

/// <summary>
/// Proves a live field tells the platform what it is, and that a paste past its limit is cut with no error.
/// Desktop starts the app with <c>DROVER_FIELD_INPUT_CHECK=1</c>. Android starts it with the
/// <c>drover.field_input</c> extra. Any other start never calls this.
/// </summary>
internal static class FieldInputCheck
{
    #region Private Fields

    private static bool armed;

    private static int finished;

    #endregion

    #region Properties

    public static bool Armed => armed;

    #endregion

    #region Public Methods

    /// <summary>
    /// Arms the check before the platform creates the application, as Android does from its launch intent.
    /// </summary>
    public static void Arm() => armed = true;

    /// <summary>Arms the check when the desktop workflow asked for it.</summary>
    public static void ArmFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("DROVER_FIELD_INPUT_CHECK") == "1")
        {
            armed = true;
        }
    }

    /// <summary>Builds the fields, reads what the platform kept, and ends the process.</summary>
    public static void Start(Window window)
    {
        try
        {
            Prove(window);
        }
        catch (Exception exception)
        {
            Log.Factory.CreateLogger(nameof(FieldInputCheck)).LogError(exception, "Field input check failed");
            Write("Field input check failed");
            NativeStop.Now(1);
        }
    }

    #endregion

    #region Private Methods

    private static void Prove(Window window)
    {
        var email = new Field { Label = "Email", Type = "email", Autofill = "username" };
        var password = new Field { Label = "Password", Type = "password", Autofill = "current-password" };
        var created = new Field { Label = "New password", Type = "password", Autofill = "new-password" };
        var address = new Field { Label = "Address", Type = "url" };
        var name = new Field { Label = "Name", MaxLength = FieldLimits.BotName };
        var panel = new StackPanel();
        panel.Children.Add(email);
        panel.Children.Add(password);
        panel.Children.Add(created);
        panel.Children.Add(address);
        panel.Children.Add(name);
        window.Content = panel;

        var firstHint = Read(email);
        var secondHint = Read(password);
        var thirdHint = Read(created);
        var fourthHint = Read(address);
        Expect("Email", firstHint, "username", "EmailSmtpAddress");
        Expect("Password", secondHint, "password", "Password");
        Expect("New password", thirdHint, "newPassword", "Password");
        Expect("Address", fourthHint, "none", "Url");

        var over = new string('n', FieldLimits.BotName + 1);
        if (Input(name) is not TextBox box)
        {
            throw new InvalidOperationException("The name field is not a text box.");
        }

        // The platform raises TextChanged on a later dispatcher turn, which is what cuts the paste.
        box.Text = over;
        _ = Task.Run(() => WatchPaste(window, name, over));
    }

    private static async Task WatchPaste(Window window, Field name, string over)
    {
        var expected = over[..FieldLimits.BotName];
        var pause = TimeSpan.FromMilliseconds(Tokens.Number("DvDur1"));
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var outcome = new TaskCompletionSource<string?>();
            if (!window.DispatcherQueue.TryEnqueue(() => ReadPaste(name, expected, outcome)))
            {
                await Task.Delay(pause);
                continue;
            }

            string? problem;
            try
            {
                problem = await outcome.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException exception)
            {
                var logger = Log.Factory.CreateLogger(nameof(FieldInputCheck));
                logger.LogError(exception, "The paste was not applied within 20 seconds");
                Fail("The paste was not applied within 20 seconds.");
                return;
            }
            if (problem is null)
            {
                Pass(expected.Length);
                return;
            }

            if (attempt == 39)
            {
                Fail(problem);
                return;
            }

            await Task.Delay(pause);
        }
    }

    private static void ReadPaste(Field name, string expected, TaskCompletionSource<string?> outcome)
    {
        try
        {
            if (name.Value == expected && name.Error is null)
            {
                outcome.TrySetResult(null);
                return;
            }

            var error = name.Error ?? "none";
            var held = Input(name) is TextBox box ? box.Text.Length : -1;
            outcome.TrySetResult(
                $"The name field kept {name.Value.Length} characters, the box held {held}, and its error was {error}.");
        }
        catch (Exception exception)
        {
            Log.Factory.CreateLogger(nameof(FieldInputCheck)).LogError(exception, "Field input check failed");
            outcome.TrySetResult("The name field could not be read.");
        }
    }

    private static void Pass(int kept)
    {
        if (Interlocked.Exchange(ref finished, 1) != 0)
        {
            return;
        }

        Log.Factory.CreateLogger(nameof(FieldInputCheck)).LogWarning(
            "Field input check passed; name kept {Kept}",
            kept);
        Write("Field input check passed");
        NativeStop.Now(0);
    }

    private static void Fail(string reason)
    {
        if (Interlocked.Exchange(ref finished, 1) != 0)
        {
            return;
        }

        Log.Factory.CreateLogger(nameof(FieldInputCheck)).LogError("Field input check failed: {Reason}", reason);
        Write($"Field input check failed: {reason}");
        NativeStop.Now(1);
    }

    private static void Expect(string label, string actual, string android, string desktop)
    {
#if ANDROID
        var expected = android;
#else
        var expected = desktop;
#endif
        if (actual != expected)
        {
            throw new InvalidOperationException($"The {label} field told the platform {actual}.");
        }
    }

    private static string Read(Field field)
    {
        switch (Input(field))
        {
            case TextBox box:
                return Scope(box.InputScope);
            case PasswordBox box:
                return Scope(box.InputScope);
#if ANDROID
            case AndroidTextEditor editor:
                var hints = Hints(editor);
                return hints.Length == 0 ? "none" : hints[0];
#endif
            default:
                throw new InvalidOperationException($"The {field.Label} field has no platform input.");
        }
    }

    private static string Scope(InputScope? scope)
    {
        if (scope is null || scope.Names.Count == 0)
        {
            return string.Empty;
        }

        return scope.Names[0].NameValue.ToString();
    }

    private static Control Input(Field field)
    {
        foreach (var child in field.Children)
        {
            if (child is not Panel panel)
            {
                continue;
            }

            foreach (var inner in panel.Children)
            {
                if (inner is TextBox or PasswordBox)
                {
                    return (Control)inner;
                }
#if ANDROID
                if (inner is AndroidTextEditor editor)
                {
                    return editor;
                }
#endif
            }
        }

        throw new InvalidOperationException($"The {field.Label} field has no input.");
    }

#if ANDROID
    private static string[] Hints(AndroidTextEditor editor)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return [];
        }

        var edit = (Android.Widget.EditText)((ContentPresenter)editor.Content).Content;
        return edit.GetAutofillHints() ?? [];
    }
#endif

    private static void Write(string text)
    {
        var path = Environment.GetEnvironmentVariable("DROVER_FIELD_INPUT_REPORT");
#if ANDROID
        path ??= AndroidReport();
#endif
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, text);
    }

#if ANDROID
    private static string? AndroidReport()
    {
        var root = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
        return root is null ? null : System.IO.Path.Combine(root, "field-input.txt");
    }
#endif

    #endregion
}

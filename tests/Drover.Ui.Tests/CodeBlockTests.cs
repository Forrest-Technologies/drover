namespace Drover.Ui.Tests;

public sealed class CodeBlockTests : BunitContext
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/Components/CodeBlock.razor.js";

    private const string Code = "dotnet build Drover.slnx";

    #endregion

    #region Public Methods

    [Fact]
    public void Code_is_shown_as_text_in_a_preformatted_block()
    {
        var block = Render<CodeBlock>(parameters => parameters.Add(component => component.Code, "<b>not markup</b>"));

        block.Find("pre code").TextContent.Should().Be("<b>not markup</b>");
        block.FindAll("b").Should().BeEmpty();
    }

    [Fact]
    public void The_language_is_shown_when_one_is_given()
    {
        var block = Render<CodeBlock>(parameters => parameters
            .Add(component => component.Code, Code)
            .Add(component => component.Language, "bash"));

        block.Find(".dv-code-language").TextContent.Should().Be("bash");
    }

    [Fact]
    public void No_language_label_is_shown_when_none_is_given()
    {
        var block = Render<CodeBlock>(parameters => parameters.Add(component => component.Code, Code));

        block.FindAll(".dv-code-language").Should().BeEmpty();
    }

    [Fact]
    public void The_copy_control_is_a_real_button_named_for_what_it_does()
    {
        var button = Render<CodeBlock>(parameters => parameters.Add(component => component.Code, Code)).Find("button");

        button.TextContent.Trim().Should().Be("Copy code");
        button.GetAttribute("type").Should().Be("button");
    }

    [Fact]
    public void Nothing_is_announced_before_a_copy()
    {
        var block = Render<CodeBlock>(parameters => parameters.Add(component => component.Code, Code));

        block.Find("[role=status]").TextContent.Should().BeEmpty();
    }

    [Fact]
    public void Copying_sends_the_code_to_the_clipboard_module()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("copyText", Code).SetVoidResult();
        var block = Render<CodeBlock>(parameters => parameters.Add(component => component.Code, Code));

        block.Find("button").Click();

        module.VerifyInvoke("copyText").Arguments.Should().Equal(Code);
    }

    [Fact]
    public void A_copy_is_confirmed_in_words()
    {
        JSInterop.SetupModule(ModulePath).SetupVoid("copyText", Code).SetVoidResult();
        var block = Render<CodeBlock>(parameters => parameters.Add(component => component.Code, Code));

        block.Find("button").Click();

        block.WaitForAssertion(() => block.Find("[role=status]").TextContent.Should().Be("Copied"));
    }

    [Fact]
    public void A_refused_copy_says_what_failed_and_what_to_do()
    {
        JSInterop.SetupModule(ModulePath).SetupVoid("copyText", Code).SetException(new JSException("Write permission denied."));
        var block = Render<CodeBlock>(parameters => parameters.Add(component => component.Code, Code));

        block.Find("button").Click();

        block.WaitForAssertion(() => block.Find("[role=status]").TextContent
            .Should().Be("Copy failed. Select the code and copy it by hand."));
    }

    #endregion
}

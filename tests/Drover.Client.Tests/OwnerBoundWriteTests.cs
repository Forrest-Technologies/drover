namespace Drover.Client.Tests;

/// <summary>
/// A write to the server for one person on one server is made only while that person is signed in there, and is
/// undone if they were not by the time it landed. The server takes a write as coming from whoever is signed in when it
/// arrives, so two checks around the write are what bind it to the person it was made for.
/// </summary>
public sealed class OwnerBoundWriteTests
{
    #region Private Fields

    private static readonly Uri origin = new("https://drover.example.test/");

    private static readonly Guid owner = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    private static readonly Guid other = Guid.Parse("00000000-0000-7000-8000-0000000000a2");

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_write_for_the_signed_in_owner_is_made_once_and_not_undone()
    {
        var calls = new Calls();

        var made = await Run(calls, () => origin, () => owner);

        made.Should().BeTrue();
        (calls.Writes, calls.Undos).Should().Be((1, 0));
    }

    [Fact]
    public async Task A_write_is_not_made_when_another_person_is_signed_in_already()
    {
        var calls = new Calls();

        var made = await Run(calls, () => origin, () => other);

        made.Should().BeFalse();
        (calls.Writes, calls.Undos).Should().Be((0, 0));
    }

    [Fact]
    public async Task A_write_is_not_made_when_nobody_is_signed_in_or_the_server_is_another()
    {
        var nobody = new Calls();
        var elsewhere = new Calls();

        (await Run(nobody, () => origin, () => null)).Should().BeFalse();
        (await Run(elsewhere, () => new Uri("https://other.example.test/"), () => owner)).Should().BeFalse();

        (nobody.Writes, elsewhere.Writes).Should().Be((0, 0));
    }

    [Fact]
    public async Task A_write_that_landed_after_the_person_signed_out_and_another_signed_in_is_undone_and_not_counted()
    {
        var calls = new Calls();
        var signedIn = owner;
        calls.OnWrite = () => signedIn = other;

        var made = await Run(calls, () => origin, () => signedIn);

        made.Should().BeFalse("the account changed between the check and the write");
        (calls.Writes, calls.Undos).Should().Be((1, 1));
    }

    [Fact]
    public async Task A_write_that_landed_after_the_server_was_changed_is_undone_and_not_counted()
    {
        var calls = new Calls();
        var server = origin;
        calls.OnWrite = () => server = new Uri("https://other.example.test/");

        var made = await Run(calls, () => server, () => owner);

        made.Should().BeFalse();
        (calls.Writes, calls.Undos).Should().Be((1, 1));
    }

    [Fact]
    public async Task An_undo_that_cannot_reach_the_server_is_left_to_the_server_and_still_not_counted()
    {
        var calls = new Calls { UndoFails = true };
        var signedIn = owner;
        calls.OnWrite = () => signedIn = other;

        var made = await Run(calls, () => origin, () => signedIn);

        made.Should().BeFalse();
        calls.Undos.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_write_is_not_swallowed_and_is_not_undone()
    {
        var calls = new Calls { WriteFails = true };

        var failing = () => Run(calls, () => origin, () => owner);

        await failing.Should().ThrowAsync<HttpRequestException>();
        calls.Undos.Should().Be(0);
    }

    [Fact]
    public void A_second_save_is_refused_while_one_is_working_and_may_start_once_it_is_done()
    {
        var saves = new SingleSave();

        using (var first = saves.TryStart())
        {
            first.Should().NotBeNull();
            saves.TryStart().Should().BeNull("a second file card cannot start saving during the first copy");
        }

        using var again = saves.TryStart();
        again.Should().NotBeNull();
    }

    [Fact]
    public void Disposing_a_turn_twice_gives_back_one_turn()
    {
        var saves = new SingleSave();
        var first = saves.TryStart()!;

        first.Dispose();
        first.Dispose();

        using var second = saves.TryStart();
        second.Should().NotBeNull();
        saves.TryStart().Should().BeNull("one release, however many times the first turn was disposed");
    }

    #endregion

    #region Private Methods

    private static Task<bool> Run(Calls calls, Func<Uri?> server, Func<Guid?> signedIn) =>
        OwnerBoundWrite.Run(server, origin, () => Task.FromResult(signedIn()), owner, calls.Write, calls.Undo);

    #endregion

    #region Nested Types

    private sealed class Calls
    {
        #region Properties

        public int Writes { get; private set; }

        public int Undos { get; private set; }

        public Action? OnWrite { get; set; }

        public bool WriteFails { get; init; }

        public bool UndoFails { get; init; }

        #endregion

        #region Public Methods

        public Task Write()
        {
            Writes++;
            OnWrite?.Invoke();
            return WriteFails ? Task.FromException(new HttpRequestException("Sample: unreachable.")) : Task.CompletedTask;
        }

        public Task Undo()
        {
            Undos++;
            return UndoFails ? Task.FromException(new HttpRequestException("Sample: unreachable.")) : Task.CompletedTask;
        }

        #endregion
    }

    #endregion
}

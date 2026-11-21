namespace Drover.Engine.Tests.Bots;

public sealed class BotRequestIdsTests
{
    #region Private Fields

    private static readonly Guid Space = Guid.Parse("0196f1a0-0000-7000-8000-000000000001");

    private static readonly Guid Person = Guid.Parse("0196f1a0-0000-7000-8000-000000000002");

    private static readonly Guid Bot = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid Request = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    #endregion

    #region Public Methods

    [Fact]
    public void The_id_reports_version_8_and_the_standard_variant_in_network_byte_order()
    {
        var id = BotRequestIds.For(Space, AuditActor.Person(Person), Request);
        var bytes = id.ToByteArray(bigEndian: true);

        id.Version.Should().Be(8, "the Guid says which version it is, whatever byte order it was built in");
        (bytes[6] >> 4).Should().Be(8, "the version sits in the high nibble of byte 6 in network order");
        (bytes[8] & 0xC0).Should().Be(0x80, "the variant sits in the top two bits of byte 8 in network order");
    }

    [Fact]
    public void Every_id_reports_its_version_and_variant_whatever_the_hash_was()
    {
        for (var index = 0; index < 200; index++)
        {
            var id = BotRequestIds.For(Guid.NewGuid(), AuditActor.Person(Guid.NewGuid()), Guid.NewGuid());
            var bytes = id.ToByteArray(bigEndian: true);

            id.Version.Should().Be(8);
            (bytes[8] & 0xC0).Should().Be(0x80);
        }
    }

    [Fact]
    public void The_same_space_maker_and_request_give_the_same_id()
    {
        var first = BotRequestIds.For(Space, AuditActor.Person(Person), Request);
        var again = BotRequestIds.For(Space, AuditActor.Person(Person), Request);

        again.Should().Be(first, "a retry of the request must find the bot the first one made");
    }

    [Fact]
    public void A_different_space_person_bot_or_request_gives_a_different_id()
    {
        var baseline = BotRequestIds.For(Space, AuditActor.Person(Person), Request);

        Guid[] others =
        [
            BotRequestIds.For(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1"), AuditActor.Person(Person), Request),
            BotRequestIds.For(Space, AuditActor.Person(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2")), Request),
            BotRequestIds.For(Space, new AuditActor(Person, Bot), Request),
            BotRequestIds.For(Space, AuditActor.Person(Person), Guid.Parse("0196f1a0-0000-7000-8000-0000000000a3")),
        ];

        others.Should().OnlyHaveUniqueItems().And.NotContain(baseline);
    }

    [Fact]
    public void The_id_is_never_the_request_id()
    {
        BotRequestIds.For(Space, AuditActor.Person(Person), Request).Should().NotBe(Request);
    }

    #endregion
}

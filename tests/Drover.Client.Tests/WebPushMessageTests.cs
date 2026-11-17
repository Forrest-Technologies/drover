namespace Drover.Client.Tests;

public sealed class WebPushMessageTests
{
    #region Public Methods

    [Fact]
    public void The_RFC_8291_record_is_opened_and_a_changed_record_is_refused()
    {
        var publicKey = Base64Url.DecodeFromChars("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        using var receiver = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.DecodeFromChars("q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94"),
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..] },
        });
        var secret = Base64Url.DecodeFromChars("BTBZMqHH6r4Tts7J_aSIgg");
        var data = Base64Url.DecodeFromChars("""
            DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27ml
            mlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPT
            pK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN
            """.ReplaceLineEndings(string.Empty));

        var text = Encoding.UTF8.GetString(WebPushMessage.Open(data, receiver, secret));
        text.Should().Be("When I grow up, I want to be a watermelon");

        data[^1] ^= 1;
        var changed = () => WebPushMessage.Open(data, receiver, secret);
        changed.Should().Throw<CryptographicException>();
    }

    #endregion
}

namespace Drover.Client.Push;

/// <summary>Decrypts the single-record aes128gcm payload defined by RFC 8291.</summary>
public static class WebPushMessage
{
    #region Private Fields

    private const int HeaderLength = 86;

    private const int TagLength = 16;

    #endregion

    #region Public Methods

    public static byte[] ExportPublicKey(ECDiffieHellman key)
    {
        var point = key.ExportParameters(false).Q;
        return [4, .. point.X!, .. point.Y!];
    }

    public static byte[] Open(ReadOnlySpan<byte> payload, ECDiffieHellman receiver, ReadOnlySpan<byte> authSecret)
    {
        if (authSecret.Length != 16 || payload.Length <= HeaderLength + TagLength || payload.Length > 4096
            || payload[20] != 65 || payload[21] != 4
            || BinaryPrimitives.ReadUInt32BigEndian(payload[16..20]) <= payload.Length - HeaderLength)
        {
            throw new CryptographicException("The push record has an invalid header.");
        }

        var senderKey = payload[21..HeaderLength];
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = senderKey[1..33].ToArray(), Y = senderKey[33..].ToArray() },
        });
        using var senderPublic = sender.PublicKey;
        var shared = receiver.DeriveRawSecretAgreement(senderPublic);
        try
        {
            return OpenRecord(payload, shared, ExportPublicKey(receiver), authSecret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    #endregion

    #region Private Methods

    private static byte[] OpenRecord(ReadOnlySpan<byte> payload, byte[] shared, byte[] receiverKey, ReadOnlySpan<byte> authSecret)
    {
        byte[] info = [.. "WebPush: info\0"u8, .. receiverKey, .. payload[21..HeaderLength]];
        var combined = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, authSecret.ToArray(), info);
        try
        {
            return Decrypt(payload, combined);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combined);
        }
    }

    private static byte[] Decrypt(ReadOnlySpan<byte> payload, byte[] combined)
    {
        var salt = payload[..16].ToArray();
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, combined, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        try
        {
            var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, combined, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());
            var plaintext = new byte[payload.Length - HeaderLength - TagLength];
            using var cipher = new AesGcm(key, TagLength);
            cipher.Decrypt(nonce, payload[HeaderLength..^TagLength], payload[^TagLength..], plaintext);

            var delimiter = Array.FindLastIndex(plaintext, value => value != 0);
            if (delimiter < 0 || plaintext[delimiter] != 2)
            {
                throw new CryptographicException("The push record has an invalid delimiter.");
            }

            return plaintext[..delimiter];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    #endregion
}

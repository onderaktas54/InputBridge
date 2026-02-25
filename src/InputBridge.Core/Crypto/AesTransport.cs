using System;
using System.Security.Cryptography;

namespace InputBridge.Core.Crypto;

public sealed class AesTransport : IDisposable
{
    private readonly byte[] _key;
    private readonly AesGcm _aesGcm;

    public AesTransport(byte[] key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes (256-bit) for AES-256.", nameof(key));

        _key = new byte[key.Length];
        Array.Copy(key, _key, key.Length);
        _aesGcm = new AesGcm(_key, 16); // 16 bytes for auth tag
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        // Allocate space for [12-byte IV] + [ciphertext] + [16-byte tag]
        byte[] payload = new byte[12 + plaintext.Length + 16];
        
        Span<byte> iv = payload.AsSpan(0, 12);
        Span<byte> ciphertext = payload.AsSpan(12, plaintext.Length);
        Span<byte> tag = payload.AsSpan(12 + plaintext.Length, 16);

        // Generate random IV
        RandomNumberGenerator.Fill(iv);

        // Encrypt
        _aesGcm.Encrypt(iv, plaintext, ciphertext, tag);

        return payload;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> encrypted)
    {
        if (encrypted.Length < 12 + 16)
        {
            throw new CryptographicException("Payload is too short to contain IV and AuthTag.");
        }

        ReadOnlySpan<byte> iv = encrypted.Slice(0, 12);
        ReadOnlySpan<byte> ciphertext = encrypted.Slice(12, encrypted.Length - 12 - 16);
        ReadOnlySpan<byte> tag = encrypted.Slice(encrypted.Length - 16, 16);

        byte[] plaintext = new byte[ciphertext.Length];
        
        // Decrypt will throw CryptographicException if the tag doesn't match
        _aesGcm.Decrypt(iv, ciphertext, tag, plaintext);

        return plaintext;
    }

    public void Dispose()
    {
        _aesGcm.Dispose();
        Array.Clear(_key, 0, _key.Length); // zero out the key for security
    }
}

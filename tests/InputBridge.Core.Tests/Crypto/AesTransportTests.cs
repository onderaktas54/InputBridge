using System;
using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using InputBridge.Core.Crypto;
using Xunit;

namespace InputBridge.Core.Tests.Crypto;

public class AesTransportTests
{
    private readonly byte[] _key = new byte[32];

    public AesTransportTests()
    {
        RandomNumberGenerator.Fill(_key);
    }

    [Fact]
    public void Encrypt_Decrypt_Roundtrip_ShouldPreserveData()
    {
        // Arrange
        using var aes = new AesTransport(_key);
        var original = new byte[] { 1, 2, 3, 4, 5, 255 };

        // Act
        var encrypted = aes.Encrypt(original);
        var decrypted = aes.Decrypt(encrypted);

        // Assert
        decrypted.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ShouldThrowCryptographicException()
    {
        // Arrange
        var wrongKey = new byte[32];
        RandomNumberGenerator.Fill(wrongKey);

        using var aes1 = new AesTransport(_key);
        using var aes2 = new AesTransport(wrongKey);
        var original = new byte[] { 1, 2, 3 };

        // Act
        var encrypted = aes1.Encrypt(original);
        Action act = () => aes2.Decrypt(encrypted);

        // Assert
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithManipulatedCiphertext_ShouldThrowCryptographicException()
    {
        // Arrange
        using var aes = new AesTransport(_key);
        var encrypted = aes.Encrypt(new byte[] { 1, 2, 3 });

        // Act
        encrypted[13] ^= 0x01; // flip a bit in the ciphertext

        Action act = () => aes.Decrypt(encrypted);

        // Assert
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Encrypt_CalledTwice_ShouldProduceDifferentIVs()
    {
        // Arrange
        using var aes = new AesTransport(_key);
        var original = new byte[] { 1, 2, 3 };

        // Act
        var encrypted1 = aes.Encrypt(original);
        var encrypted2 = aes.Encrypt(original);

        // Assert
        var iv1 = encrypted1.AsSpan(0, 12).ToArray();
        var iv2 = encrypted2.AsSpan(0, 12).ToArray();
        
        iv1.Should().NotBeEquivalentTo(iv2); // nonces MUST differ
    }

    [Fact]
    public void Benchmark_10000_Iterations_ShouldBeFast()
    {
        // Arrange
        using var aes = new AesTransport(_key);
        var original = new byte[24]; // simulated InputPacket size

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            var e = aes.Encrypt(original);
            var d = aes.Decrypt(e);
        }
        sw.Stop();

        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }
}

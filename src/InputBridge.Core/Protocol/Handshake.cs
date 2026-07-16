using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InputBridge.Core.Protocol;

public record SessionInfo(byte[] AesKey, string RemoteHostname, string RemoteVersion);

public sealed class HandshakeManager
{
    private const int MaxHandshakeLineChars = 4096;
    private static readonly string AppVersion =
        typeof(HandshakeManager).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    // Minimal JSON models for handshaking
    private record HandshakeChallenge(byte[] Challenge);
    private record HandshakeResponse(byte[] HmacResponse, string Hostname, string Version);
    private record HandshakeConfirmation(byte[] EncryptedAesKey);

    public async Task<SessionInfo?> PerformAsHost(
        TcpClient client,
        string sharedSecret,
        CancellationToken ct = default)
    {
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // 1. Generate challenge
        var challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);

        // 2. Send challenge
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(new HandshakeChallenge(challenge)).AsMemory(), ct);

        // 3. Receive client response
        string? responseJson = await ReadLineLimitedAsync(reader, ct);
        if (responseJson == null) return null;

        var response = JsonSerializer.Deserialize<HandshakeResponse>(responseJson);
        if (response == null) return null;

        // 4. Verify HMAC
        byte[] secretBytes = Encoding.UTF8.GetBytes(sharedSecret);
        using var hmac = new HMACSHA256(secretBytes);
        byte[] expectedHmac = hmac.ComputeHash(challenge);

        if (!CryptographicOperations.FixedTimeEquals(expectedHmac, response.HmacResponse))
        {
            return null; // HMAC mismatch, connection will be closed
        }

        // 5. Generate AES Session Key
        var sessionKey = new byte[32];
        RandomNumberGenerator.Fill(sessionKey);

        // Preserve the v1 wire format: derive a 32-byte wrapping key from the shared secret.
        byte[] derivedSecret = SHA256.HashData(secretBytes); // Ensure exactly 32 bytes
        var iv = new byte[12];
        RandomNumberGenerator.Fill(iv);
        var tag = new byte[16];
        var cipherKey = new byte[32];
        using var aes = new AesGcm(derivedSecret, 16);
        aes.Encrypt(iv, sessionKey, cipherKey, tag);

        // Package auth encrypted payload: [IV (12)][Tag (16)][Cipher (32)]
        var encryptedPayload = new byte[60];
        Buffer.BlockCopy(iv, 0, encryptedPayload, 0, 12);
        Buffer.BlockCopy(tag, 0, encryptedPayload, 12, 16);
        Buffer.BlockCopy(cipherKey, 0, encryptedPayload, 28, 32);

        await writer.WriteLineAsync(
            JsonSerializer.Serialize(new HandshakeConfirmation(encryptedPayload)).AsMemory(), ct);

        return new SessionInfo(sessionKey, response.Hostname, response.Version);
    }

    public async Task<SessionInfo?> PerformAsClient(
        TcpClient client,
        string sharedSecret,
        CancellationToken ct = default)
    {
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // 1. Receive challenge
        string? challengeJson = await ReadLineLimitedAsync(reader, ct);
        if (challengeJson == null) return null;

        var challenge = JsonSerializer.Deserialize<HandshakeChallenge>(challengeJson);
        if (challenge == null) return null;

        // 2. Compute HMAC
        byte[] secretBytes = Encoding.UTF8.GetBytes(sharedSecret);
        using var hmac = new HMACSHA256(secretBytes);
        byte[] hmacResult = hmac.ComputeHash(challenge.Challenge);

        // 3. Send response
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(new HandshakeResponse(hmacResult, Environment.MachineName, AppVersion)).AsMemory(), ct);

        // 4. Receive confirmation (Session Key)
        string? confJson = await ReadLineLimitedAsync(reader, ct);
        if (confJson == null) return null;

        var confirmation = JsonSerializer.Deserialize<HandshakeConfirmation>(confJson);
        if (confirmation == null || confirmation.EncryptedAesKey.Length != 60) return null;

        // 5. Decrypt session key
        var iv = new byte[12];
        var tag = new byte[16];
        var cipherKey = new byte[32];
        var sessionKey = new byte[32];

        Buffer.BlockCopy(confirmation.EncryptedAesKey, 0, iv, 0, 12);
        Buffer.BlockCopy(confirmation.EncryptedAesKey, 12, tag, 0, 16);
        Buffer.BlockCopy(confirmation.EncryptedAesKey, 28, cipherKey, 0, 32);

        byte[] derivedSecret = SHA256.HashData(secretBytes);
        using var aes = new AesGcm(derivedSecret, 16);

        try
        {
            aes.Decrypt(iv, cipherKey, tag, sessionKey);
        }
        catch (CryptographicException)
        {
            return null; // Key decryption failed
        }

        return new SessionInfo(sessionKey, "Host", "unknown"); // Protocol v1 does not send host metadata.
    }

    private static async Task<string?> ReadLineLimitedAsync(StreamReader reader, CancellationToken ct)
    {
        var line = new StringBuilder();
        var character = new char[1];

        while (line.Length <= MaxHandshakeLineChars)
        {
            int read = await reader.ReadAsync(character.AsMemory(), ct);
            if (read == 0) return line.Length == 0 ? null : line.ToString();
            if (character[0] == '\n')
            {
                if (line.Length > 0 && line[^1] == '\r') line.Length--;
                return line.ToString();
            }

            line.Append(character[0]);
        }

        throw new InvalidDataException("Handshake message exceeds the allowed size.");
    }
}

#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.GameInterop.CustomCards;

internal static class BppCustomCardIds
{
    public static readonly Guid Namespace = new("fe4ad371-2efd-5e90-a077-eb8e8e8f51e7");

    public static Guid ForAchievement(string achievementId)
    {
        if (string.IsNullOrWhiteSpace(achievementId))
            throw new ArgumentException("Achievement id is required.", nameof(achievementId));

        return CreateUuid5(Namespace, $"achievement:{achievementId}");
    }

    private static Guid CreateUuid5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

        byte[] hash;
        using (var sha1 = SHA1.Create())
            hash = sha1.ComputeHash(input);

        var newGuid = new byte[16];
        Array.Copy(hash, newGuid, newGuid.Length);
        newGuid[6] = (byte)((newGuid[6] & 0x0F) | 0x50);
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);
        SwapGuidByteOrder(newGuid);
        return new Guid(newGuid);
    }

    private static void SwapGuidByteOrder(byte[] guid)
    {
        Swap(guid, 0, 3);
        Swap(guid, 1, 2);
        Swap(guid, 4, 5);
        Swap(guid, 6, 7);
    }

    private static void Swap(byte[] bytes, int left, int right)
    {
        var temp = bytes[left];
        bytes[left] = bytes[right];
        bytes[right] = temp;
    }
}

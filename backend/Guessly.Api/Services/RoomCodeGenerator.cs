using System.Collections.Concurrent;

namespace Guessly.Api.Services;

public static class RoomCodeGenerator
{
    // Excludes visually ambiguous characters (0/O, 1/I).
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Random Random = new();

    public static string Generate(ConcurrentDictionary<string, Models.Room> existingRooms)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = new string(Enumerable.Range(0, 5).Select(_ => Alphabet[Random.Next(Alphabet.Length)]).ToArray());
            if (!existingRooms.ContainsKey(code))
                return code;
        }
        throw new InvalidOperationException("Could not generate a unique room code.");
    }
}

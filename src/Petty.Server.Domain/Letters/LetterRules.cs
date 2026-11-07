using System.Buffers;
using System.Text;

namespace Petty.Server.Domain.Letters;

public static class LetterRules
{
    public const int UnreadLimit = 10;
    public const int MaxBodyLength = 500;

    public static bool IsValidBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        ReadOnlySpan<char> remaining = body.AsSpan();
        int length = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune rune, out int consumed) != OperationStatus.Done
                || ++length > MaxBodyLength || (Rune.IsControl(rune) && rune.Value is not ('\n' or '\r' or '\t')))
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }
}

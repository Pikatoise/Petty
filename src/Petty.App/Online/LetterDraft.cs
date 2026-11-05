using System.Buffers;
using System.Text;
using Petty.Online.Client;

namespace Petty.App.Online;

/// <summary>Freezes the payload and idempotency key until the server confirms the send.</summary>
internal sealed class LetterDraft
{
    public SendLetter? Pending
    {
        get; private set;
    }
    public bool Sent
    {
        get; private set;
    }

    public SendLetter Prepare(Guid recipient, string body)
    {
        if (Sent)
        {
            throw new InvalidOperationException("This letter has already been sent.");
        }
        if (Pending is not null)
        {
            return Pending;
        }
        if (recipient == Guid.Empty || !IsValidBody(body))
        {
            throw new ArgumentException("Invalid letter.");
        }
        return Pending = new SendLetter(recipient, body, Guid.NewGuid());
    }

    public void Confirm(SentLetter result)
    {
        if (Pending is null || result.LetterId == Guid.Empty)
        {
            throw new InvalidOperationException("Missing send acknowledgement.");
        }
        Sent = true;
    }

    // Only for business rejections that guarantee this payload was not accepted.
    public void Reject()
    {
        if (!Sent)
        {
            Pending = null;
        }
    }

    public static bool IsValidBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }
        ReadOnlySpan<char> remaining = body;
        int count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune rune, out int consumed) != OperationStatus.Done ||
                ++count > 500 || (Rune.IsControl(rune) && rune.Value is not (9 or 10 or 13)))
            {
                return false;
            }
            remaining = remaining[consumed..];
        }
        return true;
    }
}

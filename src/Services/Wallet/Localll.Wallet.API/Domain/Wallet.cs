using Localll.SharedKernel;

namespace Localll.Wallet.API.Domain;

public enum LedgerEntryType
{
    Credit,
    Debit
}

/// <summary>
/// The balance is never set directly — it only moves through Credit/Debit,
/// each of which appends an immutable ledger entry (event-sourcing lite).
/// </summary>
public class Wallet : AggregateRoot
{
    public Guid OwnerId { get; set; }
    public string OwnerType { get; set; } = "Customer";   // Customer | Partner
    public decimal Balance { get; private set; }
    public List<LedgerEntry> Entries { get; set; } = [];

    public LedgerEntry Credit(decimal amount, string reason, Guid? referenceId = null)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be positive.");
        Balance += amount;
        return Append(LedgerEntryType.Credit, amount, reason, referenceId);
    }

    public Result<LedgerEntry> Debit(decimal amount, string reason, Guid? referenceId = null)
    {
        if (amount <= 0) return Result.Failure<LedgerEntry>(Error.Validation("Debit amount must be positive."));
        if (Balance < amount) return Result.Failure<LedgerEntry>(Error.Conflict("Insufficient balance."));
        Balance -= amount;
        return Result.Success(Append(LedgerEntryType.Debit, amount, reason, referenceId));
    }

    private LedgerEntry Append(LedgerEntryType type, decimal amount, string reason, Guid? referenceId)
    {
        var entry = new LedgerEntry
        {
            WalletId = Id,
            Type = type,
            Amount = amount,
            BalanceAfter = Balance,
            Reason = reason,
            ReferenceId = referenceId
        };
        Entries.Add(entry);
        UpdatedAtUtc = DateTime.UtcNow;
        return entry;
    }
}

public class LedgerEntry : Entity
{
    public Guid WalletId { get; set; }
    public LedgerEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }   // order / payment / withdrawal id
}

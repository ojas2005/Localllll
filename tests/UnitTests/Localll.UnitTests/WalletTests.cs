using Localll.Wallet.API.Domain;
using Xunit;

namespace UnitTests;

public class WalletTests
{
    [Fact]
    public void Credit_IncreasesBalance_AndAppendsLedgerEntry()
    {
        var wallet = new Wallet { OwnerId = Guid.NewGuid() };

        var entry = wallet.Credit(100m, "Delivery earning");

        Assert.Equal(100m, wallet.Balance);
        Assert.Single(wallet.Entries);
        Assert.Equal(LedgerEntryType.Credit, entry.Type);
        Assert.Equal(100m, entry.BalanceAfter);
    }

    [Fact]
    public void Debit_WithSufficientBalance_Succeeds()
    {
        var wallet = new Wallet { OwnerId = Guid.NewGuid() };
        wallet.Credit(200m, "Top-up");

        var result = wallet.Debit(50m, "Withdrawal");

        Assert.True(result.IsSuccess);
        Assert.Equal(150m, wallet.Balance);
        Assert.Equal(150m, result.Value.BalanceAfter);
    }

    [Fact]
    public void Debit_BeyondBalance_FailsAndLeavesBalanceUntouched()
    {
        var wallet = new Wallet { OwnerId = Guid.NewGuid() };
        wallet.Credit(30m, "Top-up");

        var result = wallet.Debit(50m, "Withdrawal");

        Assert.True(result.IsFailure);
        Assert.Equal(30m, wallet.Balance);
        Assert.Single(wallet.Entries); // only the credit
    }

    [Fact]
    public void Credit_NonPositiveAmount_Throws()
    {
        var wallet = new Wallet();
        Assert.Throws<ArgumentOutOfRangeException>(() => { wallet.Credit(0m, "Invalid"); });
    }

    [Fact]
    public void LedgerEntries_TrackRunningBalance()
    {
        var wallet = new Wallet();
        wallet.Credit(100m, "A");
        wallet.Credit(50m, "B");
        wallet.Debit(25m, "C");

        Assert.Equal([100m, 150m, 125m], wallet.Entries.Select(e => e.BalanceAfter));
    }
}

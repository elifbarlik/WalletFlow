namespace WalletFlow.Domain.Transactions;

public enum TransactionType
{
    TopUp = 1,
    Transfer = 2,
}

public enum TransactionStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3,
}

public enum EntryDirection
{
    Debit = 1,
    Credit = 2,
}

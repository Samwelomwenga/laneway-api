namespace Laneway.Api;

public static class AccountView
{
    public static AccountDto Of(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountDto
        (
            account.Id,
            account.Name,
            account.UserId,
            account.CreatedBy,
            account.CreatedAt,
            account.UpdatedAt,
            account.UpdatedBy
        );
    }
}

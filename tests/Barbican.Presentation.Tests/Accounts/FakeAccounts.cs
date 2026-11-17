// An account manager the ViewModel tests script: sign-in answers, switch answers, and the statuses the popover shows.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Presentation.Tests.Accounts;

internal sealed class FakeAccounts : IAccountManager, IAccountOverview
{
    private List<AccountStatus> _statuses = [];

    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged;

    public event EventHandler? StatusesChanged;

    public List<PasswordSignIn> PasswordRequests { get; } = [];

    public List<OAuthSignIn> OAuthRequests { get; } = [];

    public Queue<Result<AccountInfo>> Answers { get; } = new();

    public Result<Unit> SwitchAnswer { get; set; } = Result<Unit>.Ok(default);

    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Thrown by the next sign-in (a vault that cannot be written).</summary>
    public Exception? Throws { get; set; }

    public IReadOnlyList<AccountInfo> Accounts => [.. _statuses.Select(s => s.Account)];

    public AccountInfo? Active => _statuses.FirstOrDefault(s => s.IsActive)?.Account;

    public IReadOnlyList<AccountStatus> Statuses => _statuses;

    public static AccountInfo Info(string user, string server = "https://jmap.example.org") => new(AccountKey.Of(server, user), user, server);

    public void Set(params AccountStatus[] statuses)
    {
        _statuses = [.. statuses];
        StatusesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct)
    {
        PasswordRequests.Add(request);
        if (Throws is { } boom)
        {
            throw boom;
        }

        if (Gate is { } g)
        {
            await g.Task.WaitAsync(ct);
        }

        return Answers.Count > 0 ? Answers.Dequeue() : Result<AccountInfo>.Ok(Info(request.User));
    }

    public async Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct)
    {
        OAuthRequests.Add(request);
        if (Gate is { } g)
        {
            await g.Task.WaitAsync(ct);
        }

        return Answers.Count > 0 ? Answers.Dequeue() : Result<AccountInfo>.Ok(Info(request.LoginHint ?? "x"));
    }

    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct)
    {
        if (SwitchAnswer.IsOk)
        {
            var previous = Active;
            _statuses = [.. _statuses.Select(s => s with { IsActive = string.Equals(s.Account.Key, accountKey, StringComparison.Ordinal) })];
            ActiveChanged?.Invoke(this, new ActiveAccountChangedEventArgs(previous, Active));
            StatusesChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(SwitchAnswer);
    }

    public IAccountSession? SessionFor(string accountKey) => null;

    public static Result<AccountInfo> Fail(JmapError error) => Result<AccountInfo>.Fail(error);
}

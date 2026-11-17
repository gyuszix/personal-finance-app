namespace PersonalFinance.Api.Data;

// Marks code that legitimately reads or writes every user's rows - the
// scheduled sync, webhook-triggered syncs - so row-level security lets it
// through (#119). Everything else sees only the signed-in user's rows, or
// nothing.
//
//     using var _ = DbSystemScope.Enter();
//
// Only honoured outside an HTTP request: RlsSessionInterceptor ignores it
// whenever there's an HttpContext, so request code can't opt out by
// accident (or by an AsyncLocal leaking into it).
public static class DbSystemScope
{
    private static readonly AsyncLocal<bool> Active = new();

    public static bool IsActive => Active.Value;

    public static IDisposable Enter()
    {
        var previous = Active.Value;
        Active.Value = true;
        return new Exit(previous);
    }

    private sealed class Exit(bool previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}

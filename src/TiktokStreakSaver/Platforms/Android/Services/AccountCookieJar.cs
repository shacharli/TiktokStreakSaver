using Android.Webkit;
using TiktokStreakSaver.Services;

namespace TiktokStreakSaver.Platforms.Android.Services;

/// <summary>
/// Android has a single global cookie jar. Each account's cookies are kept in secure storage and
/// swapped into the jar one account at a time.
/// </summary>
internal static class AccountCookieJar
{
    private static readonly string[] Urls = { "https://www.tiktok.com", "https://tiktok.com" };
    private const int MaxAgeSeconds = 60 * 60 * 24 * 180;

    public static async Task<bool> SnapshotAsync(AccountService accounts, string accountId)
    {
        var cm = CookieManager.Instance;
        if (cm == null) return false;

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var url in Urls)
            foreach (var (name, value) in CookieHeader.Parse(cm.GetCookie(url)))
                merged[name] = value;

        if (!merged.ContainsKey("sessionid")) return false;

        var header = string.Join("; ", merged.Select(kv => $"{kv.Key}={kv.Value}"));
        return await accounts.SaveCookiesAsync(accountId, header);
    }

    public static async Task<bool> ApplyAsync(AccountService accounts, string accountId)
    {
        var header = await accounts.GetCookiesAsync(accountId);
        var cm = CookieManager.Instance;
        if (cm == null || string.IsNullOrEmpty(header)) return false;

        await ClearAsync();
        foreach (var (name, value) in CookieHeader.Parse(header))
        {
            var domain = name.StartsWith("__Host-", StringComparison.Ordinal) ? string.Empty : "; Domain=.tiktok.com";
            cm.SetCookie("https://www.tiktok.com", $"{name}={value}{domain}; Path=/; Secure; Max-Age={MaxAgeSeconds}");
        }
        cm.Flush();
        return true;
    }

    public static async Task ClearAsync()
    {
        var cm = CookieManager.Instance;
        if (cm == null) return;

        var tcs = new TaskCompletionSource<bool>();
        cm.RemoveAllCookies(new DoneCallback(() => tcs.TrySetResult(true)));
        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        cm.Flush();
        WebStorage.Instance?.DeleteAllData();
    }

    /// <summary>Adopts the session currently in the cookie jar (from before multi-account support) as "Account 1".</summary>
    public static async Task EnsureMigratedAsync(AccountService accounts, SettingsService settings)
    {
        var existing = accounts.GetAccounts();
        foreach (var a in existing)
            if (!string.IsNullOrEmpty(await accounts.GetCookiesAsync(a.Id))) return;
        if (!TikTokWebViewHelper.HasValidSessionCookie()) return;

        var created = existing.Count == 0;
        var account = existing.FirstOrDefault() ?? accounts.Add("Account 1");
        if (!await SnapshotAsync(accounts, account.Id))
        {
            if (created) accounts.Remove(account.Id);
            return;
        }

        account.UserAgent = new SessionService().GetLoginUserAgent() ?? AppConstants.DesktopChromeUserAgent;
        account.SessionValid = true;
        account.LastSnapshot = DateTime.Now;
        accounts.Update(account);

        var friends = settings.GetFriendsList();
        var changed = false;
        foreach (var f in friends)
        {
            if (!string.IsNullOrEmpty(f.AccountId)) continue;
            f.AccountId = account.Id;
            changed = true;
        }
        if (changed) settings.SaveFriendsList(friends);
    }

    private sealed class DoneCallback : Java.Lang.Object, IValueCallback
    {
        private readonly Action _done;
        public DoneCallback(Action done) => _done = done;
        public void OnReceiveValue(Java.Lang.Object? value) => _done();
    }
}

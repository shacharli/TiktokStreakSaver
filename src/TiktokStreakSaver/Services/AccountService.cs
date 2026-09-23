using System.Text.Json;
using TiktokStreakSaver.Models;
using TiktokStreakSaver.Services.Storage;

namespace TiktokStreakSaver.Services;

[Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
public class AccountService
{
    private const string AccountsKey = "accounts_list";
    private const string CookieKeyPrefix = "acct_cookies_";
    private readonly IAppStorage _storage = AppStorageProvider.Current;

    public List<Account> GetAccounts()
    {
        var json = _storage.GetString(AccountsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json)) return new List<Account>();
        try
        {
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListAccount) ?? new List<Account>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetAccounts failed: {ex.Message}");
            return new List<Account>();
        }
    }

    private void SaveAccounts(List<Account> accounts) =>
        _storage.SetString(AccountsKey, JsonSerializer.Serialize(accounts, AppJsonContext.Default.ListAccount));

    public Account? Get(string? id) =>
        string.IsNullOrEmpty(id) ? null : GetAccounts().FirstOrDefault(a => a.Id == id);

    public Account Add(string? label)
    {
        var accounts = GetAccounts();
        var account = new Account
        {
            Label = string.IsNullOrWhiteSpace(label) ? $"Account {accounts.Count + 1}" : label.Trim()
        };
        accounts.Add(account);
        SaveAccounts(accounts);
        return account;
    }

    public void Update(Account account)
    {
        var accounts = GetAccounts();
        var i = accounts.FindIndex(a => a.Id == account.Id);
        if (i < 0) return;
        accounts[i] = account;
        SaveAccounts(accounts);
    }

    public void Remove(string id)
    {
        SaveAccounts(GetAccounts().Where(a => a.Id != id).ToList());
        DeleteCookies(id);
    }

    public void RemoveAll()
    {
        foreach (var a in GetAccounts()) DeleteCookies(a.Id);
        _storage.Remove(AccountsKey);
    }

    public async Task<string?> GetCookiesAsync(string id)
    {
        try { return await SecureStorage.Default.GetAsync(CookieKeyPrefix + id); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetCookiesAsync failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SaveCookiesAsync(string id, string cookieHeader)
    {
        try { await SecureStorage.Default.SetAsync(CookieKeyPrefix + id, cookieHeader); return true; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveCookiesAsync failed: {ex.Message}");
            return false;
        }
    }

    public void DeleteCookies(string id)
    {
        try { SecureStorage.Default.Remove(CookieKeyPrefix + id); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DeleteCookies failed: {ex.Message}"); }
    }

    /// <summary>Friends saved before multi-account support have no AccountId and belong to the first account.</summary>
    public static string? ResolveAccountId(FriendConfig friend, IReadOnlyList<Account> accounts) =>
        string.IsNullOrEmpty(friend.AccountId) ? accounts.FirstOrDefault()?.Id : friend.AccountId;
}

public static class AccountPlanner
{
    public sealed record Batch(Account Account, List<FriendConfig> Friends);

    public static List<Batch> BuildQueue(
        IReadOnlyList<Account> accounts,
        IEnumerable<FriendConfig> enabledFriends,
        DateTime today,
        out int alreadySent,
        out List<Account> needRelogin)
    {
        alreadySent = 0;
        var byAccount = new Dictionary<string, List<FriendConfig>>();
        foreach (var f in enabledFriends)
        {
            var id = AccountService.ResolveAccountId(f, accounts);
            if (id == null || accounts.All(a => a.Id != id)) continue;
            if (f.LastMessageSent?.Date == today) { alreadySent++; continue; }
            if (!byAccount.TryGetValue(id, out var list)) byAccount[id] = list = new List<FriendConfig>();
            list.Add(f);
        }

        var queue = new List<Batch>();
        needRelogin = new List<Account>();
        foreach (var a in accounts)
        {
            if (!byAccount.TryGetValue(a.Id, out var list)) continue;
            if (!a.SessionValid) { needRelogin.Add(a); continue; }
            queue.Add(new Batch(a, list));
        }
        return queue;
    }
}

public static class CookieHeader
{
    public static List<(string Name, string Value)> Parse(string? header)
    {
        var result = new List<(string Name, string Value)>();
        if (string.IsNullOrWhiteSpace(header)) return result;
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Trim();
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;
            result.Add((p[..eq], p[(eq + 1)..]));
        }
        return result;
    }
}

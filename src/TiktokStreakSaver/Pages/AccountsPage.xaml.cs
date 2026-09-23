using TiktokStreakSaver.Models;
using TiktokStreakSaver.Services;

namespace TiktokStreakSaver.Pages;

[Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
public partial class AccountsPage : ContentPage
{
    private readonly AccountService _accountService = new();
    private readonly SettingsService _settingsService = new();

    public AccountsPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.CompletedTask;
#if ANDROID
        await TiktokStreakSaver.Platforms.Android.Services.AccountCookieJar.EnsureMigratedAsync(_accountService, _settingsService);
#endif
        Refresh();
    }

    private void Refresh()
    {
        AccountsContainer.Children.Clear();
        var accounts = _accountService.GetAccounts();
        var friends = _settingsService.GetFriendsList();

        if (accounts.Count == 0)
        {
            AccountsContainer.Children.Add(new Label
            {
                Text = "No accounts yet. Tap 'Add account' to log in to TikTok.",
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 24)
            });
            return;
        }

        foreach (var account in accounts)
        {
            var count = friends.Count(f => AccountService.ResolveAccountId(f, accounts) == account.Id);
            AccountsContainer.Children.Add(BuildRow(account, count));
        }
    }

    private View BuildRow(Account account, int friendCount)
    {
        var status = account.SessionValid
            ? $"Session OK{(account.LastSnapshot.HasValue ? $" - refreshed {account.LastSnapshot.Value:MMM dd HH:mm}" : string.Empty)}"
            : "Needs login";

        var relogin = new Button { Text = account.SessionValid ? "Re-login" : "Login", HeightRequest = 40, FontSize = 12 };
        relogin.Clicked += async (_, _) => await Navigation.PushAsync(new LoginPage(account.Id));

        var rename = new Button { Text = "Rename", HeightRequest = 40, FontSize = 12, BackgroundColor = Colors.Transparent };
        rename.Clicked += async (_, _) =>
        {
            var name = await DisplayPromptAsync("Rename account", "Name", initialValue: account.Label);
            if (string.IsNullOrWhiteSpace(name)) return;
            account.Label = name.Trim();
            _accountService.Update(account);
            Refresh();
        };

        var remove = new Button { Text = "Remove", HeightRequest = 40, FontSize = 12, BackgroundColor = Colors.Transparent, TextColor = Colors.IndianRed };
        remove.Clicked += async (_, _) =>
        {
            var ok = await DisplayAlert("Remove account",
                $"Remove '{account.Label}', its saved login, and its {friendCount} friend(s)/group(s)?", "Remove", "Cancel");
            if (!ok) return;
            RemoveAccountAndFriends(account.Id);
            Refresh();
        };

        var buttons = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8
        };
        buttons.Add(relogin, 0);
        buttons.Add(rename, 1);
        buttons.Add(remove, 2);

        var stack = new VerticalStackLayout { Spacing = 6, Padding = 14 };
        stack.Children.Add(new Label { Text = account.Label, FontSize = 16, FontFamily = "InterSemiBold" });
        stack.Children.Add(new Label
        {
            Text = status,
            FontSize = 13,
            TextColor = account.SessionValid ? Colors.SeaGreen : Colors.IndianRed
        });
        stack.Children.Add(new Label { Text = $"{friendCount} friend(s)/group(s)", FontSize = 12 });
        stack.Children.Add(buttons);

        return new Border { Content = stack, StrokeThickness = 1, Stroke = Colors.Gray, Opacity = 1 };
    }

    private void RemoveAccountAndFriends(string accountId)
    {
        var accounts = _accountService.GetAccounts();
        var kept = _settingsService.GetFriendsList()
            .Where(f => AccountService.ResolveAccountId(f, accounts) != accountId)
            .ToList();
        _settingsService.SaveFriendsList(kept);
        _accountService.Remove(accountId);
    }

    private async void OnAddAccountClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (TiktokStreakSaver.Platforms.Android.Services.StreakService.IsRunning)
        {
            await DisplayAlert("Run in progress", "Wait for the current run to finish before adding an account.", "OK");
            return;
        }
#endif
        var suggested = $"Account {_accountService.GetAccounts().Count + 1}";
        var name = await DisplayPromptAsync("Add account", "Name for this account", "Continue", "Cancel", initialValue: suggested);
        if (name == null) return;

        var account = _accountService.Add(name);
        await Navigation.PushAsync(new LoginPage(account.Id));
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}

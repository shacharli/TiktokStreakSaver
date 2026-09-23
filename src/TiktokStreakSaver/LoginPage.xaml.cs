using TiktokStreakSaver.Services;
using TiktokStreakSaver.Services.Storage;

namespace TiktokStreakSaver;

[Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
public partial class LoginPage : ContentPage
{
    private readonly SessionService _sessionService;
    private readonly SettingsService _settingsService;
    private bool _isLoggedIn;
    private bool _webViewTornDown;
    private bool _completionInProgress;
    private readonly AccountService _accountService = new();
    private readonly string _accountId;
    private bool _cookiesPrepared;

    public LoginPage() : this(null)
    {
    }

    public LoginPage(string? accountId)
    {
        InitializeComponent();
        _sessionService = new SessionService();
        _settingsService = new SettingsService();
        _accountId = accountId
            ?? _accountService.GetAccounts().FirstOrDefault()?.Id
            ?? _accountService.Add("Account 1").Id;
    }

    private async Task AppearingCore()
    {
        _webViewTornDown = false;
        _isLoggedIn = false;
        _completionInProgress = false;
#if ANDROID
        // Keep any pre-multi-account session, then start from a logged-out jar so this login is for this account only.
        // Only on first appearance: clearing again after returning to the page would wipe a login in progress.
        if (!_cookiesPrepared)
        {
            _cookiesPrepared = true;
            await TiktokStreakSaver.Platforms.Android.Services.AccountCookieJar.EnsureMigratedAsync(_accountService, _settingsService);
            await TiktokStreakSaver.Platforms.Android.Services.AccountCookieJar.ClearAsync();
        }
#endif
        LoadTikTok();
        await TryCompleteExistingSessionAsync();
    }

    // Any exception escaping an async void handler kills the whole app, so log it and show it instead.
    private async Task Guard(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
#if ANDROID
            TiktokStreakSaver.Platforms.Android.CrashLog.Write("LoginPage", ex);
#endif
            _completionInProgress = false;
            try { await DisplayAlert("Login problem", ex.Message, "OK"); } catch { }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Guard(AppearingCore);
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e) =>
        await Guard(() => NavigatedCore(e));

    private Task Done(bool showSuccessAlert = true) => Guard(() => DoneCore(showSuccessAlert));

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        TearDownLoginWebView();
    }

    private void LoadTikTok()
    {
        LoadingOverlay.IsVisible = true;

        var desktopUa = AppConstants.DesktopChromeUserAgent;
#if ANDROID || IOS
        TikTokWebViewHelper.ConfigureWebView(TikTokWebView, desktopUa);
        _sessionService.SetLoginUserAgent(desktopUa);
#endif

        TikTokWebView.Source = TikTokWebViewHelper.LoginUrl;
    }

    private async Task TryCompleteExistingSessionAsync()
    {
        await Task.Delay(800);
        if (_isLoggedIn || _completionInProgress)
            return;

#if IOS
        Platforms.iOS.Services.IosWebViewConfigurator.AttachWebView(TikTokWebView);
        if (!await TikTokWebViewHelper.HasValidSessionCookieAsync(TikTokWebView))
            return;

        _isLoggedIn = true;
        await Platforms.iOS.Services.IosWebViewConfigurator.ExportCookiesFromCurrentWebViewAsync();
        await SessionRefreshHelper.RefreshAndGetRunReadyAsync(_sessionService);
        await Done(showSuccessAlert: false);
#elif ANDROID
        if (!TikTokWebViewHelper.HasValidSessionCookie())
            return;

        _isLoggedIn = true;
        await Done(showSuccessAlert: false);
#endif
    }

    private async Task NavigatedCore(WebNavigatedEventArgs e)
    {
        if (_isLoggedIn || _completionInProgress) return;
        LoadingOverlay.IsVisible = false;

        bool hasSession;
#if IOS
        Platforms.iOS.Services.IosWebViewConfigurator.AttachWebView(TikTokWebView);
        hasSession = await TikTokWebViewHelper.HasValidSessionCookieAsync(TikTokWebView);
        if (!hasSession && TikTokWebViewHelper.CheckLoginStatus(e.Url).IsLoggedIn)
        {
            await Task.Delay(400);
            hasSession = await TikTokWebViewHelper.HasValidSessionCookieAsync(TikTokWebView);
        }
#else
        hasSession = TikTokWebViewHelper.HasValidSessionCookie();
#endif

        if (!hasSession)
            return;

        _isLoggedIn = true;
#if IOS
        await Platforms.iOS.Services.IosWebViewConfigurator.ExportCookiesFromCurrentWebViewAsync();
#endif
        await Done();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (TikTokWebView.CanGoBack)
            TikTokWebView.GoBack();
        else
            await Navigation.PopAsync();
    }

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        LoadingOverlay.IsVisible = true;
        TikTokWebView.Reload();
    }

    private void TearDownLoginWebView()
    {
        if (_webViewTornDown)
            return;
        _webViewTornDown = true;
        TikTokWebViewHelper.TearDownLoginWebView(TikTokWebView);
    }

    private async Task DoneCore(bool showSuccessAlert)
    {
        if (_completionInProgress)
            return;
        _completionInProgress = true;

        if (_isLoggedIn)
        {
            // Persist everything before tearing the WebView down, so nothing is lost if teardown misbehaves.
            if (!_sessionService.TrySetSessionValid(true, out var persistError))
            {
                _completionInProgress = false;
                await DisplayAlert("Could Not Save Session",
                    persistError ?? "Login succeeded but session state did not persist on this device.", "OK");
                return;
            }

#if ANDROID
            var account = _accountService.Get(_accountId);
            if (account == null
                || !await TiktokStreakSaver.Platforms.Android.Services.AccountCookieJar.SnapshotAsync(_accountService, _accountId))
            {
                _completionInProgress = false;
                await DisplayAlert("Could Not Save Session",
                    "Login worked, but the session could not be stored securely on this device.", "OK");
                return;
            }
            account.UserAgent = AppConstants.DesktopChromeUserAgent;
            account.SessionValid = true;
            account.LastSnapshot = DateTime.Now;
            _accountService.Update(account);
#endif

            TearDownLoginWebView();

            AppStorageProvider.Current.SetBool(AppConstants.AuthRequiredKey, false);

            bool justEnabled = false;
            if (!_settingsService.IsScheduled())
            {
#if ANDROID
                var context = Platform.CurrentActivity ?? Android.App.Application.Context;
                TiktokStreakSaver.Platforms.Android.StreakScheduler.ScheduleNextRun(context);
#endif
                justEnabled = true;
            }

            SessionState.NotifyChanged();

            if (showSuccessAlert)
            {
                var body = justEnabled
#if IOS
                    ? "You're logged in to TikTok! Set up a daily Shortcut (see Profile) to run streaks automatically."
#else
                    ? "You're logged in to TikTok! Background automation has been enabled — your streaks will run on the schedule set in Profile."
#endif
                    : "You're logged in to TikTok! The app will use this session for streak messaging.";
                await DisplayAlert("Logged In", body, "OK");
            }

            await Navigation.PopAsync();
        }
        else
        {
            _sessionService.TrySetSessionValid(false, out _);
            await DisplayAlert("Not Logged In",
                "Please login to TikTok first before continuing.", "OK");
            _completionInProgress = false;
        }
    }
}

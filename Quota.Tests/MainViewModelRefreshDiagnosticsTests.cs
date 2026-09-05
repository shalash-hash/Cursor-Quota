using System.Net.Http;
using Microsoft.Data.Sqlite;
using Quota.Localization;
using Quota.Models;
using Quota.Services;
using Quota.ViewModels;
using Xunit;

namespace Quota.Tests;

public sealed class MainViewModelRefreshDiagnosticsTests : IDisposable
{
    private readonly string _databasePath;

    public MainViewModelRefreshDiagnosticsTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"quota-vm-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task RefreshAsync_FinalFailure_ExposesFailureTimeAndReason()
    {
        var provider = new StubQuotaProvider(() =>
            Task.FromException<QuotaUsage>(
                new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage")));
        var viewModel = CreateViewModel(provider);

        await viewModel.RefreshAsync(RefreshSource.Manual);

        Assert.True(viewModel.HasRefreshFailure);
        Assert.Contains("403", viewModel.LastRefreshFailureReasonText, StringComparison.Ordinal);
        Assert.Contains("Forbidden", viewModel.LastRefreshFailureReasonText, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.LastRefreshFailureText));
    }

    [Fact]
    public async Task RefreshAsync_NetworkFailureThenSuccess_ClearsFailureDiagnostic()
    {
        var attempts = 0;
        var provider = new StubQuotaProvider(() =>
        {
            attempts++;
            if (attempts == 1)
                return Task.FromException<QuotaUsage>(new HttpRequestException("dns failure"));

            return Task.FromResult(CreateUsage());
        });
        var viewModel = CreateViewModel(provider);

        await viewModel.RefreshAsync(RefreshSource.Manual);
        Assert.True(viewModel.HasRefreshFailure);

        await viewModel.RefreshAsync(RefreshSource.Manual);

        Assert.False(viewModel.HasRefreshFailure);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.LastUpdateText));
    }

    [Fact]
    public async Task RefreshAsync_FailureAfterSuccess_KeepsLastSuccessfulData()
    {
        var attempts = 0;
        var provider = new StubQuotaProvider(() =>
        {
            attempts++;
            if (attempts == 1)
                return Task.FromResult(CreateUsage(total: 42));

            return Task.FromException<QuotaUsage>(
                new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage"));
        });
        var viewModel = CreateViewModel(provider);

        await viewModel.RefreshAsync(RefreshSource.Manual);
        var successfulPercent = viewModel.TotalUsedPercentText;

        await viewModel.RefreshAsync(RefreshSource.Manual);

        Assert.True(viewModel.HasRefreshFailure);
        Assert.Equal(successfulPercent, viewModel.TotalUsedPercentText);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.LastUpdateText));
    }

    [Fact]
    public async Task RefreshAsync_FailureReason_DoesNotContainAuthorizationSecrets()
    {
        var provider = new StubQuotaProvider(() =>
            Task.FromException<QuotaUsage>(
                new CursorQuotaFetchException(
                    "Bearer super-secret-token",
                    403,
                    "Forbidden",
                    "usage")));
        var viewModel = CreateViewModel(provider);

        await viewModel.RefreshAsync(RefreshSource.Manual);

        Assert.DoesNotContain("Bearer", viewModel.LastRefreshFailureReasonText, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", viewModel.LastRefreshFailureReasonText, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private MainViewModel CreateViewModel(IQuotaUsageProvider provider)
    {
        var uiSettingsService = new UiSettingsService();
        var localization = new LocalizationService(uiSettingsService);
        var snapshotRepository = new QuotaSnapshotRepository(_databasePath);
        var usageHistoryService = new UsageHistoryService(snapshotRepository);

        return new MainViewModel(
            provider,
            new QuotaCalculator(),
            new StartupService(),
            snapshotRepository,
            usageHistoryService,
            new QuotaDiagnosticLogger(),
            uiSettingsService,
            new ThemeService(uiSettingsService),
            localization);
    }

    private static QuotaUsage CreateUsage(double total = 25) =>
        new()
        {
            PeriodStart = new DateTime(2026, 8, 6, 12, 0, 0),
            PeriodEnd = new DateTime(2026, 9, 6, 12, 0, 0),
            RetrievedAt = DateTime.Now,
            TotalUsedPercent = total,
            FirstPartyUsedPercent = total,
            ApiUsedPercent = 0,
            TotalSpendCents = 5000,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

    private sealed class StubQuotaProvider(Func<Task<QuotaUsage>> factory) : IQuotaUsageProvider
    {
        public Task<QuotaUsage> GetUsageAsync() => factory();
    }
}

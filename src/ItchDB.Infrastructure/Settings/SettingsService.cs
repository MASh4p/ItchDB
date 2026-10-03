using System.Text.Json;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Settings;

public sealed class SettingsService(IDbContextFactory<ItchDbContext> dbFactory, IDataProtectionProvider protection)
{
    private const string SettingsKey = "settings";
    private const string ApiKeyKey = "secret:itch_api_key";
    private const string CfClearanceKey = "secret:cf_clearance";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly IDataProtector _protector = protection.CreateProtector("ItchDB.Secrets.v1");
    private AppSettingsModel? _cached;

    public event Action? Changed;

    public AppSettingsModel Current => _cached ?? Load();

    public AppSettingsModel Load()
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.AppSettings.AsNoTracking().FirstOrDefault(x => x.Key == SettingsKey);
        _cached = row is null ? new AppSettingsModel() : Deserialize(row.Value);
        return _cached;
    }

    public async Task SaveAsync(AppSettingsModel model)
    {
        await SetRawAsync(SettingsKey, JsonSerializer.Serialize(model, Json));
        _cached = Deserialize(JsonSerializer.Serialize(model, Json));
        Changed?.Invoke();
    }

    public AppSettingsModel Clone() => Deserialize(JsonSerializer.Serialize(Current, Json));

    public Task<string?> GetApiKeyAsync() => GetSecretAsync(ApiKeyKey);
    public Task SetApiKeyAsync(string? value) => SetSecretAsync(ApiKeyKey, value);
    public Task<string?> GetCfClearanceAsync() => GetSecretAsync(CfClearanceKey);
    public Task SetCfClearanceAsync(string? value) => SetSecretAsync(CfClearanceKey, value);

    public async Task<string?> GetRawAsync(string key)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key))?.Value;
    }

    public async Task SetRawAsync(string key, string? value)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key);
        if (value is null)
        {
            if (row is not null) db.AppSettings.Remove(row);
        }
        else if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
        await db.SaveChangesAsync();
    }

    private async Task<string?> GetSecretAsync(string key)
    {
        var raw = await GetRawAsync(key);
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            return _protector.Unprotect(raw);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private Task SetSecretAsync(string key, string? value) =>
        SetRawAsync(key, string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value.Trim()));

    private static AppSettingsModel Deserialize(string json)
    {
        try
        {
            var model = JsonSerializer.Deserialize<AppSettingsModel>(json, Json) ?? new AppSettingsModel();
            if (model.GamePageIntervalMs <= 0) model.GamePageIntervalMs = 12_000;
            if (model.ParallelRequests <= 0) model.ParallelRequests = 4;
            if (model.ParallelRequests > 8) model.ParallelRequests = 8;
            return model;
        }
        catch (JsonException)
        {
            return new AppSettingsModel();
        }
    }
}

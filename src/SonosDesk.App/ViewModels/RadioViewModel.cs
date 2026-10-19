using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.ViewModels;

public sealed partial class RadioViewModel : ViewModelBase
{
    private readonly IRadioDirectory _directory;
    private readonly ISonosControlService _control;
    private readonly ISonosStateService _state;
    private readonly ISonosConfigurationService _configuration;
    private readonly IUiDispatcher _dispatcher;
    private readonly StationLogoService _logos;
    private readonly IAppShell _shell;
    private readonly IRadioLogoResolver _logoResolver;
    private readonly IStreamUrlResolver _streamUrls;
    private bool _initialized;
    private bool _refreshingCountries;
    private Func<string>? _header;
    private string? _emptyKey;

    public RadioViewModel(
        IRadioDirectory directory,
        ISonosControlService control,
        ISonosStateService state,
        ISonosConfigurationService configuration,
        IUiDispatcher dispatcher,
        StationLogoService logos,
        IAppShell shell,
        IRadioLogoResolver logoResolver,
        IStreamUrlResolver streamUrls,
        UserNotifier notifier,
        ILogger<RadioViewModel> logger)
        : base(notifier, logger)
    {
        _streamUrls = streamUrls;
        _logoResolver = logoResolver;
        _logos = logos;
        _shell = shell;
        _directory = directory;
        _control = control;
        _state = state;
        _configuration = configuration;
        _dispatcher = dispatcher;
        Countries = BuildCountries();
        var configured = configuration.Current.RadioCountryCode ?? RegionCode();
        SelectedCountry = Countries.FirstOrDefault(c => c.Value == configured) ?? Countries[0];
        LoadSaved(configuration.Current);
        configuration.SettingsChanged += (_, s) => _dispatcher.Post(() => LoadSaved(s));
    }

    public string ProviderName => _directory.ProviderName;

    public bool CanPlayStreams => _control.Supports(SonosCapabilities.PlayStreamUrl);

    [ObservableProperty]
    public partial IReadOnlyList<Option<string?>> Countries { get; private set; } = [];

    public IReadOnlyList<string> Genres { get; } = ["pop", "rock", "news", "jazz", "classical", "electronic", "hiphop", "schlager", "80s", "chillout"];

    public ObservableCollection<RadioStationViewModel> Results { get; } = [];

    public ObservableCollection<RadioStationViewModel> SavedStations { get; } = [];

    // When a receiver is selected in the room list, stations play there instead of on a Sonos room.
    public ReceiverViewModel? ReceiverTarget { get; set; }

    public ObservableCollection<StationFolderViewModel> SavedFolders { get; } = [];

    public IReadOnlyList<string> FolderNames =>
        _configuration.Current.RadioStations.Select(s => s.Folder).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetText))]
    public partial GroupViewModel? TargetGroup { get; set; }

    [ObservableProperty]
    public partial string? TargetText { get; set; }

    public Func<CancellationToken, Task<GroupViewModel?>>? TargetResolver { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial Option<string?>? SelectedCountry { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial string ResultsHeader { get; private set; } = L.T("radio.popular");

    [ObservableProperty]
    public partial bool HasSaved { get; private set; }

    [ObservableProperty]
    public partial string? EmptyText { get; private set; }

    public void EnsureLoaded()
    {
        if (!_initialized)
        {
            _initialized = true;
            _ = LoadPopularAsync();
        }
    }

    public void RefreshLocalizedTexts()
    {
        var selected = SelectedCountry?.Value;
        _refreshingCountries = true;
        try
        {
            Countries = BuildCountries();
            SelectedCountry = Countries.FirstOrDefault(c => c.Value == selected) ?? Countries[0];
        }
        finally
        {
            _refreshingCountries = false;
        }

        LoadSaved(_configuration.Current);
        foreach (var result in Results)
        {
            result.RefreshLocalizedTexts();
        }

        ResultsHeader = _header?.Invoke() ?? L.T("radio.popular");
        EmptyText = _emptyKey is null ? null : L.T(_emptyKey);
    }

    partial void OnSelectedCountryChanged(Option<string?>? value)
    {
        if (!_initialized || _refreshingCountries)
        {
            return;
        }

        _ = _configuration.UpdateAsync(s => s with { RadioCountryCode = value?.Value });
        _ = string.IsNullOrWhiteSpace(SearchText) ? LoadPopularAsync() : SearchAsync();
    }

    [RelayCommand]
    private Task LoadPopularAsync()
    {
        SearchText = null;
        var country = SelectedCountry?.Value;
        return LoadAsync(
            ct => _directory.GetPopularAsync(country, 60, ct),
            () => PopularHeader(country));
    }

    private string PopularHeader(string? country) =>
        country is null
            ? L.T("radio.popular_world")
            : L.F("radio.popular_country", Countries.FirstOrDefault(c => c.Value == country)?.Label ?? country);

    [RelayCommand]
    private Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return LoadPopularAsync();
        }

        var search = new RadioSearch { Name = SearchText.Trim(), CountryCode = SelectedCountry?.Value, Limit = 80 };
        return LoadAsync(ct => _directory.SearchAsync(search, ct), () => L.F("radio.results_for", search.Name));
    }

    [RelayCommand]
    private Task SearchGenreAsync(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return Task.CompletedTask;
        }

        SearchText = null;
        var search = new RadioSearch { Tag = genre, CountryCode = SelectedCountry?.Value, Limit = 80 };
        return LoadAsync(ct => _directory.SearchAsync(search, ct), () => L.F("radio.genre", genre));
    }

    [RelayCommand]
    private Task PlayAsync(RadioStationViewModel? station)
    {
        return station is null ? Task.CompletedTask : PlayStationAsync(station);
    }

    private async Task PlayStationAsync(RadioStationViewModel station)
    {
        if (ReceiverTarget is { } receiver)
        {
            await receiver.PlayStationAsync(station.Station);
            return;
        }

        GroupViewModel? group = null;
        var ok = await RunAsync(async ct =>
        {
            group = TargetResolver is { } resolve ? await resolve(ct) : TargetGroup;
            if (group is null)
            {
                throw new SonosException(SonosErrorKind.NotFound, L.T("radio.select_room_first"));
            }

            group.MarkStarting();
            await _control.Playback.PlayStreamAsync(group.Id, station.Station.StreamUrl, station.Name, station.LogoUrl, ct);
            _state.RequestGroupRefresh(group.Id);
            _ = _directory.RegisterPlayAsync(station.Station.Id);
        });
        if (!ok)
        {
            group?.CancelStarting();
        }
    }

    [RelayCommand]
    private Task ToggleSaveAsync(RadioStationViewModel? station)
    {
        if (station is null)
        {
            return Task.CompletedTask;
        }

        var saved = !station.IsSaved;
        var oldLogo = _configuration.Current.RadioStations.FirstOrDefault(r => r.Id == station.Station.Id)?.LogoFile;
        return RunAsync(async ct =>
        {
            await _configuration.UpdateAsync(
                s => s with
                {
                    RadioStations = saved
                        ? [.. s.RadioStations.Where(r => r.Id != station.Station.Id), ToSaved(station.Station)]
                        : [.. s.RadioStations.Where(r => r.Id != station.Station.Id)],
                },
                ct);
            if (!saved)
            {
                _logos.Delete(oldLogo);
            }

            Notifier.Show(saved ? L.F("radio.added_saved", station.Name) : L.F("radio.removed", station.Name), NotificationSeverity.Success);
        });
    }

    // Deletes a station from "My stations" (with its custom logo) after asking.
    [RelayCommand]
    private Task DeleteSavedAsync(RadioStationViewModel? station)
    {
        if (station is null || !station.IsSaved || !_shell.Confirm(L.F("radio.delete_confirm", station.Name)))
        {
            return Task.CompletedTask;
        }

        var logo = _configuration.Current.RadioStations.FirstOrDefault(r => r.Id == station.Station.Id)?.LogoFile;
        return RunAsync(async ct =>
        {
            await _configuration.UpdateAsync(s => s with { RadioStations = [.. s.RadioStations.Where(r => r.Id != station.Station.Id)] }, ct);
            _logos.Delete(logo);
            Notifier.Show(L.F("radio.removed", station.Name), NotificationSeverity.Success);
        });
    }

    [RelayCommand]
    private async Task ChangeLogoAsync(RadioStationViewModel? station)
    {
        if (station is null || _shell.PickImageFile() is not { } path)
        {
            return;
        }

        await RunAsync(
            async ct =>
            {
                var file = await _logos.ImportAsync(path, ct);
                string? previous = null;
                await _configuration.UpdateAsync(
                    s =>
                    {
                        var existing = s.RadioStations.FirstOrDefault(r => r.Id == station.Station.Id);
                        previous = existing?.LogoFile;
                        var updated = (existing ?? ToSaved(station.Station)) with { LogoFile = file };
                        return s with { RadioStations = existing is null ? [.. s.RadioStations, updated] : [.. s.RadioStations.Select(r => r.Id == updated.Id ? updated : r)] };
                    },
                    ct);
                _logos.Delete(previous);
            },
            L.F("radio.logo_saved", station.Name));
    }

    [RelayCommand]
    private Task ResetLogoAsync(RadioStationViewModel? station)
    {
        var existing = station is null ? null : _configuration.Current.RadioStations.FirstOrDefault(r => r.Id == station.Station.Id);
        if (existing?.LogoFile is not { } file)
        {
            return Task.CompletedTask;
        }

        return RunAsync(
            async ct =>
            {
                await _configuration.UpdateAsync(
                    s => s with { RadioStations = [.. s.RadioStations.Select(r => r.Id == existing.Id ? r with { LogoFile = null } : r)] },
                    ct);
                _logos.Delete(file);
            },
            L.F("radio.logo_reset", existing.Name));
    }

    private async Task LoadAsync(Func<CancellationToken, Task<IReadOnlyList<RadioStation>>> load, Func<string> header)
    {
        IsLoading = true;
        _emptyKey = null;
        EmptyText = null;
        _header = header;
        ResultsHeader = header();
        IReadOnlyList<RadioStation> stations = [];
        var ok = await RunAsync(async ct => stations = await load(ct));
        Results.Clear();
        foreach (var station in stations)
        {
            Results.Add(CreateItem(station));
        }

        _emptyKey = !ok ? "radio.directory_unavailable" : Results.Count == 0 ? "radio.no_results" : null;
        EmptyText = _emptyKey is null ? null : L.T(_emptyKey);
        IsLoading = false;
    }

    private void LoadSaved(AppSettings settings)
    {
        var ids = settings.RadioStations.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        SavedStations.Clear();
        foreach (var saved in settings.RadioStations)
        {
            if (FromSaved(saved) is { } station)
            {
                SavedStations.Add(CreateItem(station));
            }
        }

        foreach (var result in Results)
        {
            result.IsSaved = ids.Contains(result.Station.Id);
            ApplyLogo(result, settings.RadioStations.FirstOrDefault(s => s.Id == result.Station.Id)?.LogoFile);
        }

        SavedFolders.Clear();
        foreach (var folder in SavedStations
                     .GroupBy(s => s.Folder ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(g => g.Key.Length == 0 ? 0 : 1)
                     .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            SavedFolders.Add(new StationFolderViewModel(folder.Key.Length == 0 ? null : folder.Key, [.. folder.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)]));
        }

        HasSaved = SavedStations.Count > 0;
        OnPropertyChanged(nameof(FolderNames));
    }

    public Task AddCustomStationAsync(string name, string url, string? folder) => RunAsync(
        async ct =>
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var entered))
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("radio.invalid_url"));
            }

            var stream = await _streamUrls.ResolveAsync(entered, ct);
            var station = new SavedRadioStation
            {
                Id = "custom-" + Guid.NewGuid().ToString("N"),
                Name = name.Trim(),
                StreamUrl = stream.AbsoluteUri,
                Folder = SettingsNormalizer.NormalizeFolder(folder),
                IsCustom = true,
            };
            await _configuration.UpdateAsync(s => s with { RadioStations = [.. s.RadioStations, station] }, ct);
        },
        L.F("radio.custom_added", name.Trim()));

    public Task MoveToFolderAsync(RadioStationViewModel station, string? folder)
    {
        var normalized = SettingsNormalizer.NormalizeFolder(folder);
        return RunAsync(
            ct => _configuration.UpdateAsync(
                s =>
                {
                    var existing = s.RadioStations.FirstOrDefault(r => r.Id == station.Station.Id);
                    var updated = (existing ?? ToSaved(station.Station)) with { Folder = normalized };
                    return s with { RadioStations = existing is null ? [.. s.RadioStations, updated] : [.. s.RadioStations.Select(r => r.Id == updated.Id ? updated : r)] };
                },
                ct),
            normalized is null ? L.F("radio.moved_none", station.Name) : L.F("radio.moved_to", station.Name, normalized));
    }

    // Renames a station in "My stations"; the new name is also what the Sonos app and the receiver display show.
    public Task RenameStationAsync(RadioStationViewModel station, string newName)
    {
        var name = newName.Trim();
        if (name.Length == 0 || name == station.Name || !station.IsSaved)
        {
            return Task.CompletedTask;
        }

        if (name.Length > 80)
        {
            name = name[..80];
        }

        return RunAsync(
            ct => _configuration.UpdateAsync(
                s => s with { RadioStations = [.. s.RadioStations.Select(r => r.Id == station.Station.Id ? r with { Name = name } : r)] },
                ct),
            L.F("radio.renamed", name));
    }

    public Task RenameFolderAsync(string folder, string? newName)
    {
        var normalized = SettingsNormalizer.NormalizeFolder(newName);
        return RunAsync(ct => _configuration.UpdateAsync(
            s => s with
            {
                RadioStations = [.. s.RadioStations.Select(r => string.Equals(r.Folder, folder, StringComparison.CurrentCultureIgnoreCase) ? r with { Folder = normalized } : r)],
            },
            ct));
    }

    private RadioStationViewModel CreateItem(RadioStation station)
    {
        var saved = _configuration.Current.RadioStations.FirstOrDefault(s => s.Id == station.Id);
        var item = new RadioStationViewModel(station, saved is not null) { Folder = saved?.Folder, IsCustom = saved?.IsCustom ?? false };
        ApplyLogo(item, saved?.LogoFile);
        return item;
    }

    private void ApplyLogo(RadioStationViewModel item, string? logoFile)
    {
        if (_logos.Load(logoFile) is { } custom)
        {
            item.Logo = custom;
            item.HasCustomLogo = true;
            return;
        }

        if (item.HasCustomLogo || item.Logo is null)
        {
            item.HasCustomLogo = false;
            item.Logo = null;
            _ = LoadLogoAsync(item);
        }
    }

    private async Task LoadLogoAsync(RadioStationViewModel item)
    {
        if (item.Station.FaviconUrl is { } favicon && await LoadFaviconAsync(item, favicon))
        {
            return;
        }

        try
        {
            if (await _logoResolver.FindLogoAsync(item.Name, item.Station.Homepage) is { } found)
            {
                await LoadFaviconAsync(item, found);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug("Logo lookup failed: {Message}", ex.Message);
        }
    }

    private async Task<bool> LoadFaviconAsync(RadioStationViewModel item, Uri favicon)
    {
        var image = await ImageLoader.LoadAsync(_control.AlbumArt, favicon, 96).ConfigureAwait(false);
        if (image is null)
        {
            return false;
        }

        _dispatcher.Post(() =>
        {
            item.LogoUrl = favicon;
            if (!item.HasCustomLogo)
            {
                item.Logo = image;
            }
        });
        return true;
    }

    internal static SavedRadioStation ToSaved(RadioStation station) => new()
    {
        Id = station.Id,
        Name = station.Name,
        StreamUrl = station.StreamUrl.AbsoluteUri,
        FaviconUrl = station.FaviconUrl?.AbsoluteUri,
        CountryCode = station.CountryCode,
    };

    internal static RadioStation? FromSaved(SavedRadioStation saved) =>
        Uri.TryCreate(saved.StreamUrl, UriKind.Absolute, out var stream)
            ? new RadioStation
            {
                Id = saved.Id,
                Name = saved.Name,
                StreamUrl = stream,
                FaviconUrl = Uri.TryCreate(saved.FaviconUrl, UriKind.Absolute, out var icon) ? icon : null,
                CountryCode = saved.CountryCode,
            }
            : null;

    private static string RegionCode()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return "DE";
        }
    }

    internal static List<Option<string?>> BuildCountries()
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                var code = region.TwoLetterISORegionName.ToUpperInvariant();
                if (code.Length == 2 && code.All(char.IsAsciiLetterUpper) && !regions.ContainsKey(code))
                {
                    regions[code] = region.DisplayName;
                }
            }
            catch (ArgumentException)
            {
                // Culture without a region (should not happen for specific cultures).
            }
        }

        var comparer = StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true);
        var list = new List<Option<string?>> { new(null, L.T("radio.all_countries")) };
        list.AddRange(regions
            .OrderBy(r => r.Value, comparer)
            .Select(r => new Option<string?>(r.Key, r.Value)));
        return list;
    }
}

public sealed class StationFolderViewModel(string? name, IReadOnlyList<RadioStationViewModel> stations)
{
    public string? Name { get; } = name;

    public string Header => Name ?? L.T("radio.no_folder");

    public bool HasName => Name is not null;

    public IReadOnlyList<RadioStationViewModel> Stations { get; } = stations;
}

public sealed partial class RadioStationViewModel(RadioStation station, bool isSaved) : ObservableObject
{
    public RadioStation Station { get; } = station;

    public string Name => Station.Name;

    public string Details => string.Join(" · ", new[]
    {
        IsCustom ? L.T("radio.custom_stream") : null,
        Station.Codec,
        Station.Bitrate is { } b ? L.F("radio.bitrate", b) : null,
        Station.Tags.Count > 0 ? string.Join(", ", Station.Tags.Take(3)) : null,
    }.Where(p => !string.IsNullOrWhiteSpace(p)));

    [ObservableProperty]
    public partial ImageSource? Logo { get; set; }

    [ObservableProperty]
    public partial bool IsSaved { get; set; } = isSaved;

    [ObservableProperty]
    public partial bool HasCustomLogo { get; set; }

    public string? Folder { get; init; }

    public bool IsCustom { get; init; }

    public Uri? LogoUrl { get; set; } = station.FaviconUrl;

    public void RefreshLocalizedTexts() => OnPropertyChanged(nameof(Details));
}

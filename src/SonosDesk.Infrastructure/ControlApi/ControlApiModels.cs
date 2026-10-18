using System.Text.Json.Serialization;

namespace SonosDesk.Infrastructure.ControlApi;

// DTOs for the documented subset of the Sonos Control API v1
// (https://docs.sonos.com/reference). Only fields used by SonosDesk are mapped.

internal sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("scope")] string? Scope);

public sealed record ControlApiTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

internal sealed record HouseholdsResponse(IReadOnlyList<HouseholdDto>? Households);

internal sealed record HouseholdDto(string Id, string? Name);

internal sealed record GroupsResponse(IReadOnlyList<GroupDto>? Groups, IReadOnlyList<PlayerDto>? Players, bool? Partial);

internal sealed record GroupDto(string Id, string? Name, string CoordinatorId, string? PlaybackState, IReadOnlyList<string>? PlayerIds);

internal sealed record PlayerDto(string Id, string? Name, string? SoftwareVersion);

internal sealed record GroupInfoResponse(GroupDto? Group);

internal sealed record PlaybackStatusDto(
    string? PlaybackState,
    int? PositionMillis,
    PlayModesDto? PlayModes,
    AvailablePlaybackActionsDto? AvailablePlaybackActions);

internal sealed record PlayModesDto(bool? Repeat, bool? RepeatOne, bool? Shuffle, bool? Crossfade);

internal sealed record AvailablePlaybackActionsDto(bool? CanPlay, bool? CanPause, bool? CanSkip, bool? CanSkipToPrevious, bool? CanRepeat, bool? CanShuffle);

internal sealed record MetadataStatusDto(ContainerDto? Container, ItemDto? CurrentItem, string? StreamInfo);

internal sealed record ContainerDto(string? Name, string? Type, ServiceDto? Service, string? ImageUrl);

internal sealed record ServiceDto(string? Name, string? Id);

internal sealed record ItemDto(TrackDto? Track);

internal sealed record TrackDto(string? Name, NamedDto? Artist, NamedDto? Album, string? ImageUrl, int? DurationMillis, ServiceDto? Service);

internal sealed record NamedDto(string? Name);

internal sealed record VolumeDto(int Volume, bool? Muted, bool? Fixed);

internal sealed record GlobalErrorDto(string? ErrorCode, string? Reason);

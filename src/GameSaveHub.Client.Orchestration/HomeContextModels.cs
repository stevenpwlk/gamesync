using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public sealed record HomeContextSnapshot(
    bool IsEnrolled,
    Guid? DeviceId,
    string? PlayerName,
    bool ServerHealthy,
    WorldCatalogItemResponse? PrimaryWorld,
    WorldStatusResponse? WorldStatus,
    string? SafetyStopCode,
    TransferSession? LocalSession,
    TransferSession? LastFinishedSession,
    bool GameRunning,
    bool WgsStable,
    bool WgsAvailable = true);

public enum HomeVisualState
{
    Onboarding,
    Unavailable,
    SafetyStop,
    Ready,
    Preparing,
    Placeholder,
    ReadyToPlay,
    Hosting,
    RemotePreparing,
    RemoteHosting,
    Securing,
    OffHub,
    Interrupted,
    ManualReview
}

public enum HomePrimaryAction
{
    None,
    StartTransfer,
    LaunchGame,
    ResumeTransfer,
    OpenDiagnostics
}

public sealed record HomeViewState(
    HomeVisualState State,
    string Title,
    string Instruction,
    HomePrimaryAction PrimaryAction,
    string? PrimaryActionLabel,
    int ProgressStep,
    bool IsProgressIndeterminate);

public sealed record PrimaryWorldSelection(
    bool Success,
    string Code,
    WorldCatalogItemResponse? World);

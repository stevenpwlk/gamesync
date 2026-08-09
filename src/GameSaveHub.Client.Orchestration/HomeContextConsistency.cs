using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public static class HomeContextConsistency
{
    public static string? Validate(
        Guid localDeviceId,
        WorldStatusResponse? worldStatus,
        TransferSession? localSession)
    {
        var remote = worldStatus?.ActiveSession;
        if (localSession is null)
            return remote?.DeviceId == localDeviceId ? "local_checkpoint_missing" : null;

        if (remote is null)
            return localSession.ServerSessionId is null ? null : "server_session_missing";
        if (localSession.ServerSessionId != remote.SessionId)
            return "session_checkpoint_mismatch";
        if (remote.DeviceId != localDeviceId)
            return "session_owner_mismatch";
        return null;
    }
}

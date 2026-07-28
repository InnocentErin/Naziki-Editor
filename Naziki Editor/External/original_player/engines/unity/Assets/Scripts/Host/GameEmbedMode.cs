public enum GameEmbedModeKind
{
    StandaloneDebug,
    BridgeEmbedded
}

public static class GameEmbedMode
{
#if CYTOID_FLUTTER_HOST || CYTOID_EDITOR_HOST
    private const bool CompileTimeBridgeEmbedded = true;
#else
    private const bool CompileTimeBridgeEmbedded = false;
#endif

    public static GameEmbedModeKind Current =>
        CompileTimeBridgeEmbedded
            ? GameEmbedModeKind.BridgeEmbedded
            : GameEmbedModeKind.StandaloneDebug;

    public static bool IsBridgeEmbedded => Current == GameEmbedModeKind.BridgeEmbedded;

    public static bool IsStandaloneDebug => Current == GameEmbedModeKind.StandaloneDebug;

#if CYTOID_EDITOR_HOST
    public static bool IsEditorPreview => true;
#else
    public static bool IsEditorPreview => false;
#endif
}

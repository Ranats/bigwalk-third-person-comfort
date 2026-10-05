using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BigWalkThirdPerson;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "dev.sopur.bigwalk.thirdperson";
    public const string Name = "Big Walk Third Person";
    public const string Version = "1.0.0";

    internal static ManualLogSource LogSource;
    internal static ConfigEntry<CameraMode> Mode;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<KeyCode> DumpKey;
    internal static ConfigEntry<bool> StartEnabled;
    internal static ConfigEntry<float> Distance;
    internal static ConfigEntry<float> MinDistance;
    internal static ConfigEntry<float> MaxDistance;
    internal static ConfigEntry<float> Height;
    internal static ConfigEntry<float> Shoulder;
    internal static ConfigEntry<float> ZoomSpeed;
    internal static ConfigEntry<bool> Collision;
    internal static ConfigEntry<float> CollisionRadius;
    internal static ConfigEntry<bool> Stabilize;
    internal static ConfigEntry<float> StabilizeAmount;
    internal static ConfigEntry<float> StabilizeSmoothTime;
    internal static ConfigEntry<bool> LevelHorizon;
    internal static ConfigEntry<bool> ShowBody;
    internal static ConfigEntry<bool> HideHead;
    internal static ConfigEntry<bool> ShowStatus;

    public override void Load()
    {
        LogSource = Log;

        var cam = "Camera";
        Mode = Config.Bind(cam, nameof(Mode), CameraMode.Direct,
            "Direct = move the camera itself before rendering (recommended). " +
            "Guide = reroute the game's camera guide mechanism (experimental).");
        ToggleKey = Config.Bind(cam, nameof(ToggleKey), KeyCode.F4,
            "Key that toggles third person.");
        DumpKey = Config.Bind(cam, nameof(DumpKey), KeyCode.F8,
            "Key that dumps the local player hierarchy to the BepInEx log (for troubleshooting).");
        StartEnabled = Config.Bind(cam, nameof(StartEnabled), true,
            "Start in third person as soon as a local player exists.");
        Distance = Config.Bind(cam, nameof(Distance), 3.2f, "Camera distance behind the player (meters).");
        MinDistance = Config.Bind(cam, nameof(MinDistance), 1.0f, "Minimum zoom distance.");
        MaxDistance = Config.Bind(cam, nameof(MaxDistance), 7.0f, "Maximum zoom distance.");
        Height = Config.Bind(cam, nameof(Height), 0.45f, "Camera pivot height above the eye position.");
        Shoulder = Config.Bind(cam, nameof(Shoulder), 0.45f, "Sideways (over-shoulder) offset of the camera.");
        ZoomSpeed = Config.Bind(cam, nameof(ZoomSpeed), 1.6f, "Mouse wheel zoom speed.");
        Collision = Config.Bind(cam, nameof(Collision), true, "Pull the camera in when geometry blocks the view.");
        CollisionRadius = Config.Bind(cam, nameof(CollisionRadius), 0.22f, "Sphere radius used for camera collision.");
        Stabilize = Config.Bind(cam, nameof(Stabilize), true,
            "Smooth the camera pivot to remove head bob. Main motion-sickness comfort feature.");
        StabilizeAmount = Config.Bind(cam, nameof(StabilizeAmount), 0.85f,
            "How much of the pivot bob is removed (0 = off, 1 = fully smoothed).");
        StabilizeSmoothTime = Config.Bind(cam, nameof(StabilizeSmoothTime), 0.12f,
            "Seconds of smoothing on the pivot. Higher = steadier but laggier.");
        LevelHorizon = Config.Bind(cam, nameof(LevelHorizon), false,
            "Force the camera level with the horizon (removes roll).");

        var body = "Body";
        ShowBody = Config.Bind(body, nameof(ShowBody), true,
            "Show the full (remote-mode) body on the local player while in third person.");
        HideHead = Config.Bind(body, nameof(HideHead), false,
            "Hide the head renderers in third person (use if the head blocks the view).");

        var ui = "UI";
        ShowStatus = Config.Bind(ui, nameof(ShowStatus), true,
            "Show a small on-screen label while third person is active.");

        ClassInjector.RegisterTypeInIl2Cpp<ThirdPersonComponent>();
        ClassInjector.RegisterTypeInIl2Cpp<CameraDriver>();

        var go = new GameObject("BigWalkThirdPerson");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<ThirdPersonComponent>());

        LogSource.LogInfo($"{Name} {Version} loaded. Toggle with {ToggleKey.Value}.");
    }
}

public enum CameraMode
{
    Guide,
    Direct,
}

using System;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BigWalkThirdPerson;

/// <summary>
/// Drives the third-person camera. Two modes:
///  - Direct (default): the player camera is a child of the CameraPivot rig.
///    We overwrite its world position after the game has placed it, which keeps
///    the whole rig (look rotation, crouch, head bob) working.
///  - Guide: reroutes PlayerCameraMinder.cameraGuide to our own transform.
/// </summary>
public class ThirdPersonComponent : MonoBehaviour
{
    public ThirdPersonComponent(IntPtr ptr) : base(ptr) { }
    public ThirdPersonComponent() : base(ClassInjector.DerivedConstructorPointer<ThirdPersonComponent>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    private PlayerCharacter pc;
    private PlayerCameraReferences pcr;
    private PlayerCameraMinder minder;
    private Camera cam;
    private Transform pivot;     // "CameraPivot" - eye anchor that is never offset by us
    private Transform guide;     // our guide transform (Guide mode)
    private Transform gameGuide; // the game's original guide (Guide mode)
    private CameraDriver driver;

    private bool active;
    private bool suspended;
    private float curDistance;
    private float nextResolve;
    private float nextBodyCheck;
    private bool wantEnabled;
    private bool bodyApplied;
    private bool origHideTorso;
    private Vector3 origCamLocalPos;
    private int collideMode = -1; // -1 = not probed, 0 = none works, 1 = sphere, 2 = ray
    private int collisionMask;
    private string lastError;
    private float lastErrorTime;
    private Vector3 smoothPivot;
    private bool hasSmoothPivot;

    private void Awake()
    {
        var g = new GameObject("BigWalkThirdPersonGuide");
        UnityEngine.Object.DontDestroyOnLoad(g);
        g.hideFlags = HideFlags.HideAndDontSave;
        guide = g.transform;
        curDistance = Plugin.Distance.Value;
        wantEnabled = Plugin.StartEnabled.Value;
    }

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(Plugin.ToggleKey.Value))
                wantEnabled = !wantEnabled;
            if (Input.GetKeyDown(Plugin.DumpKey.Value))
                Dump();
            if (active && !suspended)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (scroll != 0f)
                    curDistance = Mathf.Clamp(curDistance - scroll * Plugin.ZoomSpeed.Value,
                        Plugin.MinDistance.Value, Plugin.MaxDistance.Value);
            }
        }
        catch (Exception e)
        {
            LogThrottled("input", $"Input handling failed: {e.Message}");
        }

        if (Time.unscaledTime >= nextResolve)
        {
            nextResolve = Time.unscaledTime + 0.5f;
            try { ResolveRefs(); }
            catch (Exception e) { LogThrottled("resolve", $"ResolveRefs: {e.Message}"); }
        }

        try
        {
            if (wantEnabled && !active && RefsOk()) Enter();
            if (!wantEnabled && active) Exit();
        }
        catch (Exception e)
        {
            LogThrottled("enterexit", $"Enter/Exit: {e}");
        }

        if (active && !suspended && Plugin.ShowBody.Value && Time.unscaledTime >= nextBodyCheck)
        {
            nextBodyCheck = Time.unscaledTime + 0.5f;
            try { ApplyBody(); }
            catch (Exception e) { LogThrottled("body", $"ApplyBody: {e.Message}"); }
        }

        // Pivot smoothing runs once per frame here so it stays stable even when
        // ApplyDirect is invoked twice (LateUpdate + OnPreRender).
        if (active && RefsOk() && pivot != null)
        {
            Vector3 raw = pivot.position;
            if (!hasSmoothPivot || (raw - smoothPivot).sqrMagnitude > 2.25f)
            {
                // Teleport / first frame: snap instead of dragging the camera.
                smoothPivot = raw;
                hasSmoothPivot = true;
            }
            else
            {
                float tau = Mathf.Max(0.01f, Plugin.StabilizeSmoothTime.Value);
                smoothPivot = Vector3.Lerp(smoothPivot, raw,
                    1f - Mathf.Exp(-Time.deltaTime / tau));
            }
        }
        else
        {
            hasSmoothPivot = false;
        }
    }

    private void LateUpdate()
    {
        if (!active || !RefsOk()) return;
        try
        {
            if (Plugin.Mode.Value == CameraMode.Guide) LateUpdateGuide();
            else ApplyDirect();
        }
        catch (Exception e)
        {
            LogThrottled("lateupdate", $"LateUpdate: {e.Message}");
        }
    }

    private bool RefsOk() => pc != null && cam != null;

    private void ResolveRefs()
    {
        if (pcr == null)
            pcr = UnityEngine.Object.FindObjectOfType<PlayerCameraReferences>();
        if (cam == null)
            cam = pcr != null && pcr.playerCamera != null ? pcr.playerCamera : Camera.main;

        if (pc == null && PlayerCharacter.allPlayerCharacters != null)
        {
            PlayerCharacter fallback = null;
            for (int i = 0; i < PlayerCharacter.allPlayerCharacters.Count; i++)
            {
                var c = PlayerCharacter.allPlayerCharacters[i];
                if (c == null) continue;
                try { if (c.isLocalPlayer) { pc = c; break; } }
                catch (Exception) { }
                if (fallback == null && c.cameraTransform != null && c.cameraMinder != null)
                    fallback = c;
            }
            if (pc == null) pc = fallback;
        }

        if (pc != null)
        {
            minder = pc.cameraMinder;
            if (cam == null && pc.cameraTransform != null)
                cam = pc.cameraTransform.GetComponent<Camera>();

            // CameraPivot is the camera's parent in the rig:
            // kernal/crouchTranslator/CameraHeightOffset/CameraUprighter/CameraPivot/PlayerCamera
            if (pivot == null && cam != null && cam.transform.parent != null)
                pivot = cam.transform.parent;
            if (pivot == null && pc.registry != null && pc.registry.headBone != null)
                pivot = pc.registry.headBone;
        }
    }

    private void Enter()
    {
        active = true;
        suspended = false;
        origCamLocalPos = cam.transform.localPosition;
        BuildCollisionMask();

        if (Plugin.Mode.Value == CameraMode.Guide)
        {
            if (minder != null)
            {
                gameGuide = minder._cameraGuide;
                minder.cameraGuide = guide;
            }
        }
        else
        {
            EnsureDriver();
        }

        if (Plugin.ShowBody.Value)
        {
            origHideTorso = pc.looks.hideLocalTorso;
            ApplyBody();
        }
        Plugin.LogSource.LogInfo($"Third person ON ({Plugin.Mode.Value})");
    }

    private void Exit()
    {
        active = false;
        suspended = false;
        try
        {
            if (Plugin.Mode.Value == CameraMode.Guide && minder != null)
            {
                if (minder._cameraGuide == null || minder._cameraGuide.Pointer == guide.Pointer)
                    minder.cameraGuide = gameGuide;
            }
            else
            {
                if (driver != null) driver.enabled = false;
                if (cam != null) cam.transform.localPosition = origCamLocalPos;
            }
            RestoreBody();
            Plugin.LogSource.LogInfo("Third person OFF");
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogError($"Exit failed: {e}");
        }
    }

    private void EnsureDriver()
    {
        if (driver != null || cam == null) return;
        driver = cam.gameObject.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<CameraDriver>())
            .TryCast<CameraDriver>();
        if (driver != null) driver.owner = this;
    }

    private void BuildCollisionMask()
    {
        // Exclude every layer that any collider under the player uses, plus the
        // built-in Ignore Raycast layer, so the boom can't hit the player's body.
        int exclude = 1 << 2;
        if (pc != null)
        {
            var cols = pc.GetComponentsInChildren<Collider>(true);
            if (cols != null)
                for (int i = 0; i < cols.Count; i++)
                    if (cols[i] != null) exclude |= 1 << cols[i].gameObject.layer;
            exclude |= 1 << pc.gameObject.layer;
        }
        collisionMask = ~exclude;
    }

    /// <summary>Probes once which physics query is unstripped in this build.</summary>
    private int CollideMode()
    {
        if (collideMode != -1) return collideMode;
        Vector3 p = pivot != null ? pivot.position : Vector3.zero;
        var ray = new Ray(p, Vector3.down);
        try
        {
            Physics.SphereCast(ray, 0.1f, out RaycastHit _, 1f, collisionMask, QueryTriggerInteraction.Ignore);
            collideMode = 1;
        }
        catch (Exception) { collideMode = -2; }
        if (collideMode == -2)
        {
            try
            {
                Physics.Raycast(ray, out RaycastHit _, 1f, collisionMask, QueryTriggerInteraction.Ignore);
                collideMode = 2;
            }
            catch (Exception) { collideMode = 0; }
        }
        if (collideMode == 0)
            Plugin.LogSource.LogWarning("No usable physics query found (all stripped); camera collision disabled.");
        else
            Plugin.LogSource.LogInfo($"Collision probe: mode {collideMode} (1=sphere,2=ray)");
        return collideMode;
    }

    /// <summary>Computes the boom-arm position and view rotation.</summary>
    internal bool ComputeBoom(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = default;
        if (!RefsOk()) return false;

        Vector3 pivotPos = pivot != null ? pivot.position : pc.transform.position + Vector3.up * 1.5f;
        if (Plugin.Stabilize.Value && hasSmoothPivot)
            pivotPos = Vector3.Lerp(pivotPos, smoothPivot,
                Mathf.Clamp01(Plugin.StabilizeAmount.Value));

        rot = cam.transform.rotation; // view direction the game computed
        if (Plugin.LevelHorizon.Value)
        {
            var e = rot.eulerAngles;
            rot = Quaternion.Euler(e.x, e.y, 0f);
        }
        Vector3 fwd = rot * Vector3.forward;
        Vector3 right = rot * Vector3.right;

        Vector3 anchor = pivotPos + Vector3.up * Plugin.Height.Value + right * Plugin.Shoulder.Value;
        float dist = curDistance;
        Vector3 desired = anchor - fwd * dist;

        if (Plugin.Collision.Value && CollideMode() != 0)
        {
            Vector3 dir = (desired - anchor).normalized;
            float minDist = 0.25f;
            bool hit;
            RaycastHit h = default;
            if (collideMode == 1)
                hit = Physics.SphereCast(anchor, Plugin.CollisionRadius.Value, dir, out h, dist,
                    collisionMask, QueryTriggerInteraction.Ignore);
            else
                hit = Physics.Raycast(anchor, dir, out h, dist, collisionMask, QueryTriggerInteraction.Ignore);
            if (hit && h.distance < dist)
                desired = anchor + dir * Mathf.Max(h.distance, minDist);
        }
        pos = desired;
        return true;
    }

    private void LateUpdateGuide()
    {
        if (minder == null) return;
        var cur = minder._cameraGuide;
        if (cur == null || cur.Pointer == guide.Pointer)
        {
            suspended = false;
            if (cur == null || cur.Pointer != guide.Pointer)
                minder.cameraGuide = guide;
        }
        else if (gameGuide == null || cur.Pointer != gameGuide.Pointer)
        {
            // Something else (cutscene, dream, ...) holds the camera. Leave it alone.
            suspended = true;
            gameGuide = cur;
            return;
        }
        else
        {
            // Camera control returned to the default guide -> reassert ours.
            suspended = false;
            minder.cameraGuide = guide;
        }
        suspended = false;

        if (ComputeBoom(out Vector3 pos, out Quaternion rot))
            guide.SetPositionAndRotation(pos, rot);
    }

    internal void ApplyDirect()
    {
        if (ComputeBoom(out Vector3 pos, out Quaternion rot))
            cam.transform.SetPositionAndRotation(pos, rot);
    }

    private void ApplyBody()
    {
        if (pc == null || pc.looks == null) return;
        if (!bodyApplied) pc.looks.SetBodyToRemoteMode();
        if (pc.looks.hideLocalTorso) pc.looks.SetHideLocalTorso(false);
        SetHeadVisible(!Plugin.HideHead.Value);
        bodyApplied = true;
    }

    private void RestoreBody()
    {
        if (pc == null || pc.looks == null || !bodyApplied) return;
        try
        {
            SetHeadVisible(true);
            pc.looks.SetBodyToLocalMode();
            if (pc.looks.hideLocalTorso != origHideTorso) pc.looks.SetHideLocalTorso(origHideTorso);
            bodyApplied = false;
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"RestoreBody: {e.Message}");
        }
    }

    private void SetHeadVisible(bool visible)
    {
        var heads = pc.looks.headRenderers;
        if (heads == null) return;
        for (int i = 0; i < heads.Count; i++)
            if (heads[i] != null && heads[i].renderer != null)
                heads[i].renderer.enabled = visible;
    }

    private void OnGUI()
    {
        try
        {
            if (!active || !Plugin.ShowStatus.Value) return;
            var r = new Rect(12, Screen.height - 34, 340, 22);
            string s = $"3rd person [{Plugin.ToggleKey.Value}]  zoom:{curDistance:0.0}m{(suspended ? " (cutscene)" : "")}";
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s);
            GUI.color = Color.white;
            GUI.Label(r, s);
        }
        catch (Exception e)
        {
            LogThrottled("gui", $"OnGUI: {e.Message}");
        }
    }

    private void LogThrottled(string key, string msg)
    {
        if (lastError == key && Time.unscaledTime - lastErrorTime < 5f) return;
        lastError = key;
        lastErrorTime = Time.unscaledTime;
        Plugin.LogSource.LogError(msg);
    }

    private void Dump()
    {
        if (pc == null) { Plugin.LogSource.LogInfo("Dump: no local player yet"); return; }
        Plugin.LogSource.LogInfo("=== ThirdPerson dump ===");
        Plugin.LogSource.LogInfo($"cam={(cam != null ? cam.name : "null")} pivot={(pivot != null ? pivot.name : "null")} " +
            $"camLocal={cam?.transform.localPosition} collideMode={collideMode} mask={collisionMask}");
        DumpTransform(pc.transform, 0);
    }

    private void DumpTransform(Transform t, int depth)
    {
        var r = t.GetComponent<Renderer>();
        string extra = r != null ? $" renderer.enabled={r.enabled} layer={t.gameObject.layer}" : $" layer={t.gameObject.layer}";
        Plugin.LogSource.LogInfo($"{new string(' ', depth * 2)}{t.name}{extra}");
        for (int i = 0; i < t.childCount; i++)
            DumpTransform(t.GetChild(i), depth + 1);
    }

    private void OnDestroy()
    {
        if (active) Exit();
    }
}

/// <summary>
/// Attached to the player camera in Direct mode; OnPreRender runs after every
/// script update, right before the camera renders - the last word on position.
/// </summary>
public class CameraDriver : MonoBehaviour
{
    public CameraDriver(IntPtr ptr) : base(ptr) { }
    public CameraDriver() : base(ClassInjector.DerivedConstructorPointer<CameraDriver>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    internal ThirdPersonComponent owner;

    private void OnPreRender()
    {
        try { owner?.ApplyDirect(); }
        catch (Exception) { }
    }
}

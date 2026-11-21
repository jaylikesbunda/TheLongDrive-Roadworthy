using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace TldMp
{
    // Camera tools for filming the game (Roadworthy setting "Promo camera", or
    // TLD_MP_PROMO_CAMERA=1 / =0 to force it on or off). Off by default; while
    // off nothing here touches the game.
    //
    // F6        free camera: smooth fly/look, the player's own input is paused
    // Shift+F6  orbit camera around the current car (or the player); you can drive
    // F7        start/stop recording a clip (fixed-step frames + ffmpeg -> MP4)
    // F8        slow motion 1 -> 1/2 -> 1/4 -> 1/10 (single-player only)
    // In the free camera: K adds a keyframe, P plays the keyframed path, Shift+P
    // plays and records it. Paths are saved as text shots that can also be
    // written by hand and run from Documents\TheLongDrive\roadworthy-camera\command.txt.
    //
    // The camera rig is the game's own (fpscontroller.CamParent and its cameras):
    // after the game has placed it, the rig is moved to the tool's pose each
    // LateUpdate and its original parent and local pose are put back on exit.
    // HUD hiding uses the game's own hide-HUD flag (mainscript.showcrosshair2),
    // the outside-view body/head setup copies SwitchToOutsideView, and the free
    // camera pauses player input the same way the game skips input for a frame
    // after closing the pause menu (inputscript.disableOneFrame).
    [DefaultExecutionOrder(10001)]
    public sealed class PromoCamera : MonoBehaviour
    {
        enum Mode { Off, Free, Orbit }

        static readonly string EnvSetting = Environment.GetEnvironmentVariable("TLD_MP_PROMO_CAMERA");
        static readonly FieldInfo SunGlassesField = typeof(mainscript).GetField("PPVSunGlasses");
        const float LookSensitivity = 1.6f, LookSharpness = 14f, MoveSharpness = 6f, SoftSharpness = 6f;
        const int OutsideHeadLayer = 17;
        static PromoCamera instance;

        // Set by the Roadworthy bridge; the stand-alone vanilla build leaves them unset.
        internal static Func<bool> SessionCheck;
        internal static Action<bool> CameraTakenHook;
        static bool InSession() { var f = SessionCheck; return f != null && f(); }
        static void CameraTaken(bool on) { var f = CameraTakenHook; if (f != null) f(on); }

        // The stand-alone vanilla build is only the camera, so it is on unless the env var says 0.
        internal static bool ForceOn;

        internal static bool SettingOn()
        {
            if (EnvSetting == "1") return true;
            if (EnvSetting == "0") return false;
            return ForceOn || PatchOptions.PromoCamera;
        }

        // True while the tool owns the camera (other camera effects stand aside).
        internal static bool Active { get { return instance != null && instance.taken; } }

        Mode mode;
        // Free camera, current and target values.
        Transform anchor;
        Vector3 velocity;
        float yaw, pitch, roll, fov = 60f, targetYaw, targetPitch, targetRoll, targetFov = 60f, gameFov = 60f;
        float speed = PromoCameraPolicy.DefaultSpeed;
        bool track;
        // Orbit camera.
        float orbitYaw, orbitPitch = 12f, orbitDistance = 7f, targetOrbitYaw, targetOrbitPitch = 12f, targetOrbitDistance = 7f, heading = float.NaN;
        // Path editing and playback.
        CamShot edit = new CamShot { Space = ShotSpace.World };
        CamShot playShot;
        string playName;
        float playTime;
        bool playing, playRecord;
        // Camera rig takeover.
        bool taken;
        fpscontroller owner;
        Transform savedParent;
        Vector3 savedLocalPosition;
        Quaternion savedLocalRotation;
        int[] savedHeadLayers;
        Vector3 lastPosition;
        Quaternion lastRotation = Quaternion.identity;
        // HUD and overlay.
        bool hudHidden, savedHud, overlayHidden;
        // Shift+F7 / "hud text": record with interaction prompts and help text kept on screen.
        bool keepText, savedCrosshair, crosshairSaved;
        Canvas canvas;
        TextMeshProUGUI label;
        float nextLabel;
        string message;
        float messageUntil;
        // Slow motion.
        float slow = 1f, savedFixedDelta;
        // Recording.
        bool recording, snapping;
        string clipDir;
        int frames, captureFps = 60, superSize = 1, savedCaptureRate;
        int pending;
        string writeError, lastClip;
        float nextPoll, nextStatus;
        string lastResult = "started";
        // Automation.
        float frozenDay = float.NaN;
        bool autoSaveChanged, savedAutoSave, loadingWorld;

        void Awake() { instance = this; }
        void Start() { PatchOptions.Load(); }

        void OnDisable() { ShutDown(); }

        static string Root()
        {
            string options = PatchOptions.DefaultPath();
            string dir = options == null ? null : Path.GetDirectoryName(options);
            return dir == null ? null : Path.Combine(dir, "roadworthy-camera");
        }

        static fpscontroller Player()
        {
            var main = mainscript.s;
            return main == null ? null : main.player;
        }

        void Update()
        {
            bool enabledNow = SettingOn();
            // Commands also work in the main menu (newworld) and while loading.
            if (enabledNow && Time.realtimeSinceStartup >= nextPoll) { nextPoll = Time.realtimeSinceStartup + 0.5f; PollCommands(); }
            var p = Player();
            if (enabledNow && p != null) { KeepTime(); KeepTeleport(p); }
            if (enabledNow && Time.realtimeSinceStartup >= nextStatus) { nextStatus = Time.realtimeSinceStartup + 2f; WriteStatus(null); }
            if (!enabledNow || p == null || p.Cam == null || p.CamParent == null || mainscript.IsVR())
            {
                if (taken || recording || playing || slow < 1f || mode != Mode.Off) ShutDown();
                return;
            }
            if (p.died || mainscript.s.sleeping)
            {
                if (mode != Mode.Off || playing) { StopPlay(false); SetMode(Mode.Off); }
                if (slow < 1f) SetSlow(1f);
            }
            KeepSlow();
            if (KeysAvailable()) HandleKeys(p);
            if (mode == Mode.Free && inputscript.i != null) inputscript.i.disableOneFrame = true;
        }

        void LateUpdate()
        {
            var p = Player();
            HideAllButText();
            if (!taken || p == null || p.Cam == null || p.CamParent == null) { UpdateOverlay(); return; }
            if (mode == Mode.Free && inputscript.i != null) inputscript.i.disableOneFrame = true;
            float dt = PromoCameraPolicy.CameraDelta(Time.deltaTime, Time.timeScale);
            bool paused = Time.timeScale == 0f || !mainscript.s.crsrLocked;
            Vector3 pos; Quaternion rot; float useFov;
            if (playing && !paused) playTime += dt;
            if (playing && SamplePlay(p, out pos, out rot, out useFov))
            {
                if (playTime >= PromoCameraPolicy.Duration(playShot)) { SyncFreeTo(pos, rot, useFov); StopPlay(true); }
            }
            else if (mode == Mode.Orbit) Orbit(p, paused ? 0f : dt, out pos, out rot, out useFov);
            else Fly(p, paused ? 0f : dt, out pos, out rot, out useFov);
            Apply(p, pos, rot, useFov);
            UpdateOverlay();
        }

        // ---- Keys --------------------------------------------------------

        bool KeysAvailable()
        {
            var main = mainscript.s;
            if (!main.crsrLocked || syncScript.InputFocused()) return false;
            return kaposztaleves.s == null || !kaposztaleves.s.SomeInputSelected();
        }

        static bool Shift() { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }

        void HandleKeys(fpscontroller p)
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                Mode want = Shift() ? Mode.Orbit : Mode.Free;
                SetMode(mode == want ? Mode.Off : want);
            }
            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (recording) { if (playing) StopPlay(false); StopRecording(); }
                else
                {
                    // Shift+F7 keeps item text and prompts on screen; F7 hides the whole HUD.
                    if (!hudHidden) keepText = Shift();
                    StartRecording(60, 1, (mode == Mode.Orbit ? "orbit" : mode == Mode.Free ? "free" : "game") + (keepText ? "_text" : ""));
                }
            }
            if (Input.GetKeyDown(KeyCode.F8)) SetSlow(Shift() ? 1f : PromoCameraPolicy.NextTimeScale(slow));
            if (mode != Mode.Free) return;
            if (Input.GetKeyDown(KeyCode.H)) { overlayHidden = !overlayHidden; if (!recording) HideHud(overlayHidden); }
            if (Input.GetKeyDown(KeyCode.T)) { track = !track; Say(track ? "Looking at the car" : "Free look"); }
            if (Input.GetKeyDown(KeyCode.G))
            {
                edit.Keys.Clear();
                edit.Space = edit.Space == ShotSpace.World ? ShotSpace.Car : ShotSpace.World;
                Say("Keys cleared; new keys are " + (edit.Space == ShotSpace.Car ? "relative to the car (follows it)" : "fixed in the world"));
            }
            if (Input.GetKeyDown(KeyCode.K)) AddKey(p);
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                if (Shift()) edit.Keys.Clear();
                else if (edit.Keys.Count > 0) edit.Keys.RemoveAt(edit.Keys.Count - 1);
                SaveShot("last", edit);
                Say(edit.Keys.Count + " keys");
            }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) { edit.Segment = Math.Max(PromoCameraPolicy.MinSegment, edit.Segment - 0.5f); SaveShot("last", edit); Say("Segment " + edit.Segment.ToString("0.0") + " s"); }
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) { edit.Segment = Math.Min(PromoCameraPolicy.MaxSegment, edit.Segment + 0.5f); SaveShot("last", edit); Say("Segment " + edit.Segment.ToString("0.0") + " s"); }
            if (Input.GetKeyDown(KeyCode.P))
            {
                if (playing) StopPlay(false);
                else StartPlay(edit, "last", Shift());
            }
        }

        // ---- Modes -------------------------------------------------------

        void SetMode(Mode next)
        {
            var p = Player();
            if (next == mode) return;
            if (next != Mode.Off && p != null && !taken) TakeOver(p);
            if (next == Mode.Orbit) { heading = float.NaN; targetOrbitYaw = orbitYaw = 0f; }
            mode = next;
            if (mode == Mode.Off && !playing) Release();
            Say(mode == Mode.Free ? "Free camera (game input paused)" : mode == Mode.Orbit ? "Orbit camera" : "Camera back to the game");
        }

        void TakeOver(fpscontroller p)
        {
            owner = p;
            savedParent = p.CamParent.parent;
            savedLocalPosition = p.CamParent.localPosition;
            savedLocalRotation = p.CamParent.localRotation;
            gameFov = p.Cam.fieldOfView;
            if (p.pumpkinhead != null)
            {
                savedHeadLayers = new int[p.pumpkinhead.Length];
                for (int i = 0; i < p.pumpkinhead.Length; i++) savedHeadLayers[i] = p.pumpkinhead[i] == null ? 0 : p.pumpkinhead[i].layer;
            }
            if (anchor == null)
            {
                anchor = NewAnchor("RoadworthyCameraAnchor");
            }
            Transform cam = p.Cam.transform;
            anchor.position = cam.position;
            Vector3 e = cam.rotation.eulerAngles;
            targetYaw = yaw = e.y;
            targetPitch = pitch = PromoCameraPolicy.ClampPitch(Signed(e.x));
            targetRoll = roll = 0f;
            targetFov = fov = gameFov;
            velocity = Vector3.zero;
            lastPosition = cam.position; lastRotation = cam.rotation;
            taken = true;
            CameraTaken(true);
        }

        // A transform the game's floating origin moves along with the world
        // (same setup as the game's own drop camera parent).
        static Transform NewAnchor(string name)
        {
            var go = new GameObject(name);
            var shift = go.AddComponent<visszarako>();
            shift.FStart(_dontlookUnderMap: true, _importantUnderMapLook: false, _moveWhenParented: false);
            shift.noWarp = true;
            return go.transform;
        }

        void Release()
        {
            if (!taken) return;
            taken = false;
            CameraTaken(false);
            var p = owner;
            owner = null;
            if (p == null || p.CamParent == null) return;
            try
            {
                p.CamParent.SetParent(savedParent, false);
                p.CamParent.localPosition = savedLocalPosition;
                p.CamParent.localRotation = savedLocalRotation;
                if (p.pumpkinhead != null && savedHeadLayers != null)
                    for (int i = 0; i < p.pumpkinhead.Length && i < savedHeadLayers.Length; i++)
                        if (p.pumpkinhead[i] != null) p.pumpkinhead[i].layer = savedHeadLayers[i];
                if (p.wearedRenderer != null) p.wearedRenderer.make = !p.otherView();
                SetSunGlasses(!p.otherView() && settingsscript.s != null && settingsscript.s.S.BEnableSunGlassesEffects);
                p.SetAllCamFov();
            }
            catch (Exception e) { Debug.LogWarning("[Roadworthy camera] restore failed: " + e.GetType().Name + " " + e.Message); }
        }

        static void SetSunGlasses(bool on)
        {
            try
            {
                var volume = SunGlassesField == null ? null : SunGlassesField.GetValue(mainscript.s) as Behaviour;
                if (volume != null) volume.enabled = on;
            }
            catch (Exception) { }
        }

        void ShutDown()
        {
            if (recording) StopRecording();
            playing = false; playShot = null;
            if (slow < 1f) SetSlow(1f);
            mode = Mode.Off;
            Release();
            if (hudHidden) HideHud(false);
            overlayHidden = false;
            if (canvas != null) canvas.enabled = false;
        }

        // Moves the game's camera rig so the main camera lands on pos/rot,
        // whatever local offset the game has put between the rig and the camera.
        void Apply(fpscontroller p, Vector3 pos, Quaternion rot, float useFov)
        {
            Transform rig = p.CamParent, cam = p.Cam.transform;
            Quaternion inv = Quaternion.Inverse(rig.rotation);
            Quaternion rel = inv * cam.rotation;
            Vector3 relPos = inv * (cam.position - rig.position);
            rig.rotation = rot * Quaternion.Inverse(rel);
            rig.position = pos - rig.rotation * relPos;
            SetFov(p.Cam, useFov); SetFov(p.mainCam, useFov); SetFov(p.farCamera, useFov); SetFov(p.skyCamera, useFov); SetFov(p.saveScreenshotCam, useFov);
            // Outside view: the player's head and worn items are drawn (as SwitchToOutsideView).
            if (p.wearedRenderer != null) p.wearedRenderer.make = false;
            if (p.pumpkinhead != null)
                for (int i = 0; i < p.pumpkinhead.Length; i++)
                    if (p.pumpkinhead[i] != null) p.pumpkinhead[i].layer = OutsideHeadLayer;
            SetSunGlasses(false);
            lastPosition = pos; lastRotation = rot;
        }

        static void SetFov(Camera c, float f) { if (c != null && c.fieldOfView != f) c.fieldOfView = f; }

        static float Signed(float angle) { angle %= 360f; return angle > 180f ? angle - 360f : angle < -180f ? angle + 360f : angle; }

        // ---- Free camera -------------------------------------------------

        void Fly(fpscontroller p, float dt, out Vector3 pos, out Quaternion rot, out float useFov)
        {
            if (anchor == null) { anchor = NewAnchor("RoadworthyCameraAnchor"); anchor.position = lastPosition; }
            if (dt > 0f && mode == Mode.Free && KeysAvailable())
            {
                float wheel = Input.mouseScrollDelta.y;
                bool zoom = Input.GetMouseButton(1);
                targetYaw += Input.GetAxis("Mouse X") * LookSensitivity;
                targetPitch = PromoCameraPolicy.ClampPitch(targetPitch - Input.GetAxis("Mouse Y") * LookSensitivity);
                if (wheel != 0f)
                {
                    if (zoom) targetFov = PromoCameraPolicy.ClampFov(targetFov - wheel * 3f);
                    else { speed = PromoCameraPolicy.StepSpeed(speed, wheel); Say("Speed " + speed.ToString("0.0") + " m/s"); }
                }
                if (Input.GetKey(KeyCode.Z)) targetRoll -= 30f * dt;
                if (Input.GetKey(KeyCode.X)) targetRoll += 30f * dt;
                if (Input.GetKey(KeyCode.LeftBracket)) targetFov = PromoCameraPolicy.ClampFov(targetFov - 20f * dt);
                if (Input.GetKey(KeyCode.RightBracket)) targetFov = PromoCameraPolicy.ClampFov(targetFov + 20f * dt);
                if (Input.GetKeyDown(KeyCode.R)) { targetRoll = 0f; targetFov = gameFov; }
                Vector3 move = new Vector3(Axis(KeyCode.D, KeyCode.A), 0f, Axis(KeyCode.W, KeyCode.S));
                float up = Axis(KeyCode.E, KeyCode.Q) + (Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.C) ? 1f : 0f);
                float mult = Shift() ? 4f : (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) ? 0.25f : 1f;
                Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
                Vector3 want = (look * move + Vector3.up * Mathf.Clamp(up, -1f, 1f)) * speed * mult;
                velocity.x = PromoCameraPolicy.Damp(velocity.x, want.x, MoveSharpness, dt);
                velocity.y = PromoCameraPolicy.Damp(velocity.y, want.y, MoveSharpness, dt);
                velocity.z = PromoCameraPolicy.Damp(velocity.z, want.z, MoveSharpness, dt);
            }
            else if (dt > 0f)
            {
                velocity.x = PromoCameraPolicy.Damp(velocity.x, 0f, MoveSharpness, dt);
                velocity.y = PromoCameraPolicy.Damp(velocity.y, 0f, MoveSharpness, dt);
                velocity.z = PromoCameraPolicy.Damp(velocity.z, 0f, MoveSharpness, dt);
            }
            pos = anchor.position + velocity * dt;
            anchor.position = pos;
            Transform target;
            if (track && (target = Target(p)) != null)
            {
                Vector3 to = target.position + Vector3.up * 0.8f - pos;
                if (to.sqrMagnitude > 0.01f)
                {
                    Vector3 e = Quaternion.LookRotation(to, Vector3.up).eulerAngles;
                    targetYaw = yaw + PromoCameraPolicy.DeltaAngle(yaw, e.y);
                    targetPitch = PromoCameraPolicy.ClampPitch(Signed(e.x));
                }
            }
            yaw = PromoCameraPolicy.Damp(yaw, targetYaw, LookSharpness, dt);
            pitch = PromoCameraPolicy.Damp(pitch, targetPitch, LookSharpness, dt);
            roll = PromoCameraPolicy.Damp(roll, targetRoll, SoftSharpness, dt);
            fov = PromoCameraPolicy.Damp(fov, targetFov, SoftSharpness, dt);
            rot = Quaternion.Euler(pitch, yaw, roll);
            useFov = fov;
        }

        static float Axis(KeyCode plus, KeyCode minus) { return (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f); }

        // After a path ends the free camera carries on from where it stopped.
        void SyncFreeTo(Vector3 pos, Quaternion rot, float useFov)
        {
            if (anchor != null) anchor.position = pos;
            Vector3 e = rot.eulerAngles;
            targetYaw = yaw = e.y; targetPitch = pitch = PromoCameraPolicy.ClampPitch(Signed(e.x)); targetRoll = roll = Signed(e.z);
            targetFov = fov = useFov; velocity = Vector3.zero;
        }

        // ---- Orbit camera ------------------------------------------------

        // The car the player sits in, else the last car they used, else the player.
        static Transform Target(fpscontroller p)
        {
            if (p.seat != null) return p.seat.transform.root;
            if (p.lastCar != null) return p.lastCar.transform.root;
            return p.transform;
        }

        void Orbit(fpscontroller p, float dt, out Vector3 pos, out Quaternion rot, out float useFov)
        {
            Transform target = Target(p);
            Vector3 center = target.position + Vector3.up * 1f;
            float carYaw = target.eulerAngles.y;
            heading = float.IsNaN(heading) ? carYaw : PromoCameraPolicy.DampAngle(heading, carYaw, 3f, dt);
            if (dt > 0f && KeysAvailable())
            {
                float wheel = Input.mouseScrollDelta.y;
                targetOrbitYaw += Input.GetAxis("Mouse X") * LookSensitivity;
                targetOrbitPitch = Mathf.Clamp(targetOrbitPitch - Input.GetAxis("Mouse Y") * LookSensitivity, -15f, 85f);
                if (wheel != 0f)
                {
                    if (Input.GetMouseButton(1)) targetFov = PromoCameraPolicy.ClampFov(targetFov - wheel * 3f);
                    else targetOrbitDistance = PromoCameraPolicy.ClampDistance(targetOrbitDistance * (1f - wheel * 0.1f));
                }
            }
            orbitYaw = PromoCameraPolicy.Damp(orbitYaw, targetOrbitYaw, LookSharpness, dt);
            orbitPitch = PromoCameraPolicy.Damp(orbitPitch, targetOrbitPitch, LookSharpness, dt);
            orbitDistance = PromoCameraPolicy.Damp(orbitDistance, targetOrbitDistance, SoftSharpness, dt);
            fov = PromoCameraPolicy.Damp(fov, targetFov, SoftSharpness, dt);
            float x, y, z;
            PromoCameraPolicy.OrbitOffset(orbitYaw, orbitPitch, orbitDistance, out x, out y, out z);
            pos = center + Quaternion.Euler(0f, heading, 0f) * new Vector3(x, y, z);
            Vector3 to = center - pos;
            rot = to.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(to, Vector3.up) : lastRotation;
            useFov = fov;
            if (anchor != null) anchor.position = pos;
        }

        // ---- Paths -------------------------------------------------------

        void Frame(fpscontroller p, CamShot shot, out Vector3 origin, out Quaternion frame, out bool ok)
        {
            ok = true;
            if (shot.Space == ShotSpace.Car)
            {
                Transform t = Target(p);
                origin = t.position;
                frame = shot.Level ? Quaternion.Euler(0f, t.eulerAngles.y, 0f) : t.rotation;
                ok = t != p.transform || p.seat != null || p.lastCar != null;
                return;
            }
            // World keys are the game's global coordinates (same seed, same place);
            // they are converted per key in double precision, see WorldPoint.
            origin = Vector3.zero;
            frame = Quaternion.identity;
        }

        bool SamplePlay(fpscontroller p, out Vector3 pos, out Quaternion rot, out float useFov)
        {
            pos = lastPosition; rot = lastRotation; useFov = fov;
            if (playShot == null) { playing = false; return false; }
            Vector3 origin; Quaternion frame; bool ok;
            Frame(p, playShot, out origin, out frame, out ok);
            CamKey k = PromoCameraPolicy.Sample(playShot, playTime);
            pos = playShot.Space == ShotSpace.World ? WorldPoint(k.X, k.Y, k.Z) : origin + frame * new Vector3(k.X, k.Y, k.Z);
            if (playShot.LookAtCar)
            {
                Transform t = Target(p);
                Vector3 look = t.position + Quaternion.Euler(0f, t.eulerAngles.y, 0f) * new Vector3(playShot.LookX, playShot.LookY, playShot.LookZ);
                Vector3 to = look - pos;
                rot = (to.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(to, Vector3.up) : lastRotation) * Quaternion.Euler(0f, 0f, k.Roll);
            }
            else rot = frame * Quaternion.Euler(k.Pitch, k.Yaw, k.Roll);
            useFov = k.Fov;
            return true;
        }

        void AddKey(fpscontroller p)
        {
            Vector3 origin; Quaternion frame; bool ok;
            Frame(p, edit, out origin, out frame, out ok);
            if (!ok) { Say("No car to follow: get in a car first, or press G for world keys"); return; }
            Quaternion inv = Quaternion.Inverse(frame);
            Vector3 local = inv * (lastPosition - origin);
            if (edit.Space == ShotSpace.World) { Vector3d g = mainscript.GlobalFromUnityPos(lastPosition); local = new Vector3((float)g.x, (float)g.y, (float)g.z); }
            Vector3 e = (inv * lastRotation).eulerAngles;
            edit.LookAtCar = track;
            edit.Keys.Add(new CamKey { X = local.x, Y = local.y, Z = local.z, Yaw = e.y, Pitch = PromoCameraPolicy.ClampPitch(Signed(e.x)), Roll = Signed(e.z), Fov = PromoCameraPolicy.ClampFov(fov), Hold = float.NaN });
            SaveShot("last", edit);
            Say("Key " + edit.Keys.Count + " (" + PromoCameraPolicy.Duration(edit).ToString("0.0") + " s path, saved as shots\\last.txt)");
        }

        static Vector3 WorldPoint(float x, float y, float z) { return mainscript.UnityPosFromGlobal(new Vector3d((double)x, (double)y, (double)z)); }

        void StartPlay(CamShot shot, string name, bool record)
        {
            var p = Player();
            if (p == null || shot == null) return;
            if (shot.Keys.Count < 2) { Say("A path needs at least 2 keys (K adds one)"); return; }
            Vector3 origin; Quaternion frame; bool ok;
            if (shot.Space == ShotSpace.Car) { Frame(p, shot, out origin, out frame, out ok); if (!ok) { Say("This shot follows a car: get in one first"); return; } }
            if (!taken) TakeOver(p);
            playShot = shot; playName = name; playTime = 0f; playing = true; playRecord = record;
            if (shot.TimeScale < 1f) SetSlow(shot.TimeScale);
            if (record && !recording) StartRecording(shot.Fps, shot.SuperSize, name);
            Say("Playing " + (name ?? "path") + " (" + PromoCameraPolicy.Duration(shot).ToString("0.0") + " s)" + (record ? ", recording" : ""));
        }

        void StopPlay(bool finished)
        {
            if (!playing) return;
            playing = false;
            if (playRecord && recording) StopRecording();
            if (playShot != null && playShot.TimeScale < 1f) SetSlow(1f);
            playShot = null; playRecord = false;
            if (mode == Mode.Off) Release();
            if (finished) Say("Path finished");
        }

        void SaveShot(string name, CamShot shot)
        {
            try
            {
                string root = Root();
                if (root == null || !PromoCameraPolicy.ValidName(name)) return;
                string dir = Path.Combine(root, "shots");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name + ".txt"), PromoCameraPolicy.Format(shot), new UTF8Encoding(false));
            }
            catch (Exception e) { Say("Could not save shot: " + e.Message); }
        }

        CamShot LoadShot(string name)
        {
            if (name == null) return edit;
            string root = Root();
            string path = root == null ? null : Path.Combine(Path.Combine(root, "shots"), name + ".txt");
            if (path == null || !File.Exists(path)) { Say("No shot named " + name); return null; }
            string error;
            CamShot shot = PromoCameraPolicy.Parse(File.ReadAllText(path), out error);
            if (error != null) Say(name + ": " + error);
            return shot;
        }

        // ---- Slow motion -------------------------------------------------

        void SetSlow(float value)
        {
            value = PromoCameraPolicy.ClampTimeScale(value);
            if (value < 1f && InSession()) { Say("Slow motion is single-player only"); return; }
            if (slow >= 1f && value < 1f) savedFixedDelta = Time.fixedDeltaTime;
            float before = slow;
            slow = value;
            if (Time.timeScale != 0f) Time.timeScale = slow;
            if (savedFixedDelta > 0f) Time.fixedDeltaTime = savedFixedDelta * slow;
            if (slow >= 1f && before < 1f) { Time.timeScale = Time.timeScale == 0f ? 0f : 1f; if (savedFixedDelta > 0f) Time.fixedDeltaTime = savedFixedDelta; savedFixedDelta = 0f; }
            Say(slow >= 1f ? "Normal speed" : "Slow motion x" + slow.ToString("0.##"));
        }

        // The game puts the time scale back to 1 after its pause menu.
        void KeepSlow()
        {
            if (slow >= 1f) return;
            if (InSession()) { SetSlow(1f); return; }
            if (Time.timeScale != 0f && Math.Abs(Time.timeScale - slow) > 0.0001f) Time.timeScale = slow;
        }

        // ---- HUD and overlay -------------------------------------------

        void HideHud(bool hide)
        {
            var main = mainscript.s;
            if (main == null) { hudHidden = false; return; }
            if (hide && !hudHidden)
            {
                savedHud = main.showcrosshair2;
                if (keepText)
                {
                    // Text-only HUD: the game keeps drawing interaction prompts and help text
                    // ([E]: drink, the barrel's contents, ...) because they follow showcrosshair2;
                    // the crosshair goes through its own setting and the rest is hidden each frame.
                    var s = settingsscript.s == null ? null : settingsscript.s.S;
                    if (s != null) { savedCrosshair = s.BCrosshair; s.BCrosshair = false; crosshairSaved = true; }
                    main.showcrosshair2 = true;
                }
                else main.showcrosshair2 = false;
                hudHidden = true;
            }
            else if (!hide && hudHidden)
            {
                main.showcrosshair2 = savedHud;
                var s = settingsscript.s == null ? null : settingsscript.s.S;
                if (crosshairSaved && s != null) s.BCrosshair = savedCrosshair;
                crosshairSaved = false;
                hudHidden = false;
            }
        }

        // Text-only HUD: switch off everything but the prompts after the game's own HUD pass.
        void HideAllButText()
        {
            if (!hudHidden || !keepText) return;
            var main = mainscript.s;
            var m = main == null ? null : main.menu;
            if (m == null) return;
            Off(m.SPiss); Off(m.SHp); Off(m.SWater); Off(m.SFood); Off(m.SShit); Off(m.SlidThrowForce);
            Off(m.GInventory); Off(m.GItemHelp); Off(m.GR); Off(m.GCMain);
            Off(m.TextFps); Off(m.TCoord);
            if (main.player != null && main.player.lookObj3D != null) Off(main.player.lookObj3D.gameObject);
        }

        static void Off(Component c) { if (c != null) Off(c.gameObject); }
        static void Off(GameObject g) { if (g != null && g.activeSelf) g.SetActive(false); }

        void Say(string text)
        {
            message = text;
            messageUntil = Time.realtimeSinceStartup + 3f;
            nextLabel = 0f;
            Debug.Log("[Roadworthy camera] " + text);
        }

        void UpdateOverlay()
        {
            bool show = SettingOn() && (taken || recording || playing || (message != null && Time.realtimeSinceStartup < messageUntil)) && !overlayHidden && !recording && !snapping;
            if (!show) { if (canvas != null && canvas.enabled) canvas.enabled = false; return; }
            if (!EnsureOverlay()) return;
            if (!canvas.enabled) { canvas.enabled = true; nextLabel = 0f; }
            if (Time.realtimeSinceStartup < nextLabel) return;
            nextLabel = Time.realtimeSinceStartup + 0.1f;
            var b = new StringBuilder();
            if (mode == Mode.Free)
            {
                b.Append("<b>FREE CAMERA</b>  speed ").Append(speed.ToString("0.0")).Append(" m/s  fov ").Append(fov.ToString("0")).Append("  roll ").Append(roll.ToString("0"));
                if (slow < 1f) b.Append("  slow x").Append(slow.ToString("0.##"));
                b.Append("\nkeys ").Append(edit.Keys.Count).Append(" (").Append(edit.Space == ShotSpace.Car ? "car" : "world").Append(", ").Append(edit.Segment.ToString("0.0")).Append(" s each)").Append(track ? "  looking at car" : "");
                b.Append("\n<size=80%>WASD move  E/Q up/down  Shift fast  Ctrl slow  wheel speed  RMB+wheel or [ ] zoom  Z/X roll  R reset");
                b.Append("\nK add key  Backspace undo  P play  Shift+P play+record  -/= key time  G car/world keys  T look at car  H hide");
                b.Append("\nF6 exit  F7 record  F8 slow motion</size>");
            }
            else if (mode == Mode.Orbit)
            {
                b.Append("<b>ORBIT CAMERA</b>  distance ").Append(orbitDistance.ToString("0.0")).Append(" m  fov ").Append(fov.ToString("0"));
                if (slow < 1f) b.Append("  slow x").Append(slow.ToString("0.##"));
                b.Append("\n<size=80%>mouse orbit  wheel distance  RMB+wheel zoom  Shift+F6 exit  F7 record  F8 slow motion</size>");
            }
            if (playing && playShot != null) b.Append("\nplaying ").Append(playName ?? "path").Append(' ').Append(playTime.ToString("0.0")).Append(" / ").Append(PromoCameraPolicy.Duration(playShot).ToString("0.0")).Append(" s");
            if (message != null && Time.realtimeSinceStartup < messageUntil) b.Append(b.Length > 0 ? "\n" : "").Append("<color=#FFD27A>").Append(message).Append("</color>");
            label.text = b.ToString();
        }

        bool EnsureOverlay()
        {
            if (canvas != null && label != null) return true;
            TMP_FontAsset font = null;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
                if (t != null && t.font != null) { font = t.font; break; }
            if (font == null) return false;
            var go = new GameObject("RoadworthyCameraOverlay");
            UnityEngine.Object.DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            label = textGo.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 22f;
            label.color = Color.white;
            label.richText = true;
            label.raycastTarget = false;
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(0, 0, 0, 255);
            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -24f);
            rect.sizeDelta = new Vector2(1500f, 300f);
            return true;
        }

        // ---- Recording ---------------------------------------------------

        void StartRecording(int fps, int size, string name)
        {
            if (recording) return;
            if (InSession()) { Say("Recording is single-player only (it slows the game to capture every frame)"); return; }
            string root = Root();
            if (root == null) { Say("No Documents folder to save clips in"); return; }
            try
            {
                clipDir = Path.Combine(Path.Combine(root, "clips"), PromoCameraPolicy.ClipFolder(DateTime.Now, name));
                Directory.CreateDirectory(clipDir);
            }
            catch (Exception e) { Say("Could not create the clip folder: " + e.Message); return; }
            captureFps = Mathf.Clamp(fps, PromoCameraPolicy.MinFps, PromoCameraPolicy.MaxFps);
            superSize = Mathf.Clamp(size, 1, 4);
            frames = 0; writeError = null;
            savedCaptureRate = Time.captureFramerate;
            Time.captureFramerate = captureFps;
            HideHud(true);
            recording = true;
            StartCoroutine(CaptureLoop());
            Debug.Log("[Roadworthy camera] recording " + captureFps + " fps to " + clipDir);
            WriteStatus("recording");
        }

        IEnumerator CaptureLoop()
        {
            var end = new WaitForEndOfFrame();
            // Two frames for the hidden HUD to leave the screen.
            yield return null; yield return null;
            while (recording)
            {
                yield return end;
                if (!recording) break;
                try { CaptureFrame(Path.Combine(clipDir, PromoCameraPolicy.FrameName(frames + 1))); frames++; }
                catch (Exception e) { writeError = e.Message; recording = false; Debug.LogWarning("[Roadworthy camera] capture failed: " + e.Message); }
            }
        }

        void CaptureFrame(string path)
        {
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture(superSize);
            byte[] data;
            try { data = tex.EncodeToJPG(95); }
            finally { UnityEngine.Object.Destroy(tex); }
            if (PromoCameraPolicy.MustWaitForWrites(Interlocked.CompareExchange(ref pending, 0, 0))) { File.WriteAllBytes(path, data); return; }
            Interlocked.Increment(ref pending);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { File.WriteAllBytes(path, data); }
                catch (Exception e) { writeError = e.Message; }
                finally { Interlocked.Decrement(ref pending); }
            });
        }

        void StopRecording()
        {
            if (!recording && clipDir == null) return;
            recording = false;
            Time.captureFramerate = savedCaptureRate;
            if (hudHidden && !overlayHidden) HideHud(false);
            var wait = Stopwatch.StartNew();
            while (Interlocked.CompareExchange(ref pending, 0, 0) > 0 && wait.ElapsedMilliseconds < 15000) Thread.Sleep(5);
            string dir = clipDir;
            clipDir = null;
            if (dir == null) return;
            lastClip = dir;
            string args = PromoCameraPolicy.FfmpegArguments(captureFps);
            string ffmpeg = FindFfmpeg();
            try
            {
                File.WriteAllText(Path.Combine(dir, "encode.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\n\"" + (ffmpeg ?? "ffmpeg") + "\" " + args + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception) { }
            bool encoding = false;
            if (ffmpeg != null && frames > 0)
            {
                try
                {
                    var info = new ProcessStartInfo(ffmpeg, args) { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
                    Process.Start(info);
                    encoding = true;
                }
                catch (Exception e) { Debug.LogWarning("[Roadworthy camera] ffmpeg did not start: " + e.Message); }
            }
            Say("Saved " + frames + " frames (" + (frames / (float)captureFps).ToString("0.0") + " s)" + (encoding ? ", encoding clip.mp4" : ", run encode.cmd to make clip.mp4") + (writeError != null ? " - write error: " + writeError : ""));
            Debug.Log("[Roadworthy camera] clip folder " + dir);
            WriteStatus("saved");
        }

        // TLD_MP_FFMPEG, then "ffmpeg=" in roadworthy-camera\camera.txt, then PATH.
        static string FindFfmpeg()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("TLD_MP_FFMPEG");
                if (!String.IsNullOrEmpty(env) && File.Exists(env)) return env;
                string root = Root();
                string config = root == null ? null : Path.Combine(root, "camera.txt");
                if (config != null && File.Exists(config))
                    foreach (string raw in File.ReadAllLines(config))
                    {
                        string line = raw.Trim();
                        if (!line.StartsWith("ffmpeg=", StringComparison.OrdinalIgnoreCase)) continue;
                        string path = line.Substring(7).Trim().Trim('"');
                        if (File.Exists(path)) return path;
                    }
                string paths = Environment.GetEnvironmentVariable("PATH");
                if (paths != null)
                    foreach (string dir in paths.Split(Path.PathSeparator))
                    {
                        if (dir.Length == 0) continue;
                        string candidate = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
            }
            catch (Exception) { }
            return null;
        }

        IEnumerator Snap()
        {
            string root = Root();
            if (root == null || snapping || recording) yield break;
            snapping = true;
            bool hid = !hudHidden;
            if (hid) HideHud(true);
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            string path = null;
            try
            {
                string dir = Path.Combine(root, "clips");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "snap_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss_fff") + ".jpg");
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture(1);
                try { File.WriteAllBytes(path, tex.EncodeToJPG(90)); }
                finally { UnityEngine.Object.Destroy(tex); }
            }
            catch (Exception e) { Say("Snapshot failed: " + e.Message); path = null; }
            if (hid) HideHud(false);
            snapping = false;
            if (path != null) { lastClip = path; Say("Snapshot " + Path.GetFileName(path)); }
            WriteStatus("snap");
        }

        // ---- Remote commands (command.txt -> status.txt) ----------------

        void PollCommands()
        {
            string root = Root();
            if (root == null) return;
            string path = Path.Combine(root, "command.txt");
            string text;
            try
            {
                if (!File.Exists(path)) return;
                text = File.ReadAllText(path);
                File.Delete(path);
            }
            catch (Exception) { return; }
            string result = "ok";
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#")) continue;
                string verb, arg;
                if (!PromoCameraPolicy.ParseCommand(raw, out verb, out arg)) { result = "unknown command: " + raw.Trim(); Say(result); continue; }
                Run(verb, arg);
            }
            WriteStatus(result);
        }

        void Run(string verb, string arg)
        {
            float num;
            switch (verb)
            {
                case "free": SetMode(Mode.Free); break;
                case "orbit": SetMode(Mode.Orbit); break;
                case "off": StopPlay(false); SetMode(Mode.Off); break;
                case "play":
                case "record":
                {
                    CamShot shot = LoadShot(arg);
                    if (shot == null) break;
                    if (playing) StopPlay(false);
                    StartPlay(shot, arg ?? "last", verb == "record" || shot.Record);
                    break;
                }
                case "rec": if (!recording) StartRecording(60, 1, "free"); break;
                case "stop": StopPlay(false); if (recording) StopRecording(); break;
                case "snap": StartCoroutine(Snap()); break;
                case "hud":
                    // hud text: hide everything but item text and prompts; hud off: hide all; hud on: normal.
                    if (hudHidden) HideHud(false);
                    keepText = arg == "text";
                    if (arg != "on") HideHud(true);
                    break;
                case "slowmo": if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out num)) SetSlow(num); break;
                case "fov": if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out num)) targetFov = PromoCameraPolicy.ClampFov(num); break;
                case "save": SaveShot(arg, edit); Say("Saved shot " + arg); break;
                case "load": { CamShot shot = LoadShot(arg); if (shot != null) { edit = shot; Say("Loaded " + arg + " (" + shot.Keys.Count + " keys)"); } break; }
                case "clear": edit.Keys.Clear(); Say("Keys cleared"); break;
                case "newworld": StartCoroutine(NewWorld(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture))); break;
                case "menu": GoToMenu(); break;
                case "quit": Say("Quitting the game"); ShutDown(); RestoreAutoSave(); Application.Quit(); break;
                case "time":
                    if (arg == "run") { frozenDay = float.NaN; Say("Time of day runs"); }
                    else if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out num)) { frozenDay = num; KeepTime(); Say("Time of day held at " + num.ToString("0.00")); }
                    break;
                case "sit": Sit(); break;
                case "biomescan":
                {
                    int radius, step;
                    if (PromoCameraPolicy.TryScan(arg, out radius, out step)) BiomeScan(radius, step);
                    break;
                }
                case "teleport":
                {
                    double tx, tz;
                    var tp = Player();
                    if (!PromoCameraPolicy.TryTeleport(arg, out tx, out tz) || tp == null) { Say("teleport needs a loaded world"); break; }
                    if (tp.seat != null) { Say("Get out of the car before teleporting"); break; }
                    teleportX = tx; teleportZ = tz; teleportUntil = Time.realtimeSinceStartup + 60f;
                    teleportHoldY = mainscript.GlobalFromUnityPos(tp.transform.position).y + 50.0;
                    Say("Teleporting to " + tx.ToString("0") + ", " + tz.ToString("0") + " (waiting for ground)");
                    WriteStatus("teleporting");
                    break;
                }
                case "camera":
                {
                    float[] pose;
                    if (!PromoCameraPolicy.TryCameraPose(arg, out pose) || Player() == null) { Say("camera needs a loaded world"); break; }
                    if (playing) StopPlay(false);
                    if (mode != Mode.Free) SetMode(Mode.Free);
                    if (anchor != null) anchor.position = WorldPoint(pose[0], pose[1], pose[2]);
                    lastPosition = WorldPoint(pose[0], pose[1], pose[2]);
                    targetYaw = yaw = pose[3]; targetPitch = pitch = pose[4]; targetRoll = roll = pose[5];
                    if (!float.IsNaN(pose[6])) targetFov = fov = pose[6];
                    velocity = Vector3.zero;
                    break;
                }
            }
        }

        // ---- Automation (new world, time of day, seat) -------------------

        IEnumerator NewWorld(int seed)
        {
            if (loadingWorld) { Say("A world is already loading"); yield break; }
            if (InSession()) { Say("newworld is single-player only"); yield break; }
            loadingWorld = true;
            float deadline = Time.realtimeSinceStartup + 120f;
            if (DataFromMenuScript.s == null || !DataFromMenuScript.s.mainmenu)
            {
                GoToMenu();
                for (int i = 0; i < 30; i++) yield return null;
            }
            while ((menuhandler.s == null || DataFromMenuScript.s == null || !DataFromMenuScript.s.mainmenu) && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i = 0; i < 120; i++) yield return null;
            if (menuhandler.s == null || DataFromMenuScript.s == null) { loadingWorld = false; Say("newworld: no main menu"); WriteStatus("newworld failed"); yield break; }
            var previous = mainscript.s;
            DataFromMenuScript.s.seed = seed;
            Say("Starting a new world, seed " + seed);
            menuhandler.s.PressedStart();
            deadline = Time.realtimeSinceStartup + 180f;
            for (int i = 0; i < 5; i++) yield return null;
            while ((mainscript.s == null || ReferenceEquals(mainscript.s, previous) || mainscript.s.player == null || menuhandler.s == null || menuhandler.s.currentMainMap == null) && Time.realtimeSinceStartup < deadline) yield return null;
            loadingWorld = false;
            if (mainscript.s == null || mainscript.s.player == null) { Say("newworld: the world never loaded"); WriteStatus("newworld failed"); yield break; }
            // A throwaway world: never autosave over anything.
            try
            {
                if (settingsscript.s != null && settingsscript.s.S != null)
                {
                    if (!autoSaveChanged) { savedAutoSave = settingsscript.s.S.BAutoSave; autoSaveChanged = true; }
                    settingsscript.s.S.BAutoSave = false;
                }
            }
            catch (Exception) { }
            // Let the start house, the car and the nearby terrain settle.
            float settle = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            Say("World ready, seed " + seed);
            WriteStatus("world ready");
        }

        void GoToMenu()
        {
            if (menuhandler.s == null) return;
            StopPlay(false);
            if (recording) StopRecording();
            ShutDown();
            frozenDay = float.NaN;
            RestoreAutoSave();
            Say("Back to the main menu (nothing saved)");
            menuhandler.s.PressedMainMenu();
        }

        void RestoreAutoSave()
        {
            if (!autoSaveChanged) return;
            try { if (settingsscript.s != null && settingsscript.s.S != null) settingsscript.s.S.BAutoSave = savedAutoSave; } catch (Exception) { }
            autoSaveChanged = false;
        }

        static napszakvaltakozas Day() { return napszakvaltakozas.s; }

        static float DayFraction()
        {
            var d = Day();
            if (d == null || !(d.dt + d.nt > 0f)) return float.NaN;
            return Mathf.Clamp01(d.t / (d.dt + d.nt));
        }

        // Holds the time of day for repeatable lighting (the game's own dev
        // time-skip moves the same offset, napszakvaltakozas.tekeres).
        void KeepTime()
        {
            if (float.IsNaN(frozenDay)) return;
            var d = Day();
            if (d == null || InSession()) return;
            float t = d.time + d.tekeres - d.startTime;
            d.tekeres += PromoCameraPolicy.TimeShift(t, d.dt + d.nt, frozenDay);
        }

        // Holds the player above the target until the near terrain there has a
        // collider, then sets them on it (Teleport alone would drop them through
        // ground that has not been built yet).
        double teleportX, teleportZ, teleportHoldY;
        float teleportUntil;
        static readonly RaycastHit[] teleportHits = new RaycastHit[16];

        void KeepTeleport(fpscontroller p)
        {
            if (teleportUntil <= 0f) return;
            if (Time.realtimeSinceStartup > teleportUntil) { teleportUntil = 0f; Say("Teleport gave up: no ground at the target yet"); WriteStatus("teleport failed"); return; }
            Vector3 top = mainscript.UnityPosFromGlobal(new Vector3d(teleportX, 5000.0, teleportZ));
            int n = Physics.RaycastNonAlloc(new Ray(top, Vector3.down), teleportHits, 10000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity; bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = teleportHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(p.transform)) continue;
                if (!(h.collider is MeshCollider)) continue;
                if (h.point.y > best) { best = h.point.y; found = true; }
            }
            var body = p.GetComponent<Rigidbody>();
            if (found)
            {
                teleportUntil = 0f;
                Vector3 at = new Vector3(top.x, best + 1.2f, top.z);
                p.Teleport(at);
                if (body != null) body.velocity = Vector3.zero;
                Say("Teleported");
                WriteStatus("teleported");
                return;
            }
            p.Teleport(mainscript.UnityPosFromGlobal(new Vector3d(teleportX, teleportHoldY, teleportZ)));
            if (body != null) body.velocity = Vector3.zero;
        }

        // Samples the world's own biome noise (terrainGenSettings.Biome, the colour
        // every terrain tile gets) on a grid around the player and writes
        // biome-scan.txt: a letter map (north up) and the nearest native-grass cells
        // that touch desert. Read-only.
        void BiomeScan(int radius, int step)
        {
            var p = Player();
            var map = menuhandler.s == null ? null : menuhandler.s.currentMainMap;
            if (p == null || map == null || map.terrainGenSettings == null || map.terrainGenSettings.Count == 0) { Say("biomescan needs a loaded world"); WriteStatus("biomescan failed"); return; }
            var settings = map.terrainGenSettings[0];
            Vector3d centre = mainscript.GlobalFromUnityPos(p.transform.position);
            int n = radius / step, side = 2 * n + 1;
            var cells = new char[side, side];
            var watch = Stopwatch.StartNew();
            int grass = 0, borders = 0;
            for (int ix = 0; ix < side; ix++)
                for (int iz = 0; iz < side; iz++)
                {
                    var g = new Vector3d(centre.x + (ix - n) * (double)step, 0, centre.z + (iz - n) * (double)step);
                    Color c = settings.Biome(g);
                    char letter = PromoCameraPolicy.BiomeLetter(c.r, c.g, c.b, c.a);
                    cells[ix, iz] = letter;
                    if (letter == 'G') grass++;
                }
            var found = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<double, string>>();
            for (int ix = 0; ix < side; ix++)
                for (int iz = 0; iz < side; iz++)
                    if (PromoCameraPolicy.GrassBorder(cells, ix, iz))
                    {
                        borders++;
                        double dx = (ix - n) * (double)step, dz = (iz - n) * (double)step;
                        found.Add(new System.Collections.Generic.KeyValuePair<double, string>(Math.Sqrt(dx * dx + dz * dz), (centre.x + dx).ToString("0") + " " + (centre.z + dz).ToString("0")));
                    }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var b2 = new StringBuilder();
            b2.Append("# biomescan centre=").Append(centre.x.ToString("0", inv)).Append(' ').Append(centre.z.ToString("0", inv)).Append(" radius=").Append(radius).Append(" step=").Append(step)
              .Append(" ms=").Append(watch.ElapsedMilliseconds).Append(" grass=").Append(grass).Append(" borders=").Append(borders).Append('\n');
            b2.Append("# nearest native-grass cells touching desert (global x z, distance m):\n");
            for (int i = 0; i < found.Count && i < 40; i++) b2.Append("border ").Append(found[i].Value).Append(' ').Append(found[i].Key.ToString("0", inv)).Append('\n');
            b2.Append("# map: G native grass, g green (no blades in base game), D desert, S snow, . other; row 0 = north, column 0 = west\n");
            for (int iz = side - 1; iz >= 0; iz--)
            {
                for (int ix = 0; ix < side; ix++) b2.Append(ix == n && iz == n ? '@' : cells[ix, iz]);
                b2.Append('\n');
            }
            try { File.WriteAllText(Path.Combine(Root(), "biome-scan.txt"), b2.ToString(), new UTF8Encoding(false)); }
            catch (Exception e) { Say("biomescan write failed: " + e.Message); }
            Say("Biome scan: " + grass + " grass cells, " + borders + " grass/desert borders (" + watch.ElapsedMilliseconds + " ms)");
            WriteStatus("biomescan done");
        }

        void Sit()
        {
            var p = Player();
            if (p == null) { Say("sit needs a loaded world"); return; }
            if (p.seat != null) { Say("Already seated"); return; }
            var seats = UnityEngine.Object.FindObjectsOfType<seatscript>();
            var dist = new float[seats.Length]; var driver = new bool[seats.Length]; var inCar = new bool[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                var seat = seats[i];
                inCar[i] = seat != null && seat.Car != null && !seat.inUse;
                driver[i] = seat != null && seat.driverSeat0;
                dist[i] = seat == null ? float.NaN : Vector3.Distance(seat.transform.position, p.transform.position);
            }
            int pick = PromoCameraPolicy.PickSeat(dist, driver, inCar, 60f);
            if (pick < 0) { Say("No free car seat within 60 m"); return; }
            try { p.GetIn(seats[pick]); Say("Seated in " + seats[pick].transform.root.name); }
            catch (Exception e) { Say("Could not sit: " + e.GetType().Name); }
        }

        void WriteStatus(string result)
        {
            if (result != null) lastResult = result;
            WriteStatusNow();
        }

        void WriteStatusNow()
        {
            try
            {
                string root = Root();
                if (root == null) return;
                Directory.CreateDirectory(root);
                var p = Player();
                var b = new StringBuilder();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                b.Append("time=").Append(DateTime.Now.ToString("s", inv)).Append('\n');
                b.Append("result=").Append(lastResult).Append('\n');
                b.Append("mode=").Append(mode.ToString().ToLowerInvariant()).Append('\n');
                b.Append("playing=").Append(playing ? (playName ?? "path") : "no").Append('\n');
                b.Append("recording=").Append(recording ? clipDir : "no").Append('\n');
                b.Append("frames=").Append(frames).Append('\n');
                b.Append("last=").Append(lastClip ?? "").Append('\n');
                b.Append("slowmo=").Append(slow.ToString("0.##", inv)).Append('\n');
                b.Append("keys=").Append(edit.Keys.Count).Append(' ').Append(edit.Space == ShotSpace.Car ? "car" : "world").Append('\n');
                b.Append("multiplayer=").Append(InSession() ? "yes" : "no").Append('\n');
                b.Append("menu=").Append(DataFromMenuScript.s != null && DataFromMenuScript.s.mainmenu ? "yes" : "no").Append(" loading=").Append(loadingWorld ? "yes" : "no").Append('\n');
                b.Append("build=").Append(ForceOn ? "vanilla+camera" : "roadworthy").Append('\n');
                float day = DayFraction();
                b.Append("day=").Append(float.IsNaN(day) ? "" : day.ToString("0.000", inv)).Append(float.IsNaN(frozenDay) ? " running" : " held").Append('\n');
                if (p != null)
                {
                    b.Append("seated=").Append(p.seat != null ? "yes" : "no").Append('\n');
                    var mainNow = mainscript.s;
                    if (p.Cam != null)
                    {
                        Vector3d cg = mainscript.GlobalFromUnityPos(p.Cam.transform.position);
                        Vector3 ce = p.Cam.transform.rotation.eulerAngles;
                        b.Append("camera_global=").Append(cg.x.ToString("0.##", inv)).Append(' ').Append(cg.y.ToString("0.##", inv)).Append(' ').Append(cg.z.ToString("0.##", inv))
                         .Append(' ').Append(ce.y.ToString("0.#", inv)).Append(' ').Append(Signed(ce.x).ToString("0.#", inv)).Append(' ').Append(Signed(ce.z).ToString("0.#", inv)).Append(' ').Append(p.Cam.fieldOfView.ToString("0", inv)).Append('\n');
                    }
                    Vector3d pg = mainscript.GlobalFromUnityPos(p.transform.position);
                    b.Append("player_global=").Append(pg.x.ToString("0.##", inv)).Append(' ').Append(pg.y.ToString("0.##", inv)).Append(' ').Append(pg.z.ToString("0.##", inv)).Append(' ').Append(p.transform.eulerAngles.y.ToString("0.#", inv)).Append('\n');
                    if (mainNow != null) b.Append("spawn_global=").Append(mainNow.playerSpawnStartPos.x.ToString("0.##", inv)).Append(' ').Append(mainNow.playerSpawnStartPos.y.ToString("0.##", inv)).Append(' ').Append(mainNow.playerSpawnStartPos.z.ToString("0.##", inv)).Append('\n');
                    Transform target = Target(p);
                    b.Append("target=").Append(target == null ? "" : target.name).Append('\n');
                    if (target != null && p.Cam != null)
                    {
                        Quaternion frame = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
                        Quaternion invFrame = Quaternion.Inverse(frame);
                        Vector3 local = invFrame * (p.Cam.transform.position - target.position);
                        Vector3 e = (invFrame * p.Cam.transform.rotation).eulerAngles;
                        b.Append("camera_car_space=").Append(local.x.ToString("0.##", inv)).Append(' ').Append(local.y.ToString("0.##", inv)).Append(' ').Append(local.z.ToString("0.##", inv))
                         .Append(' ').Append(e.y.ToString("0.#", inv)).Append(' ').Append(Signed(e.x).ToString("0.#", inv)).Append(' ').Append(Signed(e.z).ToString("0.#", inv))
                         .Append(' ').Append(p.Cam.fieldOfView.ToString("0", inv)).Append('\n');
                        Rigidbody body = target.GetComponent<Rigidbody>();
                        if (body != null) b.Append("target_speed_kmh=").Append((body.velocity.magnitude * 3.6f).ToString("0", inv)).Append('\n');
                    }
                }
                File.WriteAllText(Path.Combine(root, "status.txt"), b.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE automated gate harness for PHASE 07.1.
    ///
    /// Drives the real input path with simulated device events and records measured
    /// results so each gate has evidence rather than an assertion. Test-only: it is
    /// not part of the controller architecture.
    /// </summary>
    public sealed class ProtoAutoTest : MonoBehaviour
    {
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private ProtoThirdPersonCamera cameraRig;
        [SerializeField] private ProtoCharacterSwitcher switcher;
        [SerializeField] private ProtoCameraSettings cameraSettings;
        [SerializeField] private ProtoMovementSettings movementSettings;

        public static readonly List<string> Results = new List<string>();
        public static bool Done;
        public static bool Running;

        private Keyboard _kb;
        private Mouse _mouse;
        private GameObject _wall;

        private InputSettings.EditorInputBehaviorInPlayMode _prevEditorBehavior;
        private InputSettings.BackgroundBehavior _prevBackgroundBehavior;
        private bool _behaviorOverridden;

        private static void Log(string s) { Results.Add(s); }

        private void Awake()
        {
            Application.runInBackground = true;

            // The editor gates keyboard/mouse action state on Game View focus, which
            // would silently zero every simulated event while running unattended.
            // InputSystem.settings here is an in-memory default object (HideAndDontSave),
            // so this overrides nothing on disk. Restored in OnDestroy.
            _prevEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            _prevBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior =
                InputSettings.BackgroundBehavior.IgnoreFocus;
            _behaviorOverridden = true;

            // Pin the simulated frame rate so every hold below is a known duration.
            // Unpinned, the editor runs at several hundred fps and frame-count based
            // holds become far too short to measure anything meaningful.
            Time.captureFramerate = 60;
        }

        /// <summary>Releases input and waits until the character has genuinely stopped.</summary>
        private IEnumerator SettleToRest()
        {
            ClearKeys();
            for (int i = 0; i < 240; i++)
            {
                yield return null;
                if (CtrlSpeed <= 0.0005f) break;
            }
            yield return Frames(3);
        }

        private void Start()
        {
            Results.Clear();
            Done = false;
            Running = true;
            _kb = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            if (_kb != null && _kb.added) InputSystem.RemoveDevice(_kb);
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            if (_wall != null) Destroy(_wall);

            if (_behaviorOverridden)
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = _prevEditorBehavior;
                InputSystem.settings.backgroundBehavior = _prevBackgroundBehavior;
                _behaviorOverridden = false;
            }
            Time.captureFramerate = 0;
        }

        // ---------- simulated device helpers ----------

        private void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_kb, new KeyboardState(keys));
        }

        private void ClearKeys()
        {
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
        }

        private void MouseDelta(Vector2 d)
        {
            MouseState s = new MouseState();
            s.delta = d;
            InputSystem.QueueStateEvent(_mouse, s);
        }

        private void MouseScroll(float y)
        {
            MouseState s = new MouseState();
            s.scroll = new Vector2(0f, y);
            InputSystem.QueueStateEvent(_mouse, s);
        }

        private IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Holds a key set for n frames, re-queuing each frame so the state persists.</summary>
        private IEnumerator HoldKeys(int n, params Key[] keys)
        {
            for (int i = 0; i < n; i++)
            {
                Keys(keys);
                yield return null;
            }
        }

        private IEnumerator HoldMouse(int n, Vector2 delta)
        {
            for (int i = 0; i < n; i++)
            {
                MouseDelta(delta);
                yield return null;
            }
        }

        private IEnumerator HoldScroll(int n, float y)
        {
            for (int i = 0; i < n; i++)
            {
                MouseScroll(y);
                yield return null;
            }
        }

        private static bool Bad(Vector3 v)
        {
            return float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z);
        }

        private Transform ActiveT
        {
            get { return switcher != null && switcher.Active != null ? switcher.Active.transform : null; }
        }

        /// <summary>The controller on whichever character is currently active.</summary>
        private ProtoThirdPersonController controller
        {
            get
            {
                GameObject a = switcher != null ? switcher.Active : null;
                return a != null ? a.GetComponent<ProtoThirdPersonController>() : null;
            }
        }

        private float CtrlSpeed { get { ProtoThirdPersonController c = controller; return c != null ? c.CurrentPlanarSpeed : -1f; } }
        private bool CtrlGrounded { get { ProtoThirdPersonController c = controller; return c != null && c.IsGrounded; } }

        // ---------- the run ----------

        private IEnumerator Run()
        {
            yield return Frames(5);

            yield return InputGate();
            yield return MovementGate();
            yield return CameraGate();
            yield return CharacterGate("MALE", 0);
            yield return CharacterGate("FEMALE", 1);
            yield return SharedGate();

            Log("=== HARNESS COMPLETE ===");
            Running = false;
            Done = true;
        }

        // ---------- STEP 3: input ----------

        private IEnumerator InputGate()
        {
            Log("=== GATE 3: INPUT ===");

            string[] names = { "W", "A", "S", "D", "W+D", "W+A", "S+D", "S+A" };
            Key[][] sets = {
                new[] { Key.W }, new[] { Key.A }, new[] { Key.S }, new[] { Key.D },
                new[] { Key.W, Key.D }, new[] { Key.W, Key.A },
                new[] { Key.S, Key.D }, new[] { Key.S, Key.A }
            };

            for (int i = 0; i < names.Length; i++)
            {
                yield return HoldKeys(4, sets[i]);
                Vector2 v = input.Move;
                Log("  key " + names[i].PadRight(4) + " -> Move=" + v.ToString("F3") + " |v|=" + v.magnitude.ToString("F3"));
            }

            ClearKeys();
            yield return Frames(4);
            Log("  released -> Move=" + input.Move.ToString("F3"));

            yield return HoldMouse(3, new Vector2(15f, 0f));
            Log("  mouse X +15 -> Look=" + input.Look.ToString("F3"));
            yield return HoldMouse(3, new Vector2(-15f, 0f));
            Log("  mouse X -15 -> Look=" + input.Look.ToString("F3"));
            yield return HoldMouse(3, new Vector2(0f, 12f));
            Log("  mouse Y +12 -> Look=" + input.Look.ToString("F3"));
            yield return HoldMouse(3, new Vector2(0f, -12f));
            Log("  mouse Y -12 -> Look=" + input.Look.ToString("F3"));
            MouseDelta(Vector2.zero);
            yield return Frames(3);

            yield return HoldScroll(3, 120f);
            Log("  wheel +120 -> Zoom=" + input.Zoom.ToString("F3"));
            yield return HoldScroll(3, -120f);
            Log("  wheel -120 -> Zoom=" + input.Zoom.ToString("F3"));
            MouseScroll(0f);
            yield return Frames(3);
        }

        // ---------- STEP 4: movement ----------

        /// <summary>Forces the camera yaw by feeding mouse delta through the real input path.</summary>
        private IEnumerator SetCameraYaw(float targetYaw)
        {
            // Alternate a flush frame with a corrective frame. Queuing a delta every
            // frame overshoots, because the delta queued on the frame the loop exits
            // is still applied afterwards.
            for (int guard = 0; guard < 240; guard++)
            {
                MouseDelta(Vector2.zero);
                yield return null;

                float diff = Mathf.DeltaAngle(cameraRig.Yaw, targetYaw);
                if (Mathf.Abs(diff) < 0.25f) break;

                float step = Mathf.Clamp(diff / cameraSettings.orbitSensitivityX, -300f, 300f);
                MouseDelta(new Vector2(step, 0f));
                yield return null;
            }
            MouseDelta(Vector2.zero);
            yield return Frames(3);
        }

        /// <summary>Drives camera pitch to a target. Note +Y mouse lowers pitch.</summary>
        private IEnumerator SetCameraPitch(float targetPitch)
        {
            for (int guard = 0; guard < 240; guard++)
            {
                MouseDelta(Vector2.zero);
                yield return null;

                float diff = cameraRig.Pitch - targetPitch;
                if (Mathf.Abs(diff) < 0.25f) break;

                float step = Mathf.Clamp(diff / cameraSettings.orbitSensitivityY, -300f, 300f);
                MouseDelta(new Vector2(0f, step));
                yield return null;
            }
            MouseDelta(Vector2.zero);
            yield return Frames(3);
        }

        /// <summary>Drives zoom to a target distance via the wheel.</summary>
        private IEnumerator SetCameraDistance(float targetDistance)
        {
            for (int guard = 0; guard < 240; guard++)
            {
                MouseScroll(0f);
                yield return null;

                float diff = cameraRig.DesiredDistance - targetDistance;
                if (Mathf.Abs(diff) < 0.02f) break;

                float step = Mathf.Clamp(diff / cameraSettings.zoomSensitivity, -600f, 600f);
                MouseScroll(step);
                yield return null;
            }
            MouseScroll(0f);
            yield return Frames(20);
        }

        private IEnumerator MeasureMove(string label, float camYaw, Key[] keys, Vector2 expectedLocal)
        {
            // Fully stop first, so leftover velocity from the previous case cannot
            // bend the measured direction of this one.
            yield return SettleToRest();
            yield return SetCameraYaw(camYaw);

            Transform t = ActiveT;
            t.position = new Vector3(0f, t.position.y, 0f);
            yield return Frames(6);

            // Reach steady state before timing, then measure a clean 1.0s window.
            yield return HoldKeys(45, keys);
            float actualYaw = cameraRig.Yaw;
            Vector3 start = t.position;
            yield return HoldKeys(60, keys);   // 60 frames @ captureFramerate 60 = 1.00 s
            Vector3 end = t.position;
            yield return SettleToRest();

            Vector3 delta = end - start;
            Vector3 planar = new Vector3(delta.x, 0f, delta.z);

            // Movement must be relative to where the camera ACTUALLY is, so the
            // expectation is built from the measured yaw, not the requested one.
            Quaternion camRot = Quaternion.Euler(0f, actualYaw, 0f);
            Vector3 expectedWorld = camRot * new Vector3(expectedLocal.x, 0f, expectedLocal.y);
            if (expectedWorld.sqrMagnitude > 0.0001f) expectedWorld.Normalize();

            float angleErr = (planar.sqrMagnitude > 1e-6f && expectedWorld.sqrMagnitude > 1e-6f)
                ? Vector3.Angle(planar.normalized, expectedWorld) : -1f;

            Log("  " + label.PadRight(20)
                + " camYaw req=" + camYaw.ToString("F0").PadLeft(4) + " actual=" + cameraRig.Yaw.ToString("F1").PadLeft(6)
                + " | 1.0s dist=" + planar.magnitude.ToString("F3") + "m (walkSpeed=" + movementSettings.walkSpeed.ToString("F2") + ")"
                + " dirErr=" + angleErr.ToString("F2") + "deg"
                + " dY=" + delta.y.ToString("F4")
                + " NaN=" + Bad(end)
                + " grounded=" + CtrlGrounded);
        }

        private IEnumerator MovementGate()
        {
            Log("=== GATE 4: MOVEMENT (camera-relative) ===");

            yield return MeasureMove("1 W", 0f, new[] { Key.W }, new Vector2(0f, 1f));
            yield return MeasureMove("2 S", 0f, new[] { Key.S }, new Vector2(0f, -1f));
            yield return MeasureMove("3 A", 0f, new[] { Key.A }, new Vector2(-1f, 0f));
            yield return MeasureMove("4 D", 0f, new[] { Key.D }, new Vector2(1f, 0f));
            yield return MeasureMove("5 W+A", 0f, new[] { Key.W, Key.A }, new Vector2(-0.7071f, 0.7071f));
            yield return MeasureMove("6 W+D", 0f, new[] { Key.W, Key.D }, new Vector2(0.7071f, 0.7071f));
            yield return MeasureMove("7 S+A", 0f, new[] { Key.S, Key.A }, new Vector2(-0.7071f, -0.7071f));
            yield return MeasureMove("8 S+D", 0f, new[] { Key.S, Key.D }, new Vector2(0.7071f, -0.7071f));
            yield return MeasureMove("9 W camYaw90", 90f, new[] { Key.W }, new Vector2(0f, 1f));
            yield return MeasureMove("10 W camYaw180", 180f, new[] { Key.W }, new Vector2(0f, 1f));
            yield return MeasureMove("15 W+D camYaw45", 45f, new[] { Key.W, Key.D }, new Vector2(0.7071f, 0.7071f));

            // 11: camera rotating continuously while moving
            yield return SetCameraYaw(0f);
            Transform t = ActiveT;
            t.position = new Vector3(0f, t.position.y, 0f);
            yield return Frames(5);
            bool nan = false;
            float maxStep = 0f;
            Vector3 prev = t.position;
            for (int i = 0; i < 90; i++)
            {
                Keys(Key.W);
                MouseDelta(new Vector2(20f, 0f));
                yield return null;
                float step = (t.position - prev).magnitude;
                if (step > maxStep) maxStep = step;
                prev = t.position;
                if (Bad(t.position)) nan = true;
            }
            ClearKeys();
            MouseDelta(Vector2.zero);
            yield return Frames(20);
            Log("  11 continuous camera rotation while moving: maxFrameStep=" + maxStep.ToString("F4")
                + "m NaN=" + nan + " finalY=" + t.position.y.ToString("F4"));

            // 12/13: start and stop
            t.position = new Vector3(0f, t.position.y, 0f);
            yield return Frames(5);
            yield return HoldKeys(3, Key.W);
            float earlySpeed = CtrlSpeed;
            yield return HoldKeys(40, Key.W);
            float fullSpeed = CtrlSpeed;
            ClearKeys();
            yield return Frames(3);
            float decelSpeed = CtrlSpeed;
            yield return Frames(40);
            float stoppedSpeed = CtrlSpeed;
            Vector3 restA = t.position;
            yield return Frames(30);
            float drift = (t.position - restA).magnitude;
            Log("  12/13 start->stop: after3f=" + earlySpeed.ToString("F3")
                + " sustained=" + fullSpeed.ToString("F3")
                + " (walkSpeed=" + movementSettings.walkSpeed.ToString("F2") + ")"
                + " 3f-after-release=" + decelSpeed.ToString("F3")
                + " stopped=" + stoppedSpeed.ToString("F4")
                + " idleDrift30f=" + drift.ToString("F5") + "m");

            // 14: reverse direction
            float yawBefore = t.eulerAngles.y;
            yield return HoldKeys(40, Key.W);
            float yawFwd = t.eulerAngles.y;
            yield return HoldKeys(60, Key.S);
            float yawBack = t.eulerAngles.y;
            ClearKeys();
            yield return Frames(20);
            Log("  14 reverse: yaw start=" + yawBefore.ToString("F1")
                + " afterW=" + yawFwd.ToString("F1") + " afterS=" + yawBack.ToString("F1")
                + " turned=" + Mathf.Abs(Mathf.DeltaAngle(yawFwd, yawBack)).ToString("F1") + "deg");

            // idle spin check
            ClearKeys();
            yield return Frames(10);
            float y0 = t.eulerAngles.y;
            yield return Frames(60);
            Log("  idle rotation over 60 frames = " + Mathf.Abs(Mathf.DeltaAngle(y0, t.eulerAngles.y)).ToString("F4") + " deg");

            // frame-rate independence
            yield return SetCameraYaw(0f);
            float d30 = 0f, d120 = 0f;
            int[] rates = { 30, 120 };
            for (int r = 0; r < rates.Length; r++)
            {
                Time.captureFramerate = rates[r];
                t.position = new Vector3(0f, t.position.y, 0f);
                yield return Frames(10);
                Vector3 s0 = t.position;
                yield return HoldKeys(rates[r], Key.W);   // exactly 1 simulated second
                Vector3 s1 = t.position;
                ClearKeys();
                yield return Frames(5);
                float dist = new Vector3(s1.x - s0.x, 0f, s1.z - s0.z).magnitude;
                if (r == 0) d30 = dist; else d120 = dist;
            }
            // Restore the 60 fps pin. Dropping to 0 here would leave every later test
            // running on an unpinned several-hundred-fps clock, making frame-count
            // based holds far too short to reach steady state.
            Time.captureFramerate = 60;
            Log("  frame-rate independence: 1s @30fps=" + d30.ToString("F4") + "m  1s @120fps="
                + d120.ToString("F4") + "m  diff=" + Mathf.Abs(d30 - d120).ToString("F4") + "m");
        }

        // ---------- STEP 5: camera ----------

        private IEnumerator CameraGate()
        {
            Log("=== GATE 5: CAMERA ===");

            // 360 orbit
            float minPitch = 999f, maxPitch = -999f;
            bool camNaN = false;
            float worstUp = 1f;
            for (int i = 0; i < 180; i++)
            {
                MouseDelta(new Vector2(20f, 0f));
                yield return null;
                if (Bad(cameraRig.transform.position)) camNaN = true;
                float up = Vector3.Dot(cameraRig.transform.up, Vector3.up);
                if (up < worstUp) worstUp = up;
            }
            MouseDelta(Vector2.zero);
            yield return Frames(3);
            Log("  360 orbit: yaw=" + cameraRig.Yaw.ToString("F1") + " NaN=" + camNaN
                + " minCameraUpDot=" + worstUp.ToString("F4") + " (>0 means never flipped)");

            // pitch clamp. +Y mouse lowers pitch, -Y raises it.
            yield return HoldMouse(120, new Vector2(0f, 40f));
            float pitchAtMin = cameraRig.Pitch;
            yield return HoldMouse(240, new Vector2(0f, -40f));
            float pitchAtMax = cameraRig.Pitch;
            MouseDelta(Vector2.zero);
            yield return Frames(3);
            Log("  pitch clamp: drivenToMin=" + pitchAtMin.ToString("F2") + " drivenToMax=" + pitchAtMax.ToString("F2")
                + " allowed=[" + cameraSettings.pitchMin.ToString("F1") + "," + cameraSettings.pitchMax.ToString("F1") + "]"
                + " withinRange=" + (pitchAtMin >= cameraSettings.pitchMin - 0.01f && pitchAtMin <= cameraSettings.pitchMax + 0.01f
                                  && pitchAtMax >= cameraSettings.pitchMin - 0.01f && pitchAtMax <= cameraSettings.pitchMax + 0.01f));

            // zoom clamp
            yield return HoldScroll(120, 400f);
            float zoomIn = cameraRig.DesiredDistance;
            yield return HoldScroll(240, -400f);
            float zoomOut = cameraRig.DesiredDistance;
            MouseScroll(0f);
            yield return Frames(3);
            Log("  zoom clamp: forcedIn=" + zoomIn.ToString("F3") + " forcedOut=" + zoomOut.ToString("F3")
                + " allowed=[" + cameraSettings.minDistance.ToString("F2") + "," + cameraSettings.maxDistance.ToString("F2") + "]"
                + " neverNegative=" + (zoomIn > 0f && zoomOut > 0f));

            // --- ground clamp: drive pitch to its MINIMUM at full zoom, which would
            // otherwise put the camera below the floor.
            Transform t = ActiveT;
            t.position = new Vector3(0f, t.position.y, 0f);
            yield return SettleToRest();
            yield return SetCameraYaw(0f);
            yield return SetCameraDistance(cameraSettings.maxDistance);
            yield return SetCameraPitch(cameraSettings.pitchMin);
            float lowestY = 999f;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                if (cameraRig.transform.position.y < lowestY) lowestY = cameraRig.transform.position.y;
            }
            float pivotY = t.position.y + cameraSettings.followHeight;
            float unclampedY = pivotY + cameraRig.CurrentDistance * Mathf.Sin(cameraSettings.pitchMin * Mathf.Deg2Rad);
            Log("  ground clamp: pitch=" + cameraRig.Pitch.ToString("F1") + " dist=" + cameraRig.CurrentDistance.ToString("F2")
                + " unclampedY would be " + unclampedY.ToString("F3")
                + " -> actual lowest Y=" + lowestY.ToString("F4")
                + " (min allowed=" + cameraSettings.minHeightAboveGround.ToString("F2")
                + ") aboveGround=" + (lowestY >= cameraSettings.minHeightAboveGround - 0.02f));

            // --- collision: put a wall between the character and the camera, with the
            // camera level and at default distance so the cast genuinely crosses it.
            yield return SetCameraPitch(10f);
            yield return SetCameraDistance(cameraSettings.defaultDistance);
            t.position = new Vector3(0f, t.position.y, 0f);
            // Let the zoom and follow smoothing fully settle before sampling, otherwise
            // the camera is measured mid-glide and the numbers mean nothing.
            yield return Frames(120);
            float freeDist = cameraRig.CurrentDistance;
            Vector3 freePos = cameraRig.transform.position;

            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "ProtoTestWall";
            _wall.transform.localScale = new Vector3(6f, 4f, 0.4f);
            // At yaw 0 the camera sits at -Z, so the wall goes behind the character.
            _wall.transform.position = new Vector3(0f, 2f, -1.4f);
            Physics.SyncTransforms();

            // Track the deepest point the camera ever reaches while the wall exists.
            float deepestZ = 999f;
            for (int i = 0; i < 150; i++)
            {
                yield return null;
                if (cameraRig.transform.position.z < deepestZ) deepestZ = cameraRig.transform.position.z;
            }
            float blockedDist = cameraRig.CurrentDistance;
            Vector3 blockedPos = cameraRig.transform.position;
            // Wall near face is at z = -1.2. Steady state must stay on the character side.
            bool beyondWall = blockedPos.z < -1.2f;
            bool everBeyondWall = deepestZ < -1.2f;

            Destroy(_wall);
            _wall = null;
            Physics.SyncTransforms();
            yield return Frames(150);
            float recoveredDist = cameraRig.CurrentDistance;

            Log("  collision: free=" + freeDist.ToString("F3") + "m camZ=" + freePos.z.ToString("F3")
                + " | blocked=" + blockedDist.ToString("F3") + "m camZ=" + blockedPos.z.ToString("F3")
                + " | recovered=" + recoveredDist.ToString("F3") + "m"
                + " | pulledIn=" + (blockedDist < freeDist - 0.05f)
                + " steadyStateBeyondWall=" + beyondWall
                + " everBeyondWall=" + everBeyondWall + " (deepestZ=" + deepestZ.ToString("F3") + ", wallFace=-1.200)"
                + " returned=" + (recoveredDist > blockedDist + 0.05f));

            // --- readability: character must be on screen and the camera behind it
            Camera c = cameraRig.GetComponent<Camera>();
            Vector3 vp = c.WorldToViewportPoint(t.position + Vector3.up * 0.6f);
            Log("  readability: character viewport=" + vp.ToString("F3")
                + " onScreen=" + (vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f)
                + " camDistFromChar=" + Vector3.Distance(c.transform.position, t.position).ToString("F2") + "m"
                + " camAboveChar=" + (c.transform.position.y - t.position.y).ToString("F2") + "m");
        }

        // ---------- STEP 6/7: per character ----------

        private IEnumerator CharacterGate(string label, int index)
        {
            Log("=== " + label + " GAMEPLAY ===");
            switcher.Activate(index);
            yield return Frames(10);

            Transform t = ActiveT;
            if (t == null) { Log("  NO ACTIVE CHARACTER"); yield break; }

            Animator anim = t.GetComponentInChildren<Animator>();
            Log("  spawned=" + t.name + " grounded=" + CtrlGrounded
                + " y=" + t.position.y.ToString("F4")
                + " animator=" + (anim != null) + " isHuman=" + (anim != null && anim.isHuman)
                + " humanScale=" + (anim != null ? anim.humanScale.ToString("F6") : "n/a"));

            // idle
            ClearKeys();
            yield return Frames(40);
            Log("  idle: speed=" + CtrlSpeed.ToString("F4")
                + " grounded=" + CtrlGrounded + " y=" + t.position.y.ToString("F4")
                + " animSpeedParam=" + (anim != null && anim.runtimeAnimatorController != null ? anim.GetFloat("Speed").ToString("F3") : "n/a"));

            // walk
            t.position = new Vector3(0f, t.position.y, 0f);
            yield return HoldKeys(60, Key.W);
            Log("  walk: speed=" + CtrlSpeed.ToString("F3")
                + " grounded=" + CtrlGrounded
                + " y=" + t.position.y.ToString("F4")
                + " animSpeedParam=" + (anim != null && anim.runtimeAnimatorController != null ? anim.GetFloat("Speed").ToString("F3") : "n/a")
                + " NaN=" + Bad(t.position));
            ClearKeys();
            yield return Frames(30);

            // gravity: lift the character and let it fall back
            t.position = new Vector3(0f, 1.5f, 0f);
            yield return Frames(120);
            Log("  gravity: dropped from y=1.5 -> y=" + t.position.y.ToString("F4")
                + " grounded=" + CtrlGrounded + " fellThrough=" + (t.position.y < -0.5f));
        }

        // ---------- STEP 8: shared ----------

        private IEnumerator SharedGate()
        {
            Log("=== GATE 8: SHARED SYSTEM ===");

            for (int pass = 0; pass < 2; pass++)
            {
                switcher.Activate(0);
                yield return Frames(15);
                Transform m = ActiveT;
                Animator ma = m.GetComponentInChildren<Animator>();
                float mScaleY = m.localScale.y;
                float mHuman = ma != null ? ma.humanScale : -1f;
                Vector3 mPos = m.position;

                switcher.Activate(1);
                yield return Frames(15);
                Transform f = ActiveT;
                Animator fa = f.GetComponentInChildren<Animator>();
                float fScaleY = f.localScale.y;
                float fHuman = fa != null ? fa.humanScale : -1f;

                Log("  pass " + pass + ": male humanScale=" + mHuman.ToString("F6") + " scaleY=" + mScaleY.ToString("F4")
                    + " | female humanScale=" + fHuman.ToString("F6") + " scaleY=" + fScaleY.ToString("F4")
                    + " | distinct=" + (Mathf.Abs(mHuman - fHuman) > 1e-5f)
                    + " | camTarget=" + (cameraRig.Target != null ? cameraRig.Target.name : "NULL")
                    + " | malePosPreserved=" + mPos.ToString("F3"));
            }

            // one input asset, one controller type, one camera
            ProtoPlayerInput[] inputs = Object.FindObjectsByType<ProtoPlayerInput>(FindObjectsSortMode.None);
            ProtoThirdPersonCamera[] cams = Object.FindObjectsByType<ProtoThirdPersonCamera>(FindObjectsSortMode.None);
            ProtoThirdPersonController[] ctrls = Object.FindObjectsByType<ProtoThirdPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Log("  instances: ProtoPlayerInput=" + inputs.Length + " ProtoThirdPersonCamera=" + cams.Length
                + " ProtoThirdPersonController=" + ctrls.Length + " (one controller component per character, single shared type)");

            // move the female, switch to male, confirm no leakage
            switcher.Activate(1);
            yield return Frames(10);
            Transform ft = ActiveT;
            yield return HoldKeys(40, Key.W);
            ClearKeys();
            float femaleSpeedAtSwitch = CtrlSpeed;
            switcher.Activate(0);
            yield return Frames(2);
            Log("  leakage: femaleSpeedAtSwitch=" + femaleSpeedAtSwitch.ToString("F3")
                + " maleSpeedAfterSwitch=" + CtrlSpeed.ToString("F4")
                + " camTarget=" + (cameraRig.Target != null ? cameraRig.Target.name : "NULL"));
            yield return Frames(10);
        }
    }
}

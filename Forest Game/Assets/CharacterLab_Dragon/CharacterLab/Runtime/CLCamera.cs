using UnityEngine;

namespace CharacterLab
{
    /// <summary>
    /// Moves the camera for the player: third person, over the shoulder, first
    /// person, top-down or side view, turned with the mouse, right mouse drag,
    /// arrow keys or automatically. Settings live in config.cam.
    /// </summary>
    public class CLCamera
    {
        readonly CharacterLabPlayer player;
        readonly UnityEngine.Camera cam;
        CameraSettings C;
        public float Yaw, Pitch;
        float distance;          // current zoom
        float shownDistance;     // after wall collision
        Vector3 pivot, pivotVel;
        float fovKick, shakeAmt, shakeT, tilt, bobT, bobAmt;
        float baseFov;
        bool cursorLocked;

        public Vector3 Forward => cam.transform.forward;
        public UnityEngine.Camera Camera => cam;

        public CLCamera(CharacterLabPlayer p, UnityEngine.Camera c)
        {
            player = p;
            cam = c;
            Yaw = p.transform.eulerAngles.y;
        }

        public void Apply(PlayerConfig cfg, bool first)
        {
            var old = C;
            C = cfg.cam;
            baseFov = C.fov;
            if (first || old == null || old.view != C.view)
            {
                Pitch = C.view == CameraView.TopDown ? Mathf.Clamp(C.startPitch, 25f, 89f) : C.view == CameraView.SideView ? Mathf.Clamp(C.startPitch, -10f, 40f) : (C.view == CameraView.FirstPerson ? 0f : C.startPitch);
                distance = C.distance;
                if (C.view == CameraView.TopDown || C.view == CameraView.SideView) Yaw = Mathf.Round(Yaw / 90f) * 90f;
                Snap();
            }
            if (old == null || !Mathf.Approximately(old.distance, C.distance)) distance = C.distance;
            cam.nearClipPlane = C.view == CameraView.FirstPerson ? 0.05f : 0.1f;
            if (C.lookControl != LookControl.MouseLocked) UnlockCursor();
            player.Model?.SetFirstPerson(C.view == CameraView.FirstPerson && !C.showBody);
        }

        public void Snap()
        {
            pivot = PivotTarget();
            pivotVel = Vector3.zero;
            shownDistance = distance;
            if (C != null) Tick(0f);
        }

        Vector3 PivotTarget()
        {
            float h = player.config.model.height;
            if (C != null && C.view == CameraView.FirstPerson)
            {
                var cc = player.GetComponent<CharacterController>();
                return player.transform.position + Vector3.up * (cc.height * 0.9f);
            }
            return player.transform.position + Vector3.up * h * (C != null ? C.pivotHeight : 0.85f);
        }

        public void Kick(float degrees) => fovKick = Mathf.Max(fovKick, degrees);
        public void Shake(float amount) { shakeAmt = Mathf.Max(shakeAmt, amount); shakeT = 0; }

        // ---- cursor lock (mouse look)
        public void HandleCursor()
        {
            var inp = player.Input;
            if (C.lookControl != LookControl.MouseLocked || !CharacterLabPlayer.AllowCursorLock)
            {
                if (cursorLocked) UnlockCursor();
                return;
            }
            if (inp.EscapeDown) UnlockCursor();
            else if (!cursorLocked && (inp.LeftMouseDown || Application.isEditor && Time.frameCount < 3)) LockCursor();
            if (cursorLocked && Cursor.lockState != CursorLockMode.Locked) cursorLocked = false;   // browser/editor released it
        }

        public void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cursorLocked = true;
        }

        public void UnlockCursor()
        {
            if (!cursorLocked) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorLocked = false;
        }

        public bool CursorLocked => cursorLocked;

        // ---- every frame (LateUpdate, after the player moved)
        public void Tick(float dt)
        {
            if (C == null) return;
            var inp = player.Input;
            bool fp = C.view == CameraView.FirstPerson;

            // ---- turning
            if (dt > 0 && inp != null)
            {
                Vector2 look = Vector2.zero;
                switch (C.lookControl)
                {
                    case LookControl.MouseLocked:
                        if (cursorLocked || !CharacterLabPlayer.AllowCursorLock) look = inp.LookDelta;
                        break;
                    case LookControl.RightMouseDrag:
                        if (inp.RightMouseHeld) look = inp.LookDelta;
                        break;
                }
                float yawIn = look.x * C.sensitivityX;
                float pitchIn = look.y * C.sensitivityY;
                // sticks / arrow keys
                yawIn += inp.LookStick.x * C.keyTurnSpeed * dt;
                pitchIn += inp.LookStick.y * C.keyTurnSpeed * 0.7f * dt;
                if (C.lookControl == LookControl.Fixed || C.view == CameraView.SideView) { yawIn = 0; pitchIn = 0; }
                if (C.view == CameraView.TopDown) pitchIn = 0;
                Yaw += yawIn;
                Pitch += C.invertY ? pitchIn : -pitchIn;
                Pitch = fp ? Mathf.Clamp(Pitch, -85f, 85f) : Mathf.Clamp(Pitch, C.pitchMin, C.pitchMax);

                bool moving = player.HorizontalSpeed > 0.5f;
                float recenter = C.lookControl == LookControl.AutoFollow ? Mathf.Max(C.autoRecenter, 2.5f) : C.autoRecenter;
                if (recenter > 0 && moving && !fp && C.view != CameraView.TopDown && C.view != CameraView.SideView && Mathf.Abs(yawIn) < 0.01f)
                {
                    float behind = player.transform.eulerAngles.y;
                    float diff = Mathf.DeltaAngle(Yaw, behind);
                    if (Mathf.Abs(diff) < 150f)   // don't whip round when running at the camera
                        Yaw += diff * (1f - Mathf.Exp(-recenter * 0.5f * dt));
                }

                if (C.scrollZoom && !fp && Mathf.Abs(inp.Zoom) > 0.001f)
                    distance = Mathf.Clamp(distance - inp.Zoom * Mathf.Max(0.5f, distance * 0.15f), Mathf.Min(C.zoomMin, C.zoomMax), Mathf.Max(C.zoomMin, C.zoomMax));
            }

            // ---- follow
            Vector3 target = PivotTarget();
            float smooth = fp ? 0f : C.followSmooth * 0.5f;
            if (dt <= 0 || smooth <= 0.001f) { pivot = target; pivotVel = Vector3.zero; }
            else pivot = Vector3.SmoothDamp(pivot, target, ref pivotVel, smooth, Mathf.Infinity, dt);
            if ((pivot - target).sqrMagnitude > 400f) pivot = target;   // teleported

            Quaternion rot;
            Vector3 pos;
            switch (C.view)
            {
                case CameraView.FirstPerson:
                {
                    rot = Quaternion.Euler(Pitch, Yaw, 0);
                    float speed = player.IsGrounded ? player.HorizontalSpeed : 0f;
                    bobAmt = Mathf.MoveTowards(bobAmt, Mathf.Clamp01(speed / 8f), dt * 4f);
                    bobT += dt * (6f + speed * 0.8f);
                    Vector3 bob = new Vector3(Mathf.Cos(bobT * 0.5f) * 0.04f, Mathf.Abs(Mathf.Sin(bobT * 0.5f)) * 0.07f, 0) * bobAmt * C.headBob * 1.6f;
                    pos = pivot + rot * bob;
                    break;
                }
                case CameraView.TopDown:
                {
                    rot = Quaternion.Euler(Pitch, Yaw, 0);
                    pos = pivot - rot * Vector3.forward * distance * 1.6f;
                    break;
                }
                case CameraView.SideView:
                {
                    rot = Quaternion.Euler(Pitch, Yaw, 0);
                    pos = pivot - rot * Vector3.forward * distance * 1.5f;
                    break;
                }
                default:
                {
                    rot = Quaternion.Euler(Pitch, Yaw, 0);
                    Vector3 side = C.view == CameraView.OverShoulder ? rot * Vector3.right * C.shoulder : Vector3.zero;
                    float dist = C.view == CameraView.OverShoulder ? distance * 0.55f : distance;
                    Vector3 from = pivot + side;
                    float want = dist;
                    if (C.collision && Physics.SphereCast(pivot, 0.25f, (from - rot * Vector3.forward * dist - pivot).normalized, out var hit,
                            (from - rot * Vector3.forward * dist - pivot).magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && hit.collider.transform.root != player.transform.root)
                    {
                        want = Mathf.Max(0.3f, hit.distance * dist / Mathf.Max(0.01f, (from - rot * Vector3.forward * dist - pivot).magnitude) - 0.1f);
                    }
                    // Pull in fast, ease back out.
                    shownDistance = want < shownDistance || dt <= 0 ? want : Mathf.MoveTowards(shownDistance, want, dt * 6f);
                    pos = from - rot * Vector3.forward * shownDistance;
                    break;
                }
            }

            // ---- tilt (wall run lean, turning)
            float wantTilt = 0f;
            if (player.State == CharacterLabPlayer.MoveState.WallRun) wantTilt = C.tilt * Mathf.Sign(Vector3.Dot(cam.transform.right, player.WallNormal));
            tilt = Mathf.MoveTowards(tilt, wantTilt, dt * 60f);
            rot *= Quaternion.Euler(0, 0, tilt);

            // ---- shake
            if (shakeAmt > 0.001f && dt > 0)
            {
                shakeT += dt;
                float a = shakeAmt * 0.35f;
                pos += rot * new Vector3((Mathf.PerlinNoise(shakeT * 25f, 0) - 0.5f) * a, (Mathf.PerlinNoise(0, shakeT * 25f) - 0.5f) * a, 0);
                shakeAmt = Mathf.MoveTowards(shakeAmt, 0, dt * 2.5f);
            }

            // ---- field of view
            float speedKick = (player.IsSprinting && player.HorizontalSpeed > player.config.move.walkSpeed + 0.5f) || player.State == CharacterLabPlayer.MoveState.Dash ? C.sprintFov : 0f;
            fovKick = Mathf.MoveTowards(fovKick, 0, dt * 40f);
            float fov = baseFov + Mathf.Max(speedKick, fovKick);
            cam.fieldOfView = dt <= 0 ? fov : Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-8f * dt));

            cam.transform.SetPositionAndRotation(pos, rot);
        }
    }
}

using UnityEngine;

namespace CharacterLab
{
    // The movement "motor": every frame it reads the input, decides what the
    // player is doing (walking, dashing, sliding, wall running...) and moves the
    // CharacterController. All speeds are in metres per second.
    public partial class CharacterLabPlayer
    {
        Vector3 hv;                 // horizontal velocity (y always 0)
        float vy;                   // vertical velocity
        float facingYaw;            // which way the body faces (degrees)
        float coyoteT, jumpBufferT, dashCooldownT, stateT, launchedT, wallJumpLockT, wallRunCooldownT, actionCooldownT;
        int airJumpsLeft, airDashesLeft;
        bool jumpCutAllowed, sprintToggled, wasGrounded, crouchWanted;
        float fuel, cfgFuel;
        Vector3 dashVel, slideDir, wallRunDir;
        Vector3 groundNormal = Vector3.up;
        float groundAngle;
        Vector3 wallNormal, wallPoint;
        float wallT = 99f;          // time since we last touched a wall
        bool wallTouchedThisAir;
        Vector3 grappleAnchor;
        float ropeLength;
        Transform groundTransform, platform;
        Vector3 platformLocal;
        Quaternion platformRot;
        float poundWindup;
        Vector3 sideAxis = Vector3.right;
        float sideZ;
        bool sideInit;

        float Gravity => config.jump.gravity;
        /// <summary>The direction pointing out of the last wall you touched.</summary>
        public Vector3 WallNormal => wallNormal;
        /// <summary>Where the grappling hook is attached (while grappling).</summary>
        public Vector3 GrappleAnchor => grappleAnchor;
        static float JumpSpeed(float g, float h) => Mathf.Sqrt(2f * Mathf.Max(0.01f, g) * Mathf.Max(0.01f, h));
        Vector3 Facing => Quaternion.Euler(0, facingYaw, 0) * Vector3.forward;
        bool IsSideView => config.cam.view == CameraView.SideView || config.move.controlStyle == ControlStyle.SideScroller;

        void Motor(float dt)
        {
            var J = config.jump;
            var M = config.move;
            CarryWithPlatform();

            // ---- timers
            coyoteT -= dt; jumpBufferT -= dt; dashCooldownT -= dt; stateT += dt; launchedT -= dt;
            wallJumpLockT -= dt; wallRunCooldownT -= dt; wallT += dt; actionCooldownT -= dt;
            if (input.JumpDown) jumpBufferT = Mathf.Max(J.jumpBuffer, 0.0001f);
            if (IsGrounded) coyoteT = J.coyoteTime;

            // ---- which way does the stick point, in the world?
            Vector3 dir = MoveDirection(dt);
            float inputMag = Mathf.Clamp01(dir.magnitude);

            // ---- sprint
            switch (M.sprintMode)
            {
                case SprintMode.Hold: IsSprinting = input.SprintHeld && inputMag > 0.1f; break;
                case SprintMode.Toggle:
                    if (input.SprintDown) sprintToggled = !sprintToggled;
                    if (inputMag < 0.1f && IsGrounded) sprintToggled = false;
                    IsSprinting = sprintToggled;
                    break;
                case SprintMode.Always: IsSprinting = true; break;
                default: IsSprinting = false; break;
            }

            // ---- ability triggers
            if (input.DashDown) TryDash(dir);
            if (input.CrouchDown && !IsGrounded && config.extra.groundPound && (State == MoveState.Normal || State == MoveState.Dash)) StartPound();
            if (input.CrouchDown && IsGrounded && State == MoveState.Normal) TrySlide();
            if (input.GrappleDown && config.extra.grapple != GrappleMode.Off) TryGrapple();
            if (input.ActionDown && config.extra.action != ActionAnimation.Off && actionCooldownT <= 0)
            {
                actionCooldownT = 0.4f;
                model.PlayAction();
                sfx.Play("action");
            }

            // ---- crouch (only standing still or walking on the ground)
            crouchWanted = M.enableCrouch && input.CrouchHeld && IsGrounded && State == MoveState.Normal;
            bool wantLow = crouchWanted || State == MoveState.Slide;
            if (wantLow != IsCrouching)
            {
                if (wantLow || HasHeadroom())
                {
                    IsCrouching = wantLow;
                    SetCapsuleHeight(IsCrouching ? config.model.height * M.crouchHeight : config.model.height);
                }
            }

            IsWallSliding = false;
            IsGliding = false;
            IsJetpacking = false;

            switch (State)
            {
                case MoveState.Dash: DashUpdate(dt); break;
                case MoveState.Slide: SlideUpdate(dt, dir); break;
                case MoveState.WallRun: WallRunUpdate(dt, dir); break;
                case MoveState.GroundPound: PoundUpdate(dt); break;
                case MoveState.Grapple: GrappleUpdate(dt, dir); break;
                default: NormalUpdate(dt, dir, inputMag); break;
            }

            // ---- jumping (all states)
            HandleJump(dir);

            // ---- respawn if we fell out of the world
            if (transform.position.y < config.extra.respawnHeight)
            {
                Respawn();
                return;
            }

            MoveController(dt);
            UpdateFacing(dt, dir);
        }

        // ================================================================== direction
        Vector3 MoveDirection(float dt)
        {
            Vector2 m = input.Move;
            Vector3 dir;
            float camYaw = camRig != null ? camRig.Yaw : facingYaw;
            if (IsSideView)
            {
                if (!sideInit)
                {
                    sideInit = true;
                    sideAxis = Quaternion.Euler(0, camYaw, 0) * Vector3.right;
                    sideZ = Vector3.Dot(transform.position, Vector3.Cross(sideAxis, Vector3.up));
                }
                dir = sideAxis * m.x;
            }
            else
            {
                sideInit = false;
                if (config.move.controlStyle == ControlStyle.Tank && config.cam.view != CameraView.FirstPerson)
                {
                    facingYaw += m.x * config.move.turnSpeed * 0.25f * dt;
                    dir = Facing * m.y;
                }
                else
                {
                    dir = Quaternion.Euler(0, camYaw, 0) * new Vector3(m.x, 0, m.y);
                }
            }
            return Vector3.ClampMagnitude(dir, 1f);
        }

        float TargetSpeed()
        {
            var M = config.move;
            if (IsCrouching) return M.crouchSpeed;
            return IsSprinting ? M.sprintSpeed : M.walkSpeed;
        }

        // ================================================================== normal movement
        void NormalUpdate(float dt, Vector3 dir, float inputMag)
        {
            var M = config.move;
            var J = config.jump;
            float target = TargetSpeed();
            Vector3 desired = dir * target;
            float speed = hv.magnitude;

            if (IsGrounded)
            {
                if (groundAngle > M.slopeLimit + 1f)
                {
                    // Too steep: slide down it.
                    Vector3 down = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
                    down.y = 0;
                    hv += down.normalized * Gravity * 0.6f * dt;
                }
                else if (speed > target + 0.2f && Vector3.Dot(desired, hv) > 0)
                {
                    // Carrying extra speed (dash, slide, long jump): bleed it off gently while steering.
                    float newSpeed = Mathf.MoveTowards(speed, target, M.deceleration * 0.35f * dt);
                    Vector3 d = Vector3.RotateTowards(hv.normalized, dir.normalized, Mathf.Deg2Rad * 360f * dt, 0f);
                    hv = d * newSpeed;
                }
                else
                {
                    float accel = inputMag > 0.05f ? M.acceleration : M.deceleration;
                    hv = Vector3.MoveTowards(hv, desired, accel * dt);
                }
            }
            else
            {
                float control = wallJumpLockT > 0 ? 0.05f : M.airControl;
                bool gliding = J.airHold == AirHoldMode.Glide && input.JumpHeld && vy < 0 && launchedT <= 0;
                if (gliding)
                {
                    IsGliding = true;
                    Vector3 g = (inputMag > 0.1f ? dir.normalized : Facing) * J.glideSpeed;
                    hv = Vector3.MoveTowards(hv, g, M.acceleration * 0.4f * dt);
                }
                else if (speed > target + 0.2f)
                {
                    // Keep momentum in the air, but let the player steer it.
                    float newSpeed = Mathf.MoveTowards(speed, inputMag > 0.05f ? Mathf.Max(target, speed * 0.98f) : target, (inputMag > 0.05f ? 2f : M.deceleration * 0.15f * control) * dt);
                    Vector3 d = inputMag > 0.05f ? Vector3.RotateTowards(hv.normalized, dir.normalized, Mathf.Deg2Rad * 220f * control * dt, 0f) : hv.normalized;
                    hv = d * newSpeed;
                }
                else
                {
                    float accel = (inputMag > 0.05f ? M.acceleration : M.deceleration * 0.3f) * control;
                    hv = Vector3.MoveTowards(hv, desired, accel * dt);
                }

                // ---- wall slide / wall run
                if (wallT < 0.08f && vy < 0 && config.wall.wallJump)
                {
                    bool pushing = Vector3.Dot(dir, -wallNormal) > 0.25f;
                    if (pushing)
                    {
                        IsWallSliding = true;
                        vy = Mathf.Max(vy, -config.wall.slideSpeed);
                    }
                }
                if (config.wall.wallRun && wallT < 0.08f && wallRunCooldownT <= 0 && inputMag > 0.5f)
                {
                    Vector3 along = Vector3.ProjectOnPlane(hv.sqrMagnitude > 1f ? hv : dir, wallNormal);
                    along.y = 0;
                    if (along.magnitude > 0.5f && Vector3.Dot(dir.normalized, -wallNormal) < 0.8f && vy > -8f)
                        StartWallRun(along.normalized);
                }
            }

            // ---- vertical
            float grav = Gravity;
            if (vy < 0) grav *= J.fallMultiplier;
            vy -= grav * dt;

            // Hold-in-air abilities
            if (!IsGrounded && input.JumpHeld && launchedT <= 0)
            {
                if (J.airHold == AirHoldMode.Glide && vy < 0)
                {
                    IsGliding = true;
                    vy = Mathf.Max(vy, -J.glideFallSpeed);
                }
                else if (J.airHold == AirHoldMode.Jetpack && fuel > 0 && vy < 3f)
                {
                    IsJetpacking = true;
                    vy = Mathf.Min(vy + (J.jetpackForce + grav) * dt, 12f);
                    fuel -= dt;
                }
                else if (J.airHold == AirHoldMode.Hover && fuel > 0 && vy < 0.5f)
                {
                    IsJetpacking = true;
                    vy = Mathf.MoveTowards(vy, 0f, (grav + 40f) * dt);
                    fuel -= dt;
                }
            }
            if (IsGrounded) fuel = Mathf.Min(cfgFuel, fuel + cfgFuel / Mathf.Max(0.05f, J.fuelRecharge) * dt);
            vy = Mathf.Max(vy, -J.maxFallSpeed);
        }

        // ================================================================== jumping
        void HandleJump(Vector3 dir)
        {
            var J = config.jump;
            if (J.variableJump && input.JumpUp && vy > 0 && jumpCutAllowed && State == MoveState.Normal)
            {
                vy *= 0.45f;
                jumpCutAllowed = false;
            }
            if (jumpBufferT <= 0) return;

            if (State == MoveState.Grapple)
            {
                EndGrapple();
                vy = Mathf.Max(vy, 0) + JumpSpeed(Gravity, 1.2f);
                jumpBufferT = 0;
                return;
            }
            if (State == MoveState.GroundPound) return;

            bool canGround = (IsGrounded || coyoteT > 0) && (State != MoveState.WallRun);
            if (canGround && groundAngle <= config.move.slopeLimit + 1f)
            {
                bool longJump = State == MoveState.Slide;
                if (State == MoveState.Dash || State == MoveState.Slide) State = MoveState.Normal;
                vy = JumpSpeed(Gravity, J.jumpHeight);
                if (longJump)
                {
                    Vector3 f = hv.sqrMagnitude > 0.01f ? hv.normalized : Facing;
                    hv += f * config.slide.longJump;
                    vy *= 0.85f;
                }
                Jumped_(longJump ? "longjump" : "jump");
                return;
            }

            bool wallOk = config.wall.wallJump && !IsGrounded && wallT < 0.15f;
            if (State == MoveState.WallRun || wallOk)
            {
                Vector3 along = State == MoveState.WallRun ? wallRunDir * config.wall.runSpeed * 0.6f : Vector3.zero;
                State = MoveState.Normal;
                vy = JumpSpeed(Gravity, config.wall.jumpHeight);
                hv = wallNormal * config.wall.jumpPush + along;
                facingYaw = Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg;
                wallJumpLockT = 0.16f;
                wallRunCooldownT = 0.25f;
                wallT = 99f;
                jumpBufferT = 0;
                jumpCutAllowed = true;
                if (config.wall.resetAirJumps) ResetAirMoves();
                fx.Puff(wallPoint, CLEffects.PuffKind.Wall);
                sfx.Play("walljump");
                model.Trick(FlipOnAirJump.None);
                WallJumped?.Invoke();
                return;
            }

            // About to hit a wall? Keep the press buffered for a wall jump instead of spending an air jump.
            if (config.wall.wallJump && !IsGrounded && WallAhead()) return;

            if (input.JumpDown && airJumpsLeft > 0 && !IsGrounded && State != MoveState.Slide)
            {
                if (State == MoveState.Dash) State = MoveState.Normal;
                airJumpsLeft--;
                vy = JumpSpeed(Gravity, J.airJumpHeight);
                if (dir.sqrMagnitude > 0.01f)
                {
                    float keep = Mathf.Max(hv.magnitude, TargetSpeed());
                    hv = Vector3.Lerp(hv, dir.normalized * keep, 0.65f);
                }
                jumpBufferT = 0;
                jumpCutAllowed = true;
                fx.AirJump(transform.position);
                sfx.Play("airjump");
                model.Trick(config.anim.flipOnAirJump);
                AirJumped?.Invoke();
            }
        }

        void Jumped_(string sound)
        {
            IsGrounded = false;
            coyoteT = 0;
            jumpBufferT = 0;
            jumpCutAllowed = true;
            groundTransform = null;
            fx.Puff(transform.position, CLEffects.PuffKind.Jump);
            sfx.Play(sound == "longjump" ? "dash" : "jump");
            model.OnJump();
            Jumped?.Invoke();
        }

        // ================================================================== dash
        void TryDash(Vector3 dir)
        {
            var D = config.dash;
            if (!D.enabled || dashCooldownT > 0) return;
            if (State == MoveState.GroundPound || State == MoveState.Grapple || State == MoveState.Dash) return;
            if (!IsGrounded && airDashesLeft <= 0) return;
            Vector3 d;
            switch (D.direction)
            {
                case DashDirection.CameraForward:
                    d = camRig != null ? camRig.Forward : Facing;
                    if (IsGrounded && d.y < 0) d.y = 0;
                    break;
                case DashDirection.Facing: d = Facing; break;
                default: d = dir.sqrMagnitude > 0.01f ? dir : Facing; break;
            }
            if (IsSideView) d = Vector3.Project(d, sideAxis) + Vector3.up * Mathf.Max(0, d.y);
            if (d.sqrMagnitude < 0.0001f) d = Facing;
            d.Normalize();
            if (!IsGrounded) airDashesLeft--;
            if (State == MoveState.Slide) State = MoveState.Normal;
            State = MoveState.Dash;
            stateT = 0;
            dashVel = d * D.speed;
            if (Mathf.Abs(d.y) < 0.01f || IsGrounded) facingYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            fx.Dash(true);
            sfx.Play("dash");
            camRig?.Kick(config.cam.sprintFov * 1.4f);
            Dashed?.Invoke();
        }

        void DashUpdate(float dt)
        {
            var D = config.dash;
            hv = new Vector3(dashVel.x, 0, dashVel.z);
            if (D.noGravity) vy = dashVel.y;
            else vy = Mathf.Max(vy + dashVel.y * dt * 4f - Gravity * dt, -config.jump.maxFallSpeed);
            if (stateT >= D.duration) EndDash();
        }

        void EndDash()
        {
            var D = config.dash;
            State = MoveState.Normal;
            dashCooldownT = D.cooldown;
            float keep = Mathf.Max(D.speed * D.keepSpeed, Mathf.Min(TargetSpeed(), D.speed));
            Vector3 flat = new Vector3(dashVel.x, 0, dashVel.z);
            if (flat.sqrMagnitude > 0.01f) hv = flat.normalized * keep;
            if (D.noGravity) vy = dashVel.y * D.keepSpeed;
            fx.Dash(false);
        }

        // ================================================================== slide
        void TrySlide()
        {
            var S = config.slide;
            if (!S.enabled) return;
            float speed = hv.magnitude;
            if (speed < S.minSpeed) return;
            State = MoveState.Slide;
            stateT = 0;
            slideDir = hv.normalized;
            hv = slideDir * (speed + S.boost);
            IsCrouching = true;
            SetCapsuleHeight(config.model.height * config.move.crouchHeight);
            fx.Puff(transform.position, CLEffects.PuffKind.Land);
            sfx.Play("slide");
            Slid?.Invoke();
        }

        void SlideUpdate(float dt, Vector3 dir)
        {
            var S = config.slide;
            float speed = hv.magnitude;
            // Downhill speeds you up, uphill slows you down.
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
            float steep = Mathf.Sin(groundAngle * Mathf.Deg2Rad);
            downhill.y = 0;
            bool goingDown = downhill.sqrMagnitude > 0.0001f && Vector3.Dot(downhill.normalized, slideDir) > 0.2f && steep > 0.08f;
            Vector3 v = slideDir * speed + downhill.normalized * S.slopeBoost * steep * dt;
            speed = v.magnitude;
            slideDir = v.normalized;
            if (!goingDown) speed = Mathf.Max(0, speed - S.friction * dt);
            // a little steering
            if (dir.sqrMagnitude > 0.01f) slideDir = Vector3.RotateTowards(slideDir, dir.normalized, Mathf.Deg2Rad * 70f * dt, 0f);
            hv = slideDir * speed;
            vy -= Gravity * dt;
            if (IsGrounded && vy < -2f) vy = -2f;
            fx.SlideDust(dt);
            bool tooLong = stateT > S.maxTime && !goingDown;
            if (!input.CrouchHeld || speed < config.move.crouchSpeed || tooLong || (!IsGrounded && coyoteT <= 0))
                State = MoveState.Normal;
        }

        // ================================================================== wall run
        void StartWallRun(Vector3 along)
        {
            State = MoveState.WallRun;
            stateT = 0;
            wallRunDir = along;
            vy = Mathf.Max(vy, 2f);
            if (config.wall.resetAirJumps) ResetAirMoves();
            sfx.Play("slide");
        }

        void WallRunUpdate(float dt, Vector3 dir)
        {
            var W = config.wall;
            hv = wallRunDir * W.runSpeed;
            vy -= W.runGravity * dt;
            vy = Mathf.Max(vy, -W.slideSpeed - 2f);
            // stay stuck to the wall
            Vector3 c = transform.position + cc.center;
            if (Physics.SphereCast(c, cc.radius * 0.8f, -wallNormal, out var hit, cc.radius + 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(hit.normal.y) < 0.35f)
            {
                wallNormal = hit.normal;
                wallPoint = hit.point;
                wallT = 0;
                Vector3 along = Vector3.ProjectOnPlane(wallRunDir, wallNormal);
                along.y = 0;
                if (along.sqrMagnitude > 0.01f) wallRunDir = along.normalized;
                hv += -wallNormal * 2f;   // press into the wall
            }
            else
            {
                EndWallRun();
                return;
            }
            fx.WallSparks(wallPoint, dt);
            if (stateT > W.runTime || IsGrounded || dir.sqrMagnitude < 0.1f) EndWallRun();
        }

        void EndWallRun()
        {
            State = MoveState.Normal;
            wallRunCooldownT = 0.35f;
            hv = wallRunDir * Mathf.Min(hv.magnitude, config.wall.runSpeed) + wallNormal * 1.5f;
        }

        // ================================================================== ground pound
        void StartPound()
        {
            if (State == MoveState.Dash) EndDash();
            State = MoveState.GroundPound;
            stateT = 0;
            poundWindup = 0.16f;
            hv = Vector3.zero;
            vy = 0;
            model.Trick(FlipOnAirJump.FrontFlip);
            sfx.Play("dash");
        }

        void PoundUpdate(float dt)
        {
            hv = Vector3.zero;
            if (stateT < poundWindup) vy = 0;
            else vy = -config.extra.poundSpeed;
        }

        void PoundLanded()
        {
            State = MoveState.Normal;
            fx.Shockwave(transform.position);
            sfx.Play("pound");
            camRig?.Shake(0.6f * config.cam.shake + 0.2f);
            GroundPounded?.Invoke();
            if (config.extra.poundBounce > 0.01f)
            {
                vy = JumpSpeed(Gravity, config.extra.poundBounce);
                IsGrounded = false;
                launchedT = 0.15f;
            }
        }

        // ================================================================== grapple
        void TryGrapple()
        {
            if (State == MoveState.Grapple) { EndGrapple(); return; }
            if (State == MoveState.GroundPound) return;
            var E = config.extra;
            Vector3 origin, aim;
            bool fromCamera = cam != null && (config.cam.view == CameraView.ThirdPerson || config.cam.view == CameraView.OverShoulder || config.cam.view == CameraView.FirstPerson);
            if (fromCamera)
            {
                origin = cam.transform.position;
                aim = cam.transform.forward;
            }
            else
            {
                origin = transform.position + Vector3.up * config.model.height * 0.7f;
                aim = (Facing + Vector3.up * 0.8f).normalized;
            }
            float extra = fromCamera ? Vector3.Distance(origin, transform.position) : 0f;
            bool found = false;
            Vector3 point = Vector3.zero;
            // 1) A grapple point near where we're aiming wins (aim assist).
            float best = 0.9f;
            foreach (var gp in CharacterLabGrapplePoint.All)
            {
                Vector3 p = gp.transform.position;
                float d = Vector3.Distance(p, transform.position);
                if (d > E.grappleRange || d < 1.5f) continue;
                float dot = Vector3.Dot((p - origin).normalized, aim);
                if (dot <= best) continue;
                Vector3 from = transform.position + Vector3.up * config.model.height * 0.7f;
                if (Physics.Linecast(from, p, out var block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && block.collider.GetComponentInParent<CharacterLabGrapplePoint>() == null && block.collider.transform.root != transform.root) continue;
                best = dot;
                point = p;
                found = true;
            }
            // 2) Otherwise whatever surface the aim ray hits.
            if (!found && Physics.Raycast(origin, aim, out var hit, E.grappleRange + extra, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != transform.root)
            {
                var gp = hit.collider.GetComponentInParent<CharacterLabGrapplePoint>();
                if (E.grappleTargets == GrappleTargets.AnySurface || gp != null)
                {
                    point = gp != null ? gp.transform.position : hit.point;
                    found = Vector3.Distance(point, transform.position) <= E.grappleRange + 1f;
                }
            }
            if (!found)
            {
                fx.GrappleMiss(origin + aim * Mathf.Min(E.grappleRange, 12f));
                sfx.Play("grapple", 0.7f);
                return;
            }
            if (State == MoveState.Dash) EndDash();
            if (State == MoveState.Slide || State == MoveState.WallRun) State = MoveState.Normal;
            State = MoveState.Grapple;
            stateT = 0;
            grappleAnchor = point;
            float attach = Vector3.Distance(transform.position + Vector3.up * config.model.height * 0.6f, point);
            ropeLength = Mathf.Max(2f, Mathf.Min(attach, Mathf.Max(5f, attach * 0.7f)));   // reel in a bit so swings don't drag along the ground
            fx.Rope(true, point);
            sfx.Play("grapple");
            ResetAirMoves();
            Grappled?.Invoke();
        }

        void GrappleUpdate(float dt, Vector3 dir)
        {
            var E = config.extra;
            Vector3 hand = transform.position + Vector3.up * config.model.height * 0.6f;
            Vector3 toAnchor = grappleAnchor - hand;
            float dist = toAnchor.magnitude;
            fx.Rope(true, grappleAnchor);
            if (input.GrappleUp && E.grapple == GrappleMode.Swing) { EndGrapple(); return; }
            if (E.grapple == GrappleMode.Pull)
            {
                Vector3 v = toAnchor.normalized * E.grappleSpeed;
                hv = new Vector3(v.x, 0, v.z);
                vy = v.y;
                if (dist < 1.4f || stateT > 4f || input.GrappleUp)
                {
                    EndGrapple();
                    vy = Mathf.Max(vy * 0.4f, JumpSpeed(Gravity, 0.8f));
                }
            }
            else
            {
                // Swing: gravity + a little push from the stick, the rope stops us going further than its length.
                Vector3 v = new Vector3(hv.x, vy, hv.z);
                v += Vector3.down * Gravity * dt;
                v += dir * config.move.acceleration * 0.25f * dt;
                if (input.JumpHeld == false && dist > ropeLength * 0.6f && input.CrouchHeld) ropeLength = Mathf.Max(2f, ropeLength - 6f * dt);   // reel in
                Vector3 next = hand + v * dt;
                Vector3 radial = next - grappleAnchor;
                if (radial.magnitude > ropeLength)
                {
                    Vector3 rn = radial.normalized;
                    float outward = Vector3.Dot(v, rn);
                    if (outward > 0) v -= rn * outward;
                    // pull back onto the rope (spring)
                    v -= rn * (radial.magnitude - ropeLength) * 8f * dt;
                }
                v *= 1f - 0.05f * dt;
                hv = new Vector3(v.x, 0, v.z);
                vy = v.y;
                if (IsGrounded && dist < ropeLength * 0.5f) EndGrapple();
            }
        }

        void EndGrapple()
        {
            State = MoveState.Normal;
            fx.Rope(false, Vector3.zero);
            launchedT = 0.1f;
        }

        // ================================================================== moving the controller
        void MoveController(float dt)
        {
            Vector3 vel = hv + Vector3.up * vy;
            bool groundedBefore = IsGrounded;
            float fallSpeed = -vy;

            // Stick to the ground going down slopes/steps.
            if (IsGrounded && vy <= 0 && State != MoveState.GroundPound) vel.y = Mathf.Min(vel.y, -(2f + hv.magnitude * 0.6f));

            if (IsSideView)
            {
                // Lock the depth axis.
                Vector3 n = Vector3.Cross(sideAxis, Vector3.up);
                float z = Vector3.Dot(transform.position, n);
                vel -= n * Vector3.Dot(vel, n);
                vel += n * (sideZ - z) / Mathf.Max(dt, 0.001f) * 0.5f;
            }

            touchedGround = false;
            touchedNormal = Vector3.up;
            var flags = cc.Move(vel * dt);

            IsGrounded = (flags & CollisionFlags.Below) != 0;
            if (IsGrounded)
            {
                groundNormal = touchedGround ? touchedNormal : Vector3.up;
                groundAngle = Vector3.Angle(groundNormal, Vector3.up);
            }
            else
            {
                groundNormal = Vector3.up;
                groundAngle = 0;
                groundTransform = null;
                // Snap down small steps instead of flying off them.
                if (groundedBefore && vy <= 0 && launchedT <= 0 && State == MoveState.Normal &&
                    Physics.SphereCast(transform.position + cc.center, cc.radius * 0.95f, Vector3.down, out var hit,
                        cc.height * 0.5f - cc.radius * 0.95f + cc.stepOffset + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && Vector3.Angle(hit.normal, Vector3.up) <= config.move.slopeLimit)
                {
                    float drop = hit.distance - (cc.height * 0.5f - cc.radius * 0.95f) + cc.skinWidth * 0.5f;
                    if (drop > 0.001f) cc.Move(Vector3.down * drop);
                    IsGrounded = true;
                    groundNormal = hit.normal;
                    groundAngle = Vector3.Angle(groundNormal, Vector3.up);
                }
            }
            if ((flags & CollisionFlags.Above) != 0 && vy > 0) vy = 0;

            if (!IsGrounded && groundedBefore && vy <= 0) { /* walked off a ledge: coyote time already running */ }

            if (IsGrounded && !groundedBefore) Landed_(fallSpeed);
            if (IsGrounded)
            {
                if (vy < 0) vy = 0;
                wallTouchedThisAir = false;
                if (State == MoveState.Normal || State == MoveState.Slide) { airJumpsLeft = config.jump.airJumps; airDashesLeft = config.dash.airDashes; }
            }
            else
            {
                ProbeWalls();
            }
            RememberPlatform();
        }

        void Landed_(float fallSpeed)
        {
            if (State == MoveState.GroundPound)
            {
                PoundLanded();
                return;
            }
            if (State == MoveState.WallRun) State = MoveState.Normal;
            if (State == MoveState.Grapple && config.extra.grapple == GrappleMode.Pull) { }
            var J = config.jump;
            if (J.landingBounce > 0.01f && fallSpeed > 4f && !input.CrouchHeld && State == MoveState.Normal)
            {
                vy = fallSpeed * J.landingBounce;
                IsGrounded = false;
                launchedT = 0.1f;
                jumpCutAllowed = false;
                model.OnLand(fallSpeed);
                sfx.Play("bounce", Mathf.Clamp01(fallSpeed / 15f));
                fx.Puff(transform.position, CLEffects.PuffKind.Land);
                return;
            }
            model.OnLand(fallSpeed);
            if (fallSpeed > 3f)
            {
                fx.Puff(transform.position, CLEffects.PuffKind.Land);
                sfx.Play("land", Mathf.Clamp01(fallSpeed / 20f) * 0.8f + 0.2f);
            }
            if (fallSpeed > 18f) camRig?.Shake(Mathf.Clamp01((fallSpeed - 18f) / 25f) * config.cam.shake);
            Landed?.Invoke(fallSpeed);
        }

        void ProbeWalls()
        {
            if (wallT < 0.01f) return;   // OnControllerColliderHit already found one this frame
            Vector3 c = transform.position + cc.center;
            Vector3[] dirs = { hv.sqrMagnitude > 0.1f ? hv.normalized : Facing, wallT < 0.5f ? -wallNormal : Facing, Facing };
            foreach (var d in dirs)
            {
                if (d.sqrMagnitude < 0.01f) continue;
                if (Physics.SphereCast(c, cc.radius * 0.8f, d, out var hit, 0.35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && Mathf.Abs(hit.normal.y) < 0.35f && hit.collider.attachedRigidbody == null)
                {
                    SetWall(hit.normal, hit.point);
                    return;
                }
            }
        }

        bool WallAhead()
        {
            Vector3 d = new Vector3(hv.x, 0, hv.z);
            if (d.sqrMagnitude < 4f) return false;
            float reach = Mathf.Min(0.9f, d.magnitude * Mathf.Max(0.05f, config.jump.jumpBuffer));
            return Physics.SphereCast(transform.position + cc.center, cc.radius * 0.8f, d.normalized, out var hit, reach + 0.1f,
                       Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                   && Mathf.Abs(hit.normal.y) < 0.35f && hit.collider.attachedRigidbody == null;
        }

        void SetWall(Vector3 normal, Vector3 point)
        {
            normal.y = 0;
            if (normal.sqrMagnitude < 0.01f) return;
            wallNormal = normal.normalized;
            wallPoint = point;
            if (wallT > 0.3f && !wallTouchedThisAir && config.wall.resetAirJumps && config.wall.wallJump && !IsGrounded)
            {
                wallTouchedThisAir = true;
                ResetAirMoves();
            }
            wallT = 0;
        }

        bool touchedGround;
        Vector3 touchedNormal;
        float bounceCooldownT;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!Application.isPlaying || !started) return;
            Vector3 n = hit.normal;
            if (n.y > 0.5f)
            {
                touchedGround = true;
                touchedNormal = n;
                groundTransform = hit.collider.transform;
            }
            else if (Mathf.Abs(n.y) < 0.35f && hit.collider.attachedRigidbody == null && !IsGrounded)
            {
                SetWall(n, hit.point);
            }

            var pad = hit.collider.GetComponentInParent<CharacterLabBouncePad>();
            if (pad != null && Time.time > bounceCooldownT)
            {
                bounceCooldownT = Time.time + 0.2f;
                Vector3 up = pad.transform.up;
                if (pad.launchHeight > 0f && up.y > 0.05f)
                    Launch(up / up.y * JumpSpeed(Gravity, pad.launchHeight));
                else Launch(up * pad.launchSpeed);
                pad.Bounced();
                return;
            }

            // Shove physics objects.
            var rb = hit.collider.attachedRigidbody;
            if (rb != null && !rb.isKinematic && config.move.pushPower > 0 && hit.moveDirection.y > -0.3f)
            {
                Vector3 push = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
                rb.AddForceAtPosition(push * config.move.pushPower * Mathf.Max(1f, hv.magnitude * 0.5f), hit.point, ForceMode.Impulse);
            }
        }

        // ---- moving platforms: ride along with whatever we're standing on
        void RememberPlatform()
        {
            platform = IsGrounded ? groundTransform : null;
            if (platform != null)
            {
                platformLocal = platform.InverseTransformPoint(transform.position);
                platformRot = platform.rotation;
            }
        }

        void CarryWithPlatform()
        {
            if (platform == null) return;
            Vector3 now = platform.TransformPoint(platformLocal);
            Vector3 delta = now - transform.position;
            if (delta.sqrMagnitude > 0.000001f && delta.sqrMagnitude < 4f) cc.Move(delta);
            float yaw = (platform.rotation * Quaternion.Inverse(platformRot)).eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(0, yaw)) > 0.001f)
            {
                facingYaw += yaw;
                if (camRig != null) camRig.Yaw += yaw;
            }
        }

        // ================================================================== capsule + facing
        void SetCapsuleHeight(float h)
        {
            h = Mathf.Max(h, cc.radius * 2f + 0.01f);
            cc.height = h;
            cc.center = new Vector3(0, h * 0.5f + cc.skinWidth, 0);
        }

        bool HasHeadroom()
        {
            float full = config.model.height;
            Vector3 bottom = transform.position + Vector3.up * (cc.radius + cc.skinWidth + 0.05f);
            Vector3 top = transform.position + Vector3.up * (full - cc.radius);
            foreach (var c in Physics.OverlapCapsule(bottom, top, cc.radius * 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (c != cc && c.transform.root != transform.root) return false;
            return true;
        }

        void UpdateFacing(float dt, Vector3 dir)
        {
            float target = facingYaw;
            bool firstPerson = config.cam.view == CameraView.FirstPerson;
            bool tank = config.move.controlStyle == ControlStyle.Tank && !firstPerson && !IsSideView;
            if (firstPerson && camRig != null) { facingYaw = camRig.Yaw; target = facingYaw; }
            else if (State == MoveState.WallRun) target = Mathf.Atan2(wallRunDir.x, wallRunDir.z) * Mathf.Rad2Deg;
            else if (IsWallSliding) target = Mathf.Atan2(-wallNormal.x, -wallNormal.z) * Mathf.Rad2Deg;
            else if (State == MoveState.Dash || State == MoveState.Slide || State == MoveState.Grapple)
            {
                if (hv.sqrMagnitude > 0.5f) target = Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg;
            }
            else if (tank) target = facingYaw;
            else if (config.move.faceMode == FaceMode.CameraDirection && camRig != null && !IsSideView) target = camRig.Yaw;
            else if (dir.sqrMagnitude > 0.01f) target = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (!firstPerson && !tank) facingYaw = Mathf.MoveTowardsAngle(facingYaw, target, config.move.turnSpeed * dt);
            transform.rotation = Quaternion.Euler(0, facingYaw, 0);
        }
    }
}

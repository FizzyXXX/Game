using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CharacterLab
{
    /// <summary>
    /// All the player's visual effects, built from code (no effect assets needed):
    /// trails, dust puffs, air-jump bursts, dash afterimages, speed lines, wall
    /// sparks, the ground-pound shockwave, jetpack flames, auras and the grapple rope.
    /// Colours and on/off switches are in config.fx.
    /// </summary>
    public class CLEffects
    {
        public enum PuffKind { Jump, Land, Ring, Sparkles, Wall }

        readonly CharacterLabPlayer player;
        readonly Transform root;
        EffectSettings F;
        PlayerConfig cfg;
        Material glowMat, softMat, ringMat;
        ParticleSystem dust, glow, sparks, lines, feathers, aura;
        TrailRenderer trail;
        LineRenderer rope;
        bool dashing;
        float ghostT, stepT, slideDustT, wallT, auraT, jetT, hoverT, linesT;
        AuraType auraKind = (AuraType)(-1);
        readonly List<(GameObject go, Material mat, float t, float life)> ghosts = new List<(GameObject, Material, float, float)>();
        readonly List<(Transform t, Material mat, float age, float life, float r0, float r1)> rings = new List<(Transform, Material, float, float, float, float)>();

        static Texture2D softTex, ringTex;

        public CLEffects(CharacterLabPlayer p)
        {
            player = p;
            root = new GameObject("Effects").transform;
            root.SetParent(p.transform, false);
            MakeMaterials();
            dust = MakePS("Dust", softMat, false, -0.08f, 2.5f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.3f)));
            glow = MakePS("Glow", glowMat, false, 0f, 2.2f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0f)));
            sparks = MakePS("Sparks", glowMat, true, 1.2f, 0.6f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.2f)));
            lines = MakePS("SpeedLines", glowMat, true, 0f, 0f, AnimationCurve.Constant(0, 1, 1f));
            feathers = MakePS("Feathers", softMat, false, 0.12f, 2.5f, AnimationCurve.Constant(0, 1, 1f));
            var fr = feathers.rotationOverLifetime; fr.enabled = true; fr.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            aura = MakePS("Aura", glowMat, false, 0f, 1f, new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1, 0f)));

            var tgo = new GameObject("Trail");
            tgo.transform.SetParent(p.transform, false);
            trail = tgo.AddComponent<TrailRenderer>();
            trail.sharedMaterial = softMat;   // alpha-blended: shows on light AND dark ground
            trail.emitting = false;
            trail.minVertexDistance = 0.05f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.alignment = LineAlignment.View;
            trail.numCapVertices = 4;

            var rgo = new GameObject("GrappleRope");
            rgo.transform.SetParent(root, false);
            rope = rgo.AddComponent<LineRenderer>();
            rope.sharedMaterial = new Material(softMat) { mainTexture = Texture2D.whiteTexture };
            rope.positionCount = 2;
            rope.widthMultiplier = 0.06f;
            rope.enabled = false;
            rope.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Dispose()
        {
            foreach (var g in ghosts) { if (g.go) Object.Destroy(g.go); if (g.mat) Object.Destroy(g.mat); }
            ghosts.Clear();
            foreach (var r in rings) { if (r.t) Object.Destroy(r.t.gameObject); if (r.mat) Object.Destroy(r.mat); }
            rings.Clear();
        }

        // ================================================================== setup
        void MakeMaterials()
        {
            if (softTex == null) softTex = MakeTex(false);
            if (ringTex == null) ringTex = MakeTex(true);
            glowMat = NewMat("CharacterLab/FX/CLGlow", "Legacy Shaders/Particles/Additive", softTex);
            softMat = NewMat("CharacterLab/FX/CLSoft", "Legacy Shaders/Particles/Alpha Blended", softTex);
            ringMat = NewMat("CharacterLab/FX/CLGlow", "Legacy Shaders/Particles/Additive", ringTex);
        }

        static Material NewMat(string resource, string shaderName, Texture2D tex)
        {
            var template = Resources.Load<Material>(resource);
            Material m;
            if (template != null) m = new Material(template);
            else
            {
                var sh = Shader.Find(shaderName) ?? Shader.Find("Sprites/Default");
                m = new Material(sh);
            }
            m.mainTexture = tex;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return m;
        }

        static Texture2D MakeTex(bool ring)
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = ring ? "CL Ring" : "CL Soft" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.14f) : Mathf.Clamp01(1f - r);
                    if (!ring) a = a * a * (3f - 2f * a);
                    px[y * N + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        ParticleSystem MakePS(string name, Material mat, bool stretch, float gravity, float drag, AnimationCurve size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 1500;
            main.gravityModifier = gravity;
            main.startSpeed = 0;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, size);
            if (drag > 0)
            {
                var lim = ps.limitVelocityOverLifetime;
                lim.enabled = true;
                lim.drag = drag;
                lim.multiplyDragByParticleSize = false;
                lim.multiplyDragByParticleVelocity = false;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }

        public void Apply(PlayerConfig c)
        {
            cfg = c;
            F = c.fx;
            float h = c.model.height;
            trail.time = F.trailTime;
            trail.widthMultiplier = F.trailWidth * Mathf.Clamp(h / 1.8f, 0.4f, 2.5f);
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(F.color, 0), new GradientColorKey(F.color2, 1) },
                       new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0, 1) });
            trail.colorGradient = tg;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            trail.transform.localPosition = Vector3.up * h * 0.5f;
            if (F.trail == TrailMode.Off) trail.Clear();
            rope.startColor = Color.Lerp(F.color, Color.black, 0.25f);
            rope.endColor = Color.Lerp(F.color2, Color.black, 0.25f);
            if (auraKind != F.aura)
            {
                auraKind = F.aura;
                aura.Clear();
                var r = aura.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = F.aura == AuraType.Bubbles ? softMat : glowMat;
                var main = aura.main;
                main.gravityModifier = 0f;
            }
        }

        public void ResetTrail() => trail.Clear();

        float H => cfg != null ? cfg.model.height : 1.8f;
        float S => Mathf.Clamp(H / 1.8f, 0.4f, 3f);
        Vector3 Center => player.transform.position + Vector3.up * H * 0.5f;

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color color, float rot = 0f)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = rot,
                applyShapeToPosition = false,
            };
            ps.Emit(e, 1);
        }

        void RingPulse(Vector3 pos, Color color, float r0, float r1, float life)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Ring";
            go.transform.position = pos + Vector3.up * 0.05f;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            var mr = go.GetComponent<MeshRenderer>();
            var m = new Material(ringMat);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            rings.Add((go.transform, m, 0f, life, r0, r1));
            SetMatColor(m, color);
            go.transform.localScale = Vector3.one * r0 * 2f;
        }

        static void SetMatColor(Material m, Color c)
        {
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c * 0.5f);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        Color Mix(float t) => Color.Lerp(F.color, F.color2, t);

        // ================================================================== one-shot effects
        public void Puff(Vector3 feet, PuffKind kind)
        {
            if (F == null) return;
            float s = S;
            switch (kind)
            {
                case PuffKind.Jump:
                    if (!F.dust) return;
                    for (int i = 0; i < 10; i++)
                    {
                        Vector3 d = Quaternion.Euler(0, i * 36f + Random.Range(-10f, 10f), 0) * Vector3.forward;
                        Emit(dust, feet + d * 0.2f * s + Vector3.up * 0.1f, d * Random.Range(1.5f, 3f) * s + Vector3.up * 0.4f, Random.Range(0.35f, 0.55f) * s, Random.Range(0.35f, 0.55f), F.dustColor);
                    }
                    break;
                case PuffKind.Land:
                    if (!F.dust) return;
                    for (int i = 0; i < 16; i++)
                    {
                        Vector3 d = Quaternion.Euler(0, i * 22.5f + Random.Range(-8f, 8f), 0) * Vector3.forward;
                        Emit(dust, feet + d * 0.3f * s + Vector3.up * 0.1f, d * Random.Range(2.5f, 4.5f) * s, Random.Range(0.4f, 0.7f) * s, Random.Range(0.4f, 0.7f), F.dustColor);
                    }
                    break;
                case PuffKind.Ring:
                    RingPulse(feet, F.color, 0.3f * s, 1.6f * s, 0.35f);
                    for (int i = 0; i < 14; i++)
                    {
                        Vector3 d = Quaternion.Euler(0, i * (360f / 14f), 0) * Vector3.forward;
                        Emit(glow, feet + Vector3.up * 0.15f, d * 5f * s, 0.25f * s, 0.4f, Mix(i / 14f));
                    }
                    break;
                case PuffKind.Sparkles:
                    for (int i = 0; i < 30; i++)
                        Emit(sparks, feet + Vector3.up * H * Random.Range(0.1f, 0.9f), Random.insideUnitSphere * 4f * s + Vector3.up * 3f, Random.Range(0.08f, 0.16f) * s, Random.Range(0.5f, 0.9f), Mix(Random.value));
                    break;
                case PuffKind.Wall:
                    for (int i = 0; i < 12; i++)
                        Emit(dust, feet + Random.insideUnitSphere * 0.2f, (player.WallNormal * 2f + Random.insideUnitSphere * 1.5f) * s, Random.Range(0.3f, 0.5f) * s, 0.45f, F.dustColor);
                    if (F.wallSparks)
                        for (int i = 0; i < 10; i++)
                            Emit(sparks, feet, (player.WallNormal * 4f + Random.insideUnitSphere * 3f + Vector3.up * 2f) * s, 0.08f * s, 0.4f, F.color);
                    break;
            }
        }

        public void AirJump(Vector3 feet)
        {
            if (F == null) return;
            float s = S;
            switch (F.airJumpFx)
            {
                case AirJumpEffect.Ring:
                    RingPulse(feet, F.color, 0.25f * s, 1.3f * s, 0.32f);
                    for (int i = 0; i < 18; i++)
                    {
                        Vector3 d = Quaternion.Euler(0, i * 20f, 0) * Vector3.forward;
                        Emit(glow, feet, d * 4.5f * s + Vector3.down * 1.5f, 0.22f * s, 0.35f, Mix(i / 18f));
                    }
                    break;
                case AirJumpEffect.Sparkles:
                    for (int i = 0; i < 36; i++)
                        Emit(sparks, feet + Random.insideUnitSphere * 0.3f * s, (Random.insideUnitSphere * 5f + Vector3.down * 2f) * s, Random.Range(0.07f, 0.15f) * s, Random.Range(0.4f, 0.8f), Mix(Random.value));
                    break;
                case AirJumpEffect.Feathers:
                    for (int i = 0; i < 14; i++)
                        Emit(feathers, feet + Random.insideUnitSphere * 0.4f * s, (Random.insideUnitSphere * 2.5f + Vector3.down * 0.5f) * s, Random.Range(0.25f, 0.4f) * s, Random.Range(1.0f, 1.6f), Color.Lerp(Color.white, F.color, Random.Range(0f, 0.5f)), Random.Range(0, 6.28f));
                    break;
                case AirJumpEffect.Clouds:
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 d = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                        Emit(dust, feet + d * 0.3f * s, (d * 2.5f + Vector3.down * 1.2f) * s, Random.Range(0.7f, 1.0f) * s, Random.Range(0.6f, 0.9f), Color.Lerp(Color.white, F.color, 0.15f));
                    }
                    break;
            }
        }

        public void Dash(bool start)
        {
            dashing = start;
            if (!start || F == null) return;
            ghostT = 0;
            float s = S;
            for (int i = 0; i < 16; i++)
                Emit(sparks, Center, (Random.insideUnitSphere * 4f - player.Velocity.normalized * 3f) * s, 0.1f * s, 0.35f, Mix(Random.value));
            RingPulseVertical(Center, -player.Velocity.normalized, F.color, 0.3f * s, 1.2f * s, 0.25f);
        }

        void RingPulseVertical(Vector3 pos, Vector3 normal, Color color, float r0, float r1, float life)
        {
            if (normal.sqrMagnitude < 0.01f) return;
            RingPulse(pos, color, r0, r1, life);
            var r = rings[rings.Count - 1];
            r.t.position = pos;
            r.t.rotation = Quaternion.LookRotation(-normal);
        }

        public void SlideDust(float dt)
        {
            if (F == null || !F.dust) return;
            slideDustT -= dt;
            if (slideDustT > 0) return;
            slideDustT = 0.03f;
            float s = S;
            Vector3 back = -player.Velocity.normalized;
            Emit(dust, player.transform.position + Random.insideUnitSphere * 0.2f * s + Vector3.up * 0.1f, (back * 1.5f + Vector3.up * 1.2f + Random.insideUnitSphere) * s, Random.Range(0.3f, 0.55f) * s, 0.5f, F.dustColor);
        }

        public void WallSparks(Vector3 point, float dt)
        {
            if (F == null) return;
            wallT -= dt;
            if (wallT > 0) return;
            wallT = 0.04f;
            float s = S;
            if (F.wallSparks)
                Emit(sparks, point + Vector3.up * Random.Range(-0.2f, 0.4f), (player.WallNormal * 1.5f + Random.insideUnitSphere * 2f + Vector3.up * 1.5f) * s, 0.07f * s, 0.35f, F.color);
            if (F.dust)
                Emit(dust, point, (player.WallNormal + Vector3.up * 0.5f) * s, 0.3f * s, 0.4f, F.dustColor);
        }

        public void Shockwave(Vector3 feet)
        {
            if (F == null) return;
            float s = S;
            if (F.landShockwave)
            {
                RingPulse(feet, F.color, 0.4f * s, 4.5f * s, 0.45f);
                RingPulse(feet, F.color2, 0.2f * s, 2.8f * s, 0.35f);
                for (int i = 0; i < 24; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * 15f, 0) * Vector3.forward;
                    Emit(sparks, feet + Vector3.up * 0.2f, (d * 9f + Vector3.up * 3f) * s, 0.12f * s, 0.45f, Mix(i / 24f));
                }
            }
            if (F.dust)
                for (int i = 0; i < 28; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * (360f / 28f), 0) * Vector3.forward;
                    Emit(dust, feet + d * 0.4f * s + Vector3.up * 0.15f, d * Random.Range(5f, 8f) * s + Vector3.up * 0.8f, Random.Range(0.6f, 1f) * s, Random.Range(0.5f, 0.8f), F.dustColor);
                }
        }

        public void Rope(bool on, Vector3 anchor)
        {
            rope.enabled = on;
            if (!on) return;
            rope.SetPosition(0, player.transform.position + Vector3.up * H * 0.6f);
            rope.SetPosition(1, anchor);
            rope.widthMultiplier = 0.09f * S;
        }

        public void GrappleMiss(Vector3 to)
        {
            if (F == null) return;
            for (int i = 0; i < 8; i++)
                Emit(sparks, Vector3.Lerp(Center, to, i / 8f), Random.insideUnitSphere, 0.06f, 0.25f, Color.gray);
        }

        // ================================================================== every frame
        public void Tick(float dt)
        {
            if (F == null) return;
            float s = S;
            var st = player.State;
            bool grounded = player.IsGrounded;
            float speed = player.HorizontalSpeed;

            // ---- trail
            bool trailOn = false;
            switch (F.trail)
            {
                case TrailMode.Always: trailOn = true; break;
                case TrailMode.Sprinting: trailOn = (player.IsSprinting && speed > cfg.move.walkSpeed) || st == CharacterLabPlayer.MoveState.Dash || st == CharacterLabPlayer.MoveState.Slide; break;
                case TrailMode.Dashing: trailOn = st == CharacterLabPlayer.MoveState.Dash || st == CharacterLabPlayer.MoveState.Grapple || st == CharacterLabPlayer.MoveState.GroundPound; break;
                case TrailMode.InAir: trailOn = !grounded; break;
            }
            trail.emitting = trailOn;

            // ---- dash afterimages
            if (dashing && F.afterimages)
            {
                ghostT -= dt;
                if (ghostT <= 0)
                {
                    ghostT = 0.035f;
                    var m = new Material(softMat) { mainTexture = Texture2D.whiteTexture };
                    var go = player.Model?.Ghost(m);
                    if (go != null) ghosts.Add((go, m, 0f, 0.3f));
                    else Object.Destroy(m);
                }
            }
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var g = ghosts[i];
                g.t += dt;
                if (g.t >= g.life || g.go == null)
                {
                    if (g.go) Object.Destroy(g.go);
                    Object.Destroy(g.mat);
                    ghosts.RemoveAt(i);
                    continue;
                }
                float a = 1f - g.t / g.life;
                Color gc = Mix(g.t / g.life);
                gc.a = 0.45f * a;
                SetMatColor(g.mat, gc);
                ghosts[i] = g;
            }

            // ---- expanding rings
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.age += dt;
                if (r.age >= r.life || r.t == null)
                {
                    if (r.t) Object.Destroy(r.t.gameObject);
                    Object.Destroy(r.mat);
                    rings.RemoveAt(i);
                    continue;
                }
                float k = r.age / r.life;
                float e = 1f - (1f - k) * (1f - k);
                r.t.localScale = Vector3.one * Mathf.Lerp(r.r0, r.r1, e) * 2f;
                Color c = r.mat.HasProperty("_TintColor") ? r.mat.GetColor("_TintColor") * 2f : r.mat.color;
                c.a = 1f - k;
                SetMatColor(r.mat, c);
                rings[i] = r;
            }

            // ---- footstep dust
            if (grounded && speed > 3.5f && F.dust && st == CharacterLabPlayer.MoveState.Normal)
            {
                stepT -= dt * speed;
                if (stepT <= 0)
                {
                    stepT = 1.4f * H;
                    Vector3 p = player.transform.position + Random.insideUnitSphere * 0.15f;
                    p.y = player.transform.position.y + 0.05f;
                    Emit(dust, p, (Vector3.up * 0.6f - player.Velocity.normalized * 0.8f) * s, Random.Range(0.25f, 0.4f) * s * Mathf.Clamp(speed / 8f, 0.6f, 1.4f), 0.4f, F.dustColor);
                }
            }

            // ---- speed lines
            float fast = Mathf.Max(cfg.move.sprintSpeed, cfg.move.walkSpeed) * 1.05f;
            Vector3 vel = player.Velocity;
            if (F.speedLines && (vel.magnitude > fast || st == CharacterLabPlayer.MoveState.Dash || player.IsGliding))
            {
                linesT -= dt;
                if (linesT <= 0)
                {
                    linesT = 0.012f;
                    Vector3 dir = vel.normalized;
                    Vector3 side = Vector3.Cross(dir, Vector3.up);
                    if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                    side.Normalize();
                    Vector3 up = Vector3.Cross(side, dir);
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    float rad = Random.Range(0.9f, 2.2f) * s;
                    Vector3 p = Center + dir * Random.Range(1f, 4f) * s + (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * rad;
                    Emit(lines, p, -dir * 25f + vel, 0.04f * s, 0.22f, Color.Lerp(Color.white, F.color, 0.3f) * 0.8f);
                }
            }

            // ---- jetpack / hover flames
            if (player.IsJetpacking)
            {
                jetT -= dt;
                if (jetT <= 0)
                {
                    jetT = 0.012f;
                    bool hover = cfg.jump.airHold == AirHoldMode.Hover;
                    Vector3 back = player.transform.position + Vector3.up * H * (hover ? 0.05f : 0.45f) - player.transform.forward * cfg.model.radius * (hover ? 0f : 0.9f);
                    Vector3 v = Vector3.down * Random.Range(5f, 8f) * s + Random.insideUnitSphere * 0.8f;
                    Emit(glow, back + Random.insideUnitSphere * 0.08f * s, v, Random.Range(0.25f, 0.4f) * s, Random.Range(0.18f, 0.3f), Mix(Random.Range(0f, 0.7f)));
                    if (hover)
                    {
                        hoverT -= 0.012f;
                        if (hoverT <= 0) { hoverT = 0.15f; RingPulse(player.transform.position, F.color, 0.2f * s, 0.9f * s, 0.25f); }
                    }
                }
            }
            if (player.IsWallSliding) WallSparks(player.transform.position + Vector3.up * H * 0.5f - player.WallNormal * cfg.model.radius, dt);

            // ---- aura
            if (F.aura != AuraType.None)
            {
                auraT -= dt;
                float rate = F.aura == AuraType.Fire ? 0.02f : F.aura == AuraType.Sparkles ? 0.06f : F.aura == AuraType.Bubbles ? 0.15f : 0.08f;
                while (auraT <= 0)
                {
                    auraT += rate;
                    Vector3 basePos = player.transform.position;
                    float rr = cfg.model.radius * 1.2f;
                    switch (F.aura)
                    {
                        case AuraType.Sparkles:
                            Emit(aura, basePos + new Vector3(Random.Range(-rr, rr), Random.Range(0.1f, H), Random.Range(-rr, rr)), Vector3.up * 0.4f, Random.Range(0.1f, 0.2f) * s, Random.Range(0.6f, 1f), F.auraColor);
                            break;
                        case AuraType.Fire:
                            Emit(aura, basePos + new Vector3(Random.Range(-rr, rr) * 0.8f, Random.Range(0f, H * 0.6f), Random.Range(-rr, rr) * 0.8f), Vector3.up * Random.Range(1.5f, 3f) * s + player.Velocity * 0.6f, Random.Range(0.35f, 0.55f) * s, Random.Range(0.35f, 0.6f), Color.Lerp(F.auraColor, F.color2, Random.value * 0.4f) * 0.7f);
                            break;
                        case AuraType.Bubbles:
                            Emit(aura, basePos + new Vector3(Random.Range(-rr, rr), Random.Range(0f, H * 0.7f), Random.Range(-rr, rr)), Vector3.up * Random.Range(0.6f, 1.2f) + Random.insideUnitSphere * 0.3f, Random.Range(0.15f, 0.3f) * s, Random.Range(1f, 1.6f), new Color(F.auraColor.r, F.auraColor.g, F.auraColor.b, 0.5f));
                            break;
                        case AuraType.Glow:
                            Emit(aura, basePos + Vector3.up * H * 0.5f, player.Velocity * 0.9f, H * 1.5f, 0.35f, F.auraColor * 0.22f);
                            break;
                    }
                }
            }
        }
    }
}

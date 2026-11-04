using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace CharacterLab
{
    /// <summary>
    /// The player's look: loads the model, sizes it to the capsule, recolours it,
    /// plays its own animations (idle / walk / run / jump / roll...) if it has any,
    /// and adds cartoon motion on top (squash &amp; stretch, leaning, flips).
    /// Hierarchy under the player:  Visual (squash/lean) / Spin (flips) / Holder (fit) / your model
    /// </summary>
    public class CLModel
    {
        readonly CharacterLabPlayer player;
        public Transform Visual, Spin, Holder;
        public GameObject Instance;
        string loadedKey;
        /// <summary>The model key on show right now (null while a stand-in is shown).</summary>
        public string LoadedKey => loadedKey;
        GameObject loadedOverride;
        PlayerConfig cfg;
        float height = 1.8f;

        readonly List<Renderer> renderers = new List<Renderer>();
        bool firstPerson;

        // procedural motion
        float squash = 1f, squashVel;
        float leanPitch, leanRoll, lastYaw;
        Vector3 lastVel;
        float trickT = 99f, trickDur = 0.45f;
        FlipOnAirJump trick;
        float walkPhase, actionT = 99f;
        readonly List<(Transform t, Quaternion rest, int side, bool arm)> limbs = new List<(Transform, Quaternion, int, bool)>();

        // clips
        CLClips clips;

        public CLModel(CharacterLabPlayer p)
        {
            player = p;
            Visual = Child(p.transform, "Visual");
            Spin = Child(Visual, "Spin");
            Holder = Child(Spin, "Holder");
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            return t;
        }

        public void Dispose()
        {
            clips?.Dispose();
            DestroyMaterialCopies();
        }

        /// <summary>renderer.materials made copies for recolouring; Unity doesn't free those with the model.</summary>
        void DestroyMaterialCopies()
        {
            foreach (var m in colorCache.Keys) if (m != null) Object.Destroy(m);
            colorCache.Clear();
        }

        // ================================================================== loading
        /// <summary>Finds the model for a key: the override, then the lab's runtime
        /// provider, then Resources/CharacterLab/Models/&lt;key&gt;.</summary>
        public static GameObject FindModelPrefab(string key, GameObject overrideModel)
        {
            if (overrideModel != null) return overrideModel;
            GameObject go = null;
            if (CharacterLabPlayer.ModelProvider != null && !string.IsNullOrEmpty(key)) go = CharacterLabPlayer.ModelProvider(key);
            if (go == null && !string.IsNullOrEmpty(key)) go = Resources.Load<GameObject>("CharacterLab/Models/" + key);
            if (go == null)
            {
                // Any model in the folder will do (e.g. the file was renamed).
                var all = Resources.LoadAll<GameObject>("CharacterLab/Models");
                if (all.Length > 0) go = all[0];
            }
            return go;
        }

        public void Apply(PlayerConfig c, GameObject overrideModel)
        {
            cfg = c;
            height = c.model.height;
            string key = c.model.model;
            if (Instance == null || key != loadedKey || overrideModel != loadedOverride)
            {
                Load(key, overrideModel);
            }
            Visual.localPosition = Vector3.zero;
            Spin.localPosition = Vector3.up * height * 0.5f;
            Holder.localPosition = -Vector3.up * height * 0.5f;
            Fit(Instance.transform, Holder, c);
            ApplyColors(Instance, c, false, colorCache);
            clips?.SetUse(c.anim.useClips);
            SetFirstPerson(firstPerson);
        }

        void Load(string key, GameObject overrideModel)
        {
            clips?.Dispose();
            clips = null;
            if (Instance != null) Object.Destroy(Instance);
            DestroyMaterialCopies();
            loadedOverride = overrideModel;
            var src = FindModelPrefab(key, overrideModel);
            // A stand-in (the model isn't there yet, e.g. the lab is still downloading it) doesn't count
            // as loaded, so the next ApplyConfig tries the real one again.
            bool exact = overrideModel != null || (CharacterLabPlayer.ModelProvider != null && !string.IsNullOrEmpty(key) && CharacterLabPlayer.ModelProvider(key) != null)
                         || (!string.IsNullOrEmpty(key) && Resources.Load<GameObject>("CharacterLab/Models/" + key) != null);
            loadedKey = exact ? key : null;
            if (!exact && CharacterLabPlayer.ModelProvider == null && !string.IsNullOrEmpty(key))
                Debug.LogWarning("[Character Lab] The model \"" + key + "\" isn't in a CharacterLab/Resources/CharacterLab/Models folder (or didn't import), "
                                 + "so a stand-in is shown. A .glb model needs the glTFast package to import without errors (Window > Package Manager); "
                                 + "see CharacterLab/README.txt.", player);
            if (src == null)
            {
                Instance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.Destroy(Instance.GetComponent<Collider>());
                Instance.transform.SetParent(Holder, false);
            }
            else
            {
                Instance = Object.Instantiate(src, Holder);
                Instance.name = src.name;
                if (!Instance.activeSelf) Instance.SetActive(true);
            }
            // The player's CharacterController does the colliding; models shouldn't.
            foreach (var col in Instance.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            foreach (var rb in Instance.GetComponentsInChildren<Rigidbody>()) Object.Destroy(rb);
            renderers.Clear();
            renderers.AddRange(Instance.GetComponentsInChildren<Renderer>(true));
            foreach (var r in renderers)
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;   // fitted scale can make bounds wrong
            colorCache.Clear();
            FindLimbs();
            clips = CLClips.Create(Instance, key, overrideModel, player);
            clips?.SampleFirstPose();   // so Fit() measures the model the way it will actually look
            refitFrames = Instance.GetComponentInChildren<SkinnedMeshRenderer>() != null ? 2 : 0;
        }

        /// <summary>Scales and moves a model so it stands on the ground and is as tall as the player.</summary>
        public static void Fit(Transform inst, Transform holder, PlayerConfig c)
        {
            var mo = c.model;
            // Remember the model's own root rotation/scale (importers often put the axis fix there)
            // and measure it ONCE (posed as its first animation frame when it has clips), so
            // changing settings never makes it jump around and odd unit setups still fit.
            var info = inst.GetComponent<CLFitInfo>();
            if (info == null)
            {
                info = inst.gameObject.AddComponent<CLFitInfo>();
                info.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
                info.rotation = inst.localRotation;
                info.scale = inst.localScale;
            }
            if (!info.measured)
            {
                inst.localPosition = Vector3.zero;
                inst.localRotation = info.rotation;
                inst.localScale = info.scale;
                if (!Measure(inst, holder, out info.bounds)) return;
                info.measured = true;
            }
            Bounds b = info.bounds;
            if (b.size.y < 0.0001f) return;
            float s = c.model.height * mo.modelScale / b.size.y;
            Quaternion yaw = Quaternion.Euler(0, 90f * (int)mo.modelYaw, 0);
            inst.localRotation = yaw * info.rotation;
            inst.localScale = info.scale * s;
            Vector3 center = yaw * (b.center * s);
            inst.localPosition = new Vector3(-center.x, -(b.center.y - b.extents.y) * s + mo.modelOffsetY, -center.z);
        }

        /// <summary>Measures the model again from what Unity actually drew last frame
        /// (exact for skinned/animated models) and refits it.</summary>
        public static void Refit(Transform inst, Transform holder, PlayerConfig c)
        {
            var info = inst.GetComponent<CLFitInfo>();
            if (info == null) return;
            // Put the model back to its raw size/placement, measure, then fit again.
            var yaw = Quaternion.Euler(0, 90f * (int)c.model.modelYaw, 0);
            Vector3 pos = inst.localPosition; Quaternion rot = inst.localRotation; Vector3 scl = inst.localScale;
            if (!Measure(inst, holder, out var b, true)) return;
            float k = scl.y / Mathf.Max(0.000001f, info.scale.y);   // how much Fit scaled it
            // b is in holder space for the fitted transform; convert back to the raw, un-yawed model.
            Vector3 c0 = Quaternion.Inverse(yaw) * (b.center - pos) / k;
            Vector3 e0 = Quaternion.Inverse(yaw) * b.extents / k;
            info.bounds = new Bounds(c0, new Vector3(Mathf.Abs(e0.x), Mathf.Abs(e0.y), Mathf.Abs(e0.z)) * 2f);
            info.measured = true;
            Fit(inst, holder, c);
        }

        /// <summary>The model's bounds in the holder's space, from its current pose.</summary>
        static bool Measure(Transform inst, Transform holder, out Bounds b, bool skinned = false)
        {
            bool any = false;
            b = new Bounds();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Bounds wb;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    // After a frame of skinning (updateWhenOffscreen) Unity's own bounds are exact;
                    // before that, the skin's local bounds around its root bone are a good guess.
                    // (BakeMesh was unreliable: its scale handling broke on rigs with scaled parents.)
                    if (skinned) wb = r.bounds;
                    else wb = TransformBounds(smr.localBounds, (smr.rootBone != null ? smr.rootBone : smr.transform).localToWorldMatrix);
                }
                else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                    wb = TransformBounds(mf.sharedMesh.bounds, r.transform.localToWorldMatrix);
                else wb = r.bounds;
                Bounds lb = TransformBounds(wb, holder.worldToLocalMatrix);
                if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
            }
            return any;
        }

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            Vector3 c = m.MultiplyPoint3x4(b.center);
            Vector3 e = b.extents;
            Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0, 0)), ay = m.MultiplyVector(new Vector3(0, e.y, 0)), az = m.MultiplyVector(new Vector3(0, 0, e.z));
            Vector3 ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                                      Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                                      Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2f);
        }

        // ================================================================== colours
        public class Orig { public Color color; public string prop; public bool textured; }
        readonly Dictionary<Material, Orig> colorCache = new Dictionary<Material, Orig>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color"),
            GltfColorId = Shader.PropertyToID("baseColorFactor");

        public static string PartName(Material m) => m == null ? "" : m.name.Replace(" (Instance)", "").Trim();

        /// <summary>Every material name on the model (the "parts" you can recolour).</summary>
        public List<string> PartNames()
        {
            var list = new List<string>();
            if (Instance == null) return list;
            foreach (var r in Instance.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    string n = PartName(m);
                    if (n != "" && !list.Contains(n)) list.Add(n);
                }
            return list;
        }

        /// <summary>The colour each part has before any recolouring (for the lab's colour pickers).</summary>
        public Color OriginalColor(string part)
        {
            foreach (var kv in colorCache)
                if (PartName(kv.Key) == part) return kv.Value.color;
            return Color.white;
        }

        /// <summary>Applies tint + part colours. In the editor preview it works on copies.</summary>
        public static void ApplyColors(GameObject inst, PlayerConfig c, bool editorPreview, Dictionary<Material, Orig> cache = null)
        {
            if (inst == null) return;
            var mo = c.model;
            cache = cache ?? new Dictionary<Material, Orig>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Material[] mats;
                if (editorPreview)
                {
                    var shared = r.sharedMaterials;
                    mats = new Material[shared.Length];
                    for (int i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] == null) continue;
                        mats[i] = new Material(shared[i]) { name = shared[i].name, hideFlags = HideFlags.DontSave };
                    }
                    r.sharedMaterials = mats;
                }
                else mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (!cache.TryGetValue(m, out var o))
                    {
                        o = new Orig();
                        o.prop = m.HasProperty(BaseColorId) ? "_BaseColor" : m.HasProperty(GltfColorId) ? "baseColorFactor" : m.HasProperty(ColorId) ? "_Color" : null;
                        o.color = o.prop != null ? m.GetColor(o.prop) : Color.white;
                        o.textured = m.mainTexture != null || (m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture") != null);
                        // Many exported models arrive with an alpha of 0 (an FBX transparency quirk)
                        // and would be invisible: show them fully opaque instead.
                        if (o.prop != null && o.color.a < 0.05f)
                        {
                            o.color.a = 1f;
                            m.SetColor(o.prop, o.color);
                        }
                        cache[m] = o;
                    }
                    if (o.prop == null) continue;
                    Color col = o.color;
                    string pn = PartName(m);
                    if (mo.parts != null)
                        foreach (var p in mo.parts)
                            if (p.name == pn) { col = p.color; col.a = o.color.a; break; }
                    if (mo.tintStrength > 0.001f)
                    {
                        Color t = mo.tint;
                        // Keep the part's light/dark shading while pulling it toward the tint.
                        float lum = o.textured ? 1f : Mathf.Clamp(col.grayscale * 1.4f + 0.15f, 0.2f, 1.2f);
                        Color target = new Color(t.r * lum, t.g * lum, t.b * lum, col.a);
                        col = Color.Lerp(col, target, mo.tintStrength);
                    }
                    m.SetColor(o.prop, col);
                }
            }
        }

        public void SetFirstPerson(bool hide)
        {
            firstPerson = hide;
            foreach (var r in renderers)
                if (r != null) r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        // ================================================================== reactions
        public void OnJump()
        {
            squashVel += 6f * cfg.anim.squashStretch;
            clips?.Trigger("jump");
        }

        public void OnLand(float fallSpeed)
        {
            float k = Mathf.Clamp01(fallSpeed / 18f);
            squashVel -= (3f + 9f * k) * cfg.anim.squashStretch;
        }

        public void Trick(FlipOnAirJump kind)
        {
            if (kind == FlipOnAirJump.None) { squashVel += 4f * cfg.anim.squashStretch; return; }
            trick = kind;
            trickT = 0;
            trickDur = kind == FlipOnAirJump.Spin ? 0.4f : 0.45f;
            squashVel += 3f * cfg.anim.squashStretch;
            clips?.Trigger("jump");
        }

        public void PlayAction()
        {
            actionT = 0;
            clips?.Trigger("action");
        }

        // ================================================================== every frame
        public void Tick(float dt)
        {
            if (Instance == null || cfg == null) return;
            var a = cfg.anim;
            var st = player.State;
            Vector3 vel = player.Velocity;
            float speed = player.HorizontalSpeed;

            // ---- squash & stretch spring
            float stretchTarget = 1f;
            if (!player.IsGrounded) stretchTarget = 1f + Mathf.Clamp(vel.y * 0.012f, -0.12f, 0.15f) * a.squashStretch * 2f;
            if (st == CharacterLabPlayer.MoveState.GroundPound) stretchTarget = 1f + 0.3f * a.squashStretch;
            if (player.IsCrouching && st != CharacterLabPlayer.MoveState.Slide) stretchTarget *= Mathf.Lerp(1f, cfg.move.crouchHeight, clips != null && clips.HasClips ? 0.6f : 1f);
            squashVel += (stretchTarget - squash) * 180f * dt;
            squashVel *= Mathf.Exp(-14f * dt);
            squash += squashVel * dt;
            squash = Mathf.Clamp(squash, 0.45f, 1.7f);

            // ---- breathing / bob for models with no animation of their own
            bool clipDriven = clips != null && clips.Active;
            float bobY = 0f, bobScale = 1f;
            if (player.IsGrounded && speed > 0.3f) walkPhase += dt * (4f + speed * 1.3f) * a.animSpeed;
            else walkPhase = Mathf.MoveTowards(walkPhase, Mathf.Round(walkPhase / Mathf.PI) * Mathf.PI, dt * 4f);
            if (!clipDriven)
            {
                if (player.IsGrounded && speed > 0.3f) bobY = Mathf.Abs(Mathf.Sin(walkPhase)) * 0.07f * height * a.bob * Mathf.Clamp01(speed / 4f);
                else if (player.IsGrounded) bobScale = 1f + Mathf.Sin(Time.time * 2.2f) * 0.018f * a.bob;
                SwingLimbs(dt, speed);
            }

            // ---- lean into acceleration and turns
            Vector3 accel = (vel - lastVel) / Mathf.Max(dt, 0.001f);
            lastVel = vel;
            accel.y = 0;
            float yaw = player.transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / Mathf.Max(dt, 0.001f);
            lastYaw = yaw;
            Vector3 localAcc = player.transform.InverseTransformDirection(accel);
            float wantPitch = (Mathf.Clamp(localAcc.z * 0.6f, -12f, 14f) + Mathf.Clamp(speed * 0.9f, 0, 10f)) * a.lean;
            float wantRoll = Mathf.Clamp(-yawRate * speed * 0.004f, -20f, 20f) * a.lean;

            // ---- poses for special moves
            float extraPitch = 0f, extraRoll = 0f, spinY = 0f, lower = 0f;
            switch (st)
            {
                case CharacterLabPlayer.MoveState.Dash:
                    if (a.dashPose == DashPose.Lean) extraPitch = 28f;
                    else if (a.dashPose == DashPose.Spin) spinY = Time.time * 1440f;
                    else if (!(clips != null && clips.Has("roll"))) extraPitch = (Time.time * 900f) % 360f;
                    break;
                case CharacterLabPlayer.MoveState.Slide:
                    if (a.slidePose == SlidePose.LeanBack) { extraPitch = -32f; lower = 0.05f; }
                    else if (a.slidePose == SlidePose.Belly) { extraPitch = 75f; lower = 0.25f; }
                    else if (!(clips != null && clips.Has("roll"))) extraPitch = (Time.time * 700f) % 360f;
                    break;
                case CharacterLabPlayer.MoveState.WallRun:
                    extraRoll = 18f * Mathf.Sign(Vector3.Dot(player.transform.right, player.WallNormal));
                    break;
                case CharacterLabPlayer.MoveState.Grapple:
                    extraPitch = 15f;
                    break;
            }
            if (player.IsGliding) extraPitch = 40f;
            if (player.IsJetpacking) extraPitch = -6f;
            if (player.IsWallSliding) extraPitch = -8f;

            leanPitch = Mathf.Lerp(leanPitch, wantPitch, 1f - Mathf.Exp(-10f * dt));
            leanRoll = Mathf.Lerp(leanRoll, wantRoll, 1f - Mathf.Exp(-8f * dt));
            float pitchNow = leanPitch + extraPitch;

            // ---- Visual: lean + squash at the feet
            float sy = squash * bobScale;
            float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, squash));
            Visual.localPosition = new Vector3(0, bobY - lower * height, 0);
            Visual.localRotation = Quaternion.Euler(Mathf.Abs(extraPitch) > 60f && st != CharacterLabPlayer.MoveState.Slide ? 0 : pitchNow, 0, leanRoll + extraRoll);
            Visual.localScale = new Vector3(sxz, sy, sxz);

            // ---- Spin: flips and rolls around the body's middle
            Quaternion spin = Quaternion.identity;
            if (Mathf.Abs(extraPitch) > 60f && st != CharacterLabPlayer.MoveState.Slide) spin = Quaternion.Euler(extraPitch, 0, 0);
            if (st == CharacterLabPlayer.MoveState.Slide && Mathf.Abs(extraPitch) > 60f && a.slidePose == SlidePose.Roll) spin = Quaternion.Euler(extraPitch, 0, 0);
            if (spinY != 0f) spin = Quaternion.Euler(0, spinY, 0);
            if (trickT < trickDur)
            {
                trickT += dt;
                float t = Mathf.Clamp01(trickT / trickDur);
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                switch (trick)
                {
                    case FlipOnAirJump.FrontFlip: spin = Quaternion.Euler(360f * e, 0, 0) * spin; break;
                    case FlipOnAirJump.BackFlip: spin = Quaternion.Euler(-360f * e, 0, 0) * spin; break;
                    case FlipOnAirJump.Spin: spin = Quaternion.Euler(0, 360f * e, 0) * spin; break;
                }
            }
            Spin.localRotation = spin;

            // ---- action without a clip: a quick hop and spin
            if (actionT < 0.5f)
            {
                actionT += dt;
                if (clips == null || !clips.Has("action"))
                {
                    float t = actionT / 0.5f;
                    Spin.localRotation *= Quaternion.Euler(0, 360f * Mathf.SmoothStep(0, 1, t), 0);
                    Visual.localPosition += Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.25f;
                }
            }

            clips?.Tick(dt, player, cfg);
        }

        int refitFrames;

        public void LateTick(float dt)
        {
            // Skinned models: re-measure once Unity has skinned a frame (exact size, whatever the rig's units).
            if (refitFrames > 0 && Instance != null && --refitFrames == 0)
                Refit(Instance.transform, Holder, cfg);
        }

        // ---- swing arms/legs of unrigged models whose parts are named like "Arm.L", "LeftLeg"...
        void FindLimbs()
        {
            limbs.Clear();
            if (Instance == null) return;
            if (Instance.GetComponentInChildren<SkinnedMeshRenderer>() != null) return;
            foreach (var t in Instance.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                bool arm = n.Contains("arm"), leg = n.Contains("leg");
                if (!arm && !leg) continue;
                int side = n.EndsWith(".l") || n.EndsWith("_l") || n.StartsWith("left") || n.Contains("left") ? 1 :
                           n.EndsWith(".r") || n.EndsWith("_r") || n.StartsWith("right") || n.Contains("right") ? -1 : 0;
                if (side == 0) continue;
                limbs.Add((t, t.localRotation, side, arm));
            }
        }

        void SwingLimbs(float dt, float speed)
        {
            if (limbs.Count == 0) return;
            float amp = player.IsGrounded ? Mathf.Clamp01(speed / 6f) * 50f : 0f;
            float airArms = player.IsGrounded ? 0f : (player.Velocity.y > 0 ? 150f : 120f);
            foreach (var l in limbs)
            {
                if (l.t == null) continue;
                float ang = Mathf.Sin(walkPhase) * amp * l.side * (l.arm ? -1f : 1f);
                Quaternion q = l.rest * Quaternion.Euler(ang, 0, 0);
                if (l.arm && airArms > 0) q = l.rest * Quaternion.Euler(0, 0, airArms * l.side * 0.6f) * Quaternion.Euler(-30f, 0, 0);
                if (!l.arm && !player.IsGrounded) q = l.rest * Quaternion.Euler(l.side * 25f, 0, 0);
                if (player.IsWallSliding && l.arm) q = l.rest * Quaternion.Euler(-150f, 0, 0);
                l.t.localRotation = Quaternion.Slerp(l.t.localRotation, q, 1f - Mathf.Exp(-14f * dt));
            }
        }

        // ================================================================== afterimages
        /// <summary>Makes a frozen copy of the model (for dash afterimages).</summary>
        public GameObject Ghost(Material mat)
        {
            if (Instance == null) return null;
            var root = new GameObject("Afterimage");
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh mesh = null;
                Matrix4x4 m;
                if (r is SkinnedMeshRenderer smr)
                {
                    // BakeMesh(useScale: true) = the current pose at world size, relative to the
                    // renderer's position and rotation.
                    mesh = new Mesh();
                    smr.BakeMesh(mesh, true);
                    m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                }
                else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                {
                    mesh = mf.sharedMesh;
                    m = r.transform.localToWorldMatrix;
                }
                else continue;
                var go = new GameObject("part");
                go.transform.SetParent(root.transform, false);
                go.transform.position = m.GetColumn(3);
                go.transform.rotation = m.rotation;
                go.transform.localScale = r is SkinnedMeshRenderer ? Vector3.one : m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer) go.AddComponent<CLDestroyMesh>().mesh = mesh;
            }
            return root;
        }
    }

    /// <summary>The model's original root rotation/scale (so refitting never loses it).</summary>
    public class CLFitInfo : MonoBehaviour
    {
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public bool measured;
        public Bounds bounds;
    }

    /// <summary>Destroys a generated mesh along with its object.</summary>
    public class CLDestroyMesh : MonoBehaviour
    {
        public Mesh mesh;
        void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}

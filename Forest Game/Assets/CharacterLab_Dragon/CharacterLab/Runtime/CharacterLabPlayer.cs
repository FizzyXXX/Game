
using System;
using UnityEngine;

namespace CharacterLab
{
    /// <summary>
    /// The player you designed in the Character Lab.
    /// Handles controls, camera, model, animations, effects and sounds.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Character Lab/Character Lab Player")]
    public partial class CharacterLabPlayer : MonoBehaviour
    {
        public PlayerConfig config = new PlayerConfig();

        [Tooltip("Optional: drag any model here.")]
        public GameObject modelOverride;

        [Tooltip("Optional: camera controlled by this player.")]
        public UnityEngine.Camera playerCamera;

        [Tooltip("Animation clips for the model.")]
        public AnimationClip[] extraClips;

        [Tooltip("Turn off to control the camera with your own script.")]
        public bool controlCamera = true;

        // Events
        public event Action Jumped, AirJumped, WallJumped,
            Dashed, GroundPounded, Respawned, Slid, Grappled;

        public event Action<float> Landed;

        // Player state
        public enum MoveState
        {
            Normal,
            Dash,
            Slide,
            WallRun,
            GroundPound,
            Grapple
        }

        public MoveState State { get; private set; }

        public Vector3 Velocity => new Vector3(hv.x, vy, hv.z);

        public float HorizontalSpeed =>
            new Vector2(hv.x, hv.z).magnitude;

        public bool IsGrounded { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsWallSliding { get; private set; }
        public bool IsGliding { get; private set; }
        public bool IsJetpacking { get; private set; }

        public float Fuel01 =>
            cfgFuel <= 0 ? 0 : fuel / cfgFuel;

        public Vector3 SpawnPoint { get; set; }

        public CLInput Input => input;
        public CLCamera CameraRig => camRig;
        public CLModel Model => model;
        public CLEffects Effects => fx;

        // Explicit Unity camera reference
        public UnityEngine.Camera Cam => cam;

        public static Func<string, GameObject> ModelProvider;
        public static Func<string, AnimationClip[]> ClipProvider;

        public static bool AllowCursorLock = true;

        // Components
        CharacterController cc;

        CLInput input;
        CLCamera camRig;
        CLModel model;
        CLEffects fx;
        CLAudio sfx;

        UnityEngine.Camera cam;

        bool started;
        string appliedJson;

        // Lifecycle
        void Awake()
        {
            cc = GetComponent<CharacterController>();

            if (!Application.isPlaying)
                return;

            RemovePreview();
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;

            SpawnPoint = transform.position;
            facingYaw = transform.eulerAngles.y;

            input = new CLInput();
            model = new CLModel(this);
            fx = new CLEffects(this);
            sfx = new CLAudio(this);

            if (controlCamera)
                SetupCamera();

            started = true;

            ApplyConfig(true);
        }

        void OnDestroy()
        {
            input?.Dispose();
            model?.Dispose();
            fx?.Dispose();

            if (!Application.isPlaying)
                RemovePreview();
        }

        void OnDisable()
        {
            if (Application.isPlaying && camRig != null)
                camRig.UnlockCursor();
        }

        // Camera setup
        void SetupCamera()
        {
            cam = playerCamera != null
                ? playerCamera
                : UnityEngine.Camera.main;

            if (cam == null)
            {
                GameObject go = new GameObject("Player Camera");

                go.tag = "MainCamera";

                cam = go.AddComponent<UnityEngine.Camera>();

                if (FindAnyObjectByType<AudioListener>() == null)
                {
                    go.AddComponent<AudioListener>();
                }
            }

            camRig = new CLCamera(this, cam);
        }

        // Configuration
        public void ApplyConfig()
        {
            ApplyConfig(false);
        }

        void ApplyConfig(bool first)
        {
            if (!started)
                return;

            appliedJson = JsonUtility.ToJson(config);

            var m = config.move;
            var mo = config.model;

            cc.slopeLimit = m.slopeLimit;

            cc.stepOffset = Mathf.Min(
                m.stepOffset,
                mo.height * 0.5f
            );

            cc.radius = Mathf.Min(
                mo.radius,
                mo.height * 0.5f
            );

            cc.skinWidth = Mathf.Clamp(
                mo.radius * 0.1f,
                0.01f,
                0.08f
            );

            cc.minMoveDistance = 0f;

            SetCapsuleHeight(
                IsCrouching
                    ? mo.height * m.crouchHeight
                    : mo.height
            );

            input.Setup(config.keys, config.cam);

            model.Apply(config, modelOverride);
            fx.Apply(config);
            sfx.Apply(config);

            camRig?.Apply(config, first);

            cfgFuel = config.jump.jetpackFuel;

            if (first)
            {
                fuel = cfgFuel;
                ResetAirMoves();
            }

            fuel = Mathf.Min(fuel, cfgFuel);

            airJumpsLeft = Mathf.Min(
                airJumpsLeft,
                config.jump.airJumps
            );

            airDashesLeft = Mathf.Min(
                airDashesLeft,
                config.dash.airDashes
            );

            if (config.extra.grapple == GrappleMode.Off &&
                State == MoveState.Grapple)
            {
                EndGrapple();
            }
        }

        // Inspector validation
        void OnValidate()
        {
            if (Application.isPlaying)
            {
                appliedJson = null;
            }
            else
            {
                previewDirty = true;

#if UNITY_EDITOR
                if (modelOverride != null &&
                    (extraClips == null || extraClips.Length == 0))
                {
                    var path =
                        UnityEditor.AssetDatabase.GetAssetPath(modelOverride);

                    if (!string.IsNullOrEmpty(path))
                    {
                        var list =
                            new System.Collections.Generic.List<AnimationClip>();

                        foreach (var o in
                            UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                        {
                            if (o is AnimationClip c &&
                                !c.name.StartsWith("__preview__"))
                            {
                                list.Add(c);
                            }
                        }

                        if (list.Count > 0)
                            extraClips = list.ToArray();
                    }
                }
#endif
            }
        }

        // Update
        void Update()
        {
            if (!Application.isPlaying)
            {
                EditorPreview();
                return;
            }

            if (!started)
                return;

            if (appliedJson == null)
                ApplyConfig(false);

            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);

            if (dt <= 0f)
                return;

            input.Update();

            camRig?.HandleCursor();

            Motor(dt);

            model.Tick(dt);
            fx.Tick(dt);
            sfx.Tick(dt);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !started)
                return;

            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);

            camRig?.Tick(dt);
            model.LateTick(dt);
        }

        // Public player helpers
        public void Launch(Vector3 velocity)
        {
            if (State == MoveState.Grapple)
                EndGrapple();

            if (State != MoveState.Normal)
                State = MoveState.Normal;

            hv = new Vector3(velocity.x, 0, velocity.z) +
                (Mathf.Abs(velocity.x) + Mathf.Abs(velocity.z) < 0.01f
                    ? hv
                    : Vector3.zero);

            vy = velocity.y;

            IsGrounded = false;

            launchedT = 0.25f;
            jumpCutAllowed = false;
            coyoteT = 0;

            ResetAirMoves();

            sfx?.Play("bounce");

            fx?.Puff(
                transform.position,
                CLEffects.PuffKind.Ring
            );
        }

        public void Teleport(Vector3 position)
        {
            if (cc == null)
                cc = GetComponent<CharacterController>();

            bool was = cc.enabled;

            cc.enabled = false;

            transform.position = position;

            cc.enabled = was;

            hv = Vector3.zero;
            vy = 0;

            groundTransform = null;

            if (State == MoveState.Grapple)
                EndGrapple();

            State = MoveState.Normal;

            camRig?.Snap();
            fx?.ResetTrail();
        }

        public void Teleport(Vector3 position, float yaw)
        {
            facingYaw = yaw;

            transform.rotation = Quaternion.Euler(0, yaw, 0);

            if (camRig != null)
                camRig.Yaw = yaw;

            Teleport(position);
        }

        public void Respawn()
        {
            Teleport(SpawnPoint);

            ResetAirMoves();

            fuel = cfgFuel;

            fx?.Puff(
                SpawnPoint,
                CLEffects.PuffKind.Sparkles
            );

            sfx?.Play("respawn");

            Respawned?.Invoke();
        }

        void ResetAirMoves()
        {
            airJumpsLeft = config.jump.airJumps;
            airDashesLeft = config.dash.airDashes;
        }

        // Editor preview
        const string PreviewName = "__CharacterLab Preview";

        bool previewDirty = true;

        string previewKey;

        GameObject previewOverride;

        void EditorPreview()
        {
#if UNITY_EDITOR
            if (cc == null)
                cc = GetComponent<CharacterController>();

            if (!gameObject.scene.IsValid())
                return;

            var mo = config.model;

            float h = Mathf.Max(0.2f, mo.height);

            if (!Mathf.Approximately(cc.height, h) ||
                !Mathf.Approximately(
                    cc.radius,
                    Mathf.Min(mo.radius, h * 0.5f)))
            {
                cc.height = h;

                cc.radius = Mathf.Min(
                    mo.radius,
                    h * 0.5f
                );

                cc.center = new Vector3(0, h * 0.5f, 0);
            }

            string key =
                mo.model + "|" +
                mo.height + "|" +
                mo.modelScale + "|" +
                (int)mo.modelYaw + "|" +
                mo.modelOffsetY + "|" +
                mo.tint + mo.tintStrength + "|" +
                mo.parts.Count;

            if (!previewDirty &&
                key == previewKey &&
                previewOverride == modelOverride &&
                transform.Find(PreviewName) != null)
            {
                return;
            }

            previewDirty = false;
            previewKey = key;
            previewOverride = modelOverride;

            RemovePreview();

            var holder = new GameObject(PreviewName);

            holder.hideFlags =
                HideFlags.DontSave | HideFlags.NotEditable;

            holder.transform.SetParent(transform, false);

            var src = CLModel.FindModelPrefab(
                mo.model,
                modelOverride
            );

            if (src == null)
                return;

            var inst = Instantiate(src, holder.transform);

            foreach (var t in
                inst.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags =
                    HideFlags.DontSave | HideFlags.NotEditable;
            }

            CLModel.Fit(inst.transform, holder.transform, config);

            CLModel.ApplyColors(inst, config, true);

            foreach (var a in inst.GetComponentsInChildren<Animator>())
                a.enabled = false;
#endif
        }

        void RemovePreview()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);

                if (c.name == PreviewName)
                {
                    if (Application.isPlaying)
                        Destroy(c.gameObject);
                    else
                        DestroyImmediate(c.gameObject);
                }
            }
        }

        // Crosshair
        void OnGUI()
        {
            if (!Application.isPlaying ||
                !started ||
                camRig == null ||
                !ShowCrosshair)
            {
                return;
            }

            var v = config.cam.view;

            bool aimed =
                config.extra.grapple != GrappleMode.Off ||
                config.dash.direction == DashDirection.CameraForward;

            if (!aimed ||
                v == CameraView.TopDown ||
                v == CameraView.SideView)
            {
                return;
            }

            Rect r = new Rect(
                Screen.width * 0.5f - 12,
                Screen.height * 0.5f - 12,
                24,
                24
            );

            if (crossTex == null)
            {
                crossTex = new Texture2D(24, 24)
                {
                    filterMode = FilterMode.Bilinear
                };

                for (int y = 0; y < 24; y++)
                {
                    for (int x = 0; x < 24; x++)
                    {
                        float dx = x - 11.5f;
                        float dy = y - 11.5f;

                        float d = Mathf.Sqrt(dx * dx + dy * dy);

                        float ring =
                            Mathf.Clamp01(1.5f - Mathf.Abs(d - 8f));

                        float dot = Mathf.Clamp01(2.2f - d);

                        float a = Mathf.Max(ring, dot);

                        crossTex.SetPixel(
                            x,
                            y,
                            new Color(1, 1, 1, a * 0.9f)
                        );
                    }
                }

                crossTex.Apply();
            }

            GUI.color = new Color(0, 0, 0, 0.5f);

            GUI.DrawTexture(
                new Rect(r.x + 1, r.y + 1, r.width, r.height),
                crossTex
            );

            GUI.color = Color.white;

            GUI.DrawTexture(r, crossTex);
        }

        Texture2D crossTex;

        public static bool ShowCrosshair = true;

        // Gizmos
        void OnDrawGizmosSelected()
        {
            var mo = config.model;

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);

            Vector3 p = transform.position;

            Gizmos.DrawWireSphere(
                p + Vector3.up * mo.radius,
                mo.radius
            );

            Gizmos.DrawWireSphere(
                p + Vector3.up * (mo.height - mo.radius),
                mo.radius
            );

            Gizmos.DrawLine(
                p + transform.forward * mo.radius +
                Vector3.up * mo.height * 0.5f,

                p + transform.forward * (mo.radius + 0.6f) +
                Vector3.up * mo.height * 0.5f
            );
        }
    }
}
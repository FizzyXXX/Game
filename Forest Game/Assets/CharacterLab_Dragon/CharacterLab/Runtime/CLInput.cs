using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CharacterLab
{
    /// <summary>
    /// Reads the player's controls. Uses Unity's NEW Input System (actions are
    /// built in code from the key settings, so there's no .inputactions asset to
    /// set up). If a project only has the old Input Manager turned on, it falls
    /// back to that automatically.
    /// </summary>
    public class CLInput
    {
        public Vector2 Move;          // WASD / arrows / left stick, -1..1
        public Vector2 LookDelta;     // mouse movement this frame (pixels)
        public Vector2 LookStick;     // right stick / look keys, -1..1
        public float Zoom;            // scroll wheel this frame (+ = zoom in)
        public bool JumpDown, JumpHeld, JumpUp;
        public bool SprintDown, SprintHeld;
        public bool CrouchDown, CrouchHeld;
        public bool DashDown;
        public bool GrappleDown, GrappleHeld, GrappleUp;
        public bool ActionDown;
        public bool RightMouseHeld, LeftMouseDown, EscapeDown;
        /// <summary>When false (e.g. a menu is open) every value reads as zero.</summary>
        public bool Enabled = true;
        /// <summary>When false only the mouse is ignored (look, scroll, clicks and keys bound to mouse
        /// buttons) — e.g. while the pointer is over a menu — and the keyboard keeps working.</summary>
        public bool MouseEnabled = true;
        /// <summary>Called after the devices are read each frame — handy for AI/replays/tests that press buttons from code.</summary>
        public System.Action<CLInput> PostUpdate;

        KeySettings keys;
        CameraSettings cam;

#if ENABLE_INPUT_SYSTEM
        InputAction move, lookStick, jump, sprint, crouch, dash, grapple, action;
        readonly System.Collections.Generic.List<InputAction> all = new System.Collections.Generic.List<InputAction>();
#endif

        public void Setup(KeySettings k, CameraSettings c)
        {
            keys = k;
            cam = c;
#if ENABLE_INPUT_SYSTEM
            Dispose();
            bool arrowsLook = c.lookControl == LookControl.ArrowKeys;
            move = Add(new InputAction("Move", InputActionType.Value));
            if (k.moveKeys != MoveKeys.ArrowsOnly)
                move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            if (k.moveKeys != MoveKeys.WasdOnly && !arrowsLook)
                move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            if (k.gamepad) move.AddBinding("<Gamepad>/leftStick");

            lookStick = Add(new InputAction("LookStick", InputActionType.Value));
            if (arrowsLook)
                lookStick.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            if (k.gamepad) lookStick.AddBinding("<Gamepad>/rightStick");

            jump = Button("Jump", k.jump, "<Gamepad>/buttonSouth");
            sprint = Button("Sprint", k.sprint, "<Gamepad>/leftShoulder", "<Gamepad>/leftStickPress");
            crouch = Button("Crouch", k.crouch, "<Gamepad>/buttonEast");
            dash = Button("Dash", k.dash, "<Gamepad>/buttonWest");
            grapple = Button("Grapple", k.grapple, "<Gamepad>/rightShoulder");
            action = Button("Action", k.action, "<Gamepad>/buttonNorth");
            foreach (var a in all) a.Enable();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        InputAction Add(InputAction a)
        {
            all.Add(a);
            return a;
        }

        InputAction Button(string name, string key, params string[] pad)
        {
            var a = Add(new InputAction(name, InputActionType.Button));
            if (!string.IsNullOrEmpty(key)) a.AddBinding(key);
            if (keys.gamepad) foreach (var p in pad) a.AddBinding(p);
            return a;
        }
#endif

        public void Dispose()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (var a in all) { a.Disable(); a.Dispose(); }
            all.Clear();
#endif
        }

        public void Update()
        {
            if (keys == null) return;
#if ENABLE_INPUT_SYSTEM
            Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            LookStick = lookStick.ReadValue<Vector2>();
            var mouse = Mouse.current;
            LookDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            // The new Input System reports raw pixels; scale to feel like the old Input Manager's mouse axes.
            LookDelta *= 0.1f;
            Zoom = mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f;
            RightMouseHeld = mouse != null && mouse.rightButton.isPressed;
            LeftMouseDown = mouse != null && mouse.leftButton.wasPressedThisFrame;
            EscapeDown = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            JumpDown = jump.WasPressedThisFrame(); JumpHeld = jump.IsPressed(); JumpUp = jump.WasReleasedThisFrame();
            SprintDown = sprint.WasPressedThisFrame(); SprintHeld = sprint.IsPressed();
            CrouchDown = crouch.WasPressedThisFrame(); CrouchHeld = crouch.IsPressed();
            DashDown = dash.WasPressedThisFrame();
            GrappleDown = grapple.WasPressedThisFrame(); GrappleHeld = grapple.IsPressed(); GrappleUp = grapple.WasReleasedThisFrame();
            ActionDown = action.WasPressedThisFrame();
#else
            LegacyUpdate();
#endif
            if (!Enabled)
            {
                Move = LookDelta = LookStick = Vector2.zero;
                Zoom = 0;
                JumpDown = JumpHeld = JumpUp = SprintDown = SprintHeld = CrouchDown = CrouchHeld = false;
                DashDown = GrappleDown = GrappleHeld = GrappleUp = ActionDown = RightMouseHeld = LeftMouseDown = false;
            }
            else if (!MouseEnabled)
            {
                LookDelta = Vector2.zero;
                Zoom = 0;
                RightMouseHeld = LeftMouseDown = false;
                if (IsMouse(keys.jump)) JumpDown = JumpHeld = JumpUp = false;
                if (IsMouse(keys.sprint)) SprintDown = SprintHeld = false;
                if (IsMouse(keys.crouch)) CrouchDown = CrouchHeld = false;
                if (IsMouse(keys.dash)) DashDown = false;
                if (IsMouse(keys.grapple)) GrappleDown = GrappleHeld = GrappleUp = false;
                if (IsMouse(keys.action)) ActionDown = false;
            }
            PostUpdate?.Invoke(this);
        }

        static bool IsMouse(string path) => path != null && path.StartsWith("<Mouse>");

#if !ENABLE_INPUT_SYSTEM
        // ---- Fallback for projects that only use the old Input Manager ----
        static KeyCode Code(string path)
        {
            if (string.IsNullOrEmpty(path)) return KeyCode.None;
            switch (path)
            {
                case "<Keyboard>/space": return KeyCode.Space;
                case "<Keyboard>/leftShift": return KeyCode.LeftShift;
                case "<Keyboard>/leftCtrl": return KeyCode.LeftControl;
                case "<Keyboard>/leftAlt": return KeyCode.LeftAlt;
                case "<Keyboard>/tab": return KeyCode.Tab;
                case "<Mouse>/leftButton": return KeyCode.Mouse0;
                case "<Mouse>/rightButton": return KeyCode.Mouse1;
                case "<Mouse>/middleButton": return KeyCode.Mouse2;
            }
            string k = path.Substring(path.LastIndexOf('/') + 1);
            if (k.Length == 1 && char.IsDigit(k[0])) return KeyCode.Alpha0 + (k[0] - '0');
            try { return (KeyCode)System.Enum.Parse(typeof(KeyCode), k, true); }
            catch { return KeyCode.None; }
        }

        void LegacyUpdate()
        {
            float x = 0, y = 0;
            bool arrowsLook = cam.lookControl == LookControl.ArrowKeys;
            bool wasd = keys.moveKeys != MoveKeys.ArrowsOnly, arrows = keys.moveKeys != MoveKeys.WasdOnly && !arrowsLook;
            if ((wasd && Input.GetKey(KeyCode.D)) || (arrows && Input.GetKey(KeyCode.RightArrow))) x += 1;
            if ((wasd && Input.GetKey(KeyCode.A)) || (arrows && Input.GetKey(KeyCode.LeftArrow))) x -= 1;
            if ((wasd && Input.GetKey(KeyCode.W)) || (arrows && Input.GetKey(KeyCode.UpArrow))) y += 1;
            if ((wasd && Input.GetKey(KeyCode.S)) || (arrows && Input.GetKey(KeyCode.DownArrow))) y -= 1;
            Move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            LookStick = Vector2.zero;
            if (arrowsLook)
                LookStick = new Vector2((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                                        (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
            LookDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            Zoom = Input.mouseScrollDelta.y;
            RightMouseHeld = Input.GetMouseButton(1);
            LeftMouseDown = Input.GetMouseButtonDown(0);
            EscapeDown = Input.GetKeyDown(KeyCode.Escape);
            KeyCode j = Code(keys.jump), s = Code(keys.sprint), c = Code(keys.crouch), d = Code(keys.dash), g = Code(keys.grapple), a = Code(keys.action);
            JumpDown = Input.GetKeyDown(j); JumpHeld = Input.GetKey(j); JumpUp = Input.GetKeyUp(j);
            SprintDown = Input.GetKeyDown(s); SprintHeld = Input.GetKey(s);
            CrouchDown = Input.GetKeyDown(c); CrouchHeld = Input.GetKey(c);
            DashDown = Input.GetKeyDown(d);
            GrappleDown = Input.GetKeyDown(g); GrappleHeld = Input.GetKey(g); GrappleUp = Input.GetKeyUp(g);
            ActionDown = Input.GetKeyDown(a);
        }
#endif
    }
}

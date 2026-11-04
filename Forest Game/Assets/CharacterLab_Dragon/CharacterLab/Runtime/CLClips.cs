using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CharacterLab
{
    /// <summary>
    /// Plays a model's own animation clips without needing an Animator Controller:
    /// it finds clips by name (idle, walk, run, jump, fall, roll, fly, attack,
    /// wave, dance...) and blends between them from code with the Playables API.
    /// Old-style "legacy" clips (from some imported/runtime-loaded models) are
    /// played with an Animation component instead.
    /// </summary>
    public class CLClips
    {
        // slots
        const int Idle = 0, Walk = 1, Run = 2, Jump = 3, Fall = 4, Roll = 5, Fly = 6, Action = 7, Count = 8;
        static readonly string[] SlotNames = { "idle", "walk", "run", "jump", "fall", "roll", "fly", "action" };

        readonly AnimationClip[] slot = new AnimationClip[Count];
        readonly List<AnimationClip> all = new List<AnimationClip>();
        Animator animator;
        Animation legacy;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] plays = new AnimationClipPlayable[Count];
        readonly float[] weight = new float[Count];
        readonly float[] time = new float[Count];
        int current = Idle;
        float oneShotT = 99f;
        int oneShot = -1;
        bool use = true;
        bool built;
        ActionAnimation actionKind = (ActionAnimation)(-1);

        public bool HasClips => all.Count > 0;
        public bool Active => use && HasClips;
        public bool Has(string name)
        {
            int i = System.Array.IndexOf(SlotNames, name);
            return Active && i >= 0 && slot[i] != null;
        }

        public static CLClips Create(GameObject inst, string key, GameObject overrideModel, CharacterLabPlayer player)
        {
            var c = new CLClips();
            c.Collect(inst, key, overrideModel, player);
            if (!c.HasClips) return c;
            c.Build(inst);
            return c;
        }

        void Collect(GameObject inst, string key, GameObject overrideModel, CharacterLabPlayer player)
        {
            legacy = inst.GetComponentInChildren<Animation>();
            if (legacy != null)
                foreach (AnimationState s in legacy) if (s.clip != null && !all.Contains(s.clip)) all.Add(s.clip);
            if (player.extraClips != null)
                foreach (var cl in player.extraClips) if (cl != null && !all.Contains(cl)) all.Add(cl);
            if (all.Count == 0 && overrideModel == null && !string.IsNullOrEmpty(key))
                foreach (var cl in Resources.LoadAll<AnimationClip>("CharacterLab/Models/" + key))
                    if (!cl.name.StartsWith("__preview__") && !all.Contains(cl)) all.Add(cl);
            if (CharacterLabPlayer.ClipProvider != null && all.Count == 0)
            {
                var more = CharacterLabPlayer.ClipProvider(key);
                if (more != null) foreach (var cl in more) if (cl != null && !all.Contains(cl)) all.Add(cl);
            }
            slot[Idle] = Best(new[] { "idle" }, new[] { "attack", "weapon", "hit" });
            slot[Walk] = Best(new[] { "walk" }, new[] { "jump" });
            slot[Run] = Best(new[] { "run", "sprint" }, new[] { "weapon", "holding" });
            slot[Jump] = Best(new[] { "jump" }, new[] { "walkjump" }) ?? Best(new[] { "jump" }, null);
            slot[Fall] = Best(new[] { "fall" }, null);
            slot[Roll] = Best(new[] { "roll", "slide", "dash" }, null);
            slot[Fly] = Best(new[] { "fly", "flying", "hover", "swim" }, null);
            if (slot[Idle] == null) slot[Idle] = slot[Fly] ?? slot[Walk] ?? (all.Count > 0 ? all[0] : null);
            if (slot[Walk] == null) slot[Walk] = slot[Run] ?? slot[Fly];
            if (slot[Run] == null) slot[Run] = slot[Walk];
        }

        static string Short(AnimationClip c)
        {
            string n = c.name;
            int i = n.LastIndexOf('|');
            return (i >= 0 ? n.Substring(i + 1) : n).ToLowerInvariant();
        }

        AnimationClip Best(string[] want, string[] avoid)
        {
            AnimationClip best = null;
            int bestScore = int.MinValue;
            foreach (var c in all)
            {
                string n = Short(c);
                foreach (var w in want)
                {
                    if (!n.Contains(w)) continue;
                    int score = 100 - (n.Length - w.Length);   // "Idle" beats "Idle_Weapon"
                    if (avoid != null) foreach (var a in avoid) if (n.Contains(a)) score -= 60;
                    if (score > bestScore) { bestScore = score; best = c; }
                }
            }
            return best;
        }

        void PickAction(ActionAnimation kind)
        {
            actionKind = kind;
            AnimationClip a = null;
            switch (kind)
            {
                case ActionAnimation.Wave: a = Best(new[] { "wave", "yes", "thumbs" }, null); break;
                case ActionAnimation.Dance: a = Best(new[] { "dance" }, null) ?? Best(new[] { "wave" }, null); break;
            }
            if (a == null) a = Best(new[] { "attack", "punch", "spell", "shoot", "slash", "sword", "dagger", "staff" }, new[] { "idle" });
            if (slot[Action] != a)
            {
                slot[Action] = a;
                if (built && !legacyMode) Rebuild();
            }
        }

        bool legacyMode;

        void Build(GameObject inst)
        {
            // Legacy clips can only be played by an Animation component.
            legacyMode = all.Exists(c => c.legacy);
            if (legacyMode)
            {
                if (legacy == null) legacy = inst.AddComponent<Animation>();
                foreach (var c in all)
                    if (c.legacy && legacy.GetClip(c.name) == null) legacy.AddClip(c, c.name);
                var anim = inst.GetComponentInChildren<Animator>();
                if (anim != null) anim.enabled = false;
                built = true;
                return;
            }
            animator = inst.GetComponentInChildren<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Rebuild();
            built = true;
        }

        void Rebuild()
        {
            if (graph.IsValid()) graph.Destroy();
            graph = PlayableGraph.Create("CharacterLab");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, Count);
            for (int i = 0; i < Count; i++)
            {
                if (slot[i] == null) continue;
                plays[i] = AnimationClipPlayable.Create(graph, slot[i]);
                plays[i].SetApplyFootIK(false);
                graph.Connect(plays[i], 0, mixer, i);
                mixer.SetInputWeight(i, i == current ? 1f : 0f);
                weight[i] = i == current ? 1f : 0f;
            }
            var output = AnimationPlayableOutput.Create(graph, "Anim", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        /// <summary>Poses the model as the first frame of its idle (or first) clip, right now.</summary>
        public void SampleFirstPose()
        {
            if (!HasClips || !built) return;
            var clip = slot[Idle] ?? all[0];
            if (legacyMode)
            {
                if (legacy == null) return;
                var st = legacy[clip.name];
                if (st == null) return;
                st.enabled = true;
                st.weight = 1f;
                st.time = 0f;
                legacy.Sample();
                st.enabled = false;
            }
            else if (graph.IsValid())
            {
                for (int i = 0; i < Count; i++) if (slot[i] != null) mixer.SetInputWeight(i, i == Idle ? 1f : 0f);
                if (plays[Idle].IsValid()) plays[Idle].SetTime(0);
                graph.Evaluate(0f);
            }
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        public void SetUse(bool on)
        {
            use = on;
            if (!HasClips) return;
            if (legacyMode)
            {
                if (legacy != null) { legacy.enabled = on; if (!on) legacy.Stop(); }
            }
            else if (animator != null)
            {
                animator.enabled = on;
                if (!on && graph.IsValid()) graph.Stop();
                else if (on && graph.IsValid()) graph.Play();
            }
        }

        public void Trigger(string what)
        {
            if (!Active) return;
            if (what == "jump" && slot[Jump] != null) { oneShot = Jump; oneShotT = 0; time[Jump] = 0; if (legacyMode) legacy.Stop(slot[Jump].name); }
            if (what == "action" && slot[Action] != null) { oneShot = Action; oneShotT = 0; time[Action] = 0; if (legacyMode) legacy.Stop(slot[Action].name); }
        }

        public void Tick(float dt, CharacterLabPlayer p, PlayerConfig cfg)
        {
            if (!Active) return;
            if (cfg.extra.action != actionKind) PickAction(cfg.extra.action);
            float speed = p.HorizontalSpeed;
            var st = p.State;
            float h = Mathf.Max(0.5f, cfg.model.height);

            // ---- what should be playing?
            int want;
            bool air = !p.IsGrounded;
            if (st == CharacterLabPlayer.MoveState.Slide || st == CharacterLabPlayer.MoveState.Dash)
                want = slot[Roll] != null && ((st == CharacterLabPlayer.MoveState.Slide && cfg.anim.slidePose == SlidePose.Roll) || (st == CharacterLabPlayer.MoveState.Dash && cfg.anim.dashPose == DashPose.Roll)) ? Roll
                     : st == CharacterLabPlayer.MoveState.Slide ? Idle : Run;
            else if (st == CharacterLabPlayer.MoveState.WallRun) want = Run;
            else if (air)
            {
                if (slot[Fly] != null && slot[Jump] == null) want = Fly;
                else if (p.Velocity.y < -2f && slot[Fall] != null) want = Fall;
                else want = slot[Jump] != null ? Jump : (slot[Fly] != null ? Fly : Idle);
            }
            else if (speed > cfg.move.walkSpeed * 0.8f + 0.5f && slot[Run] != null) want = Run;
            else if (speed > 0.4f) want = Walk;
            else want = Idle;
            if (slot[want] == null) want = Idle;

            oneShotT += dt;
            if (oneShot >= 0)
            {
                float len = slot[oneShot] != null ? slot[oneShot].length : 0f;
                bool done = oneShot == Jump ? !air && oneShotT > 0.1f : oneShotT > len / Mathf.Max(0.1f, cfg.anim.animSpeed) - 0.1f;
                if (oneShot == Action && speed > 2f && oneShotT > 0.25f) done = true;
                if (done) oneShot = -1;
                else want = oneShot;
            }

            // ---- playback speed: match feet to how fast we're going
            float rate = cfg.anim.animSpeed;
            if (want == Walk) rate *= Mathf.Clamp(speed / (1.6f * h), 0.6f, 2.2f);
            else if (want == Run) rate *= Mathf.Clamp(speed / (3.6f * h), 0.7f, 2.2f);
            else if (want == Roll) rate *= 1.4f;

            if (legacyMode) { LegacyTick(want, rate, dt); return; }
            if (!graph.IsValid()) return;
            current = want;
            for (int i = 0; i < Count; i++)
            {
                if (slot[i] == null) continue;
                float target = i == want ? 1f : 0f;
                weight[i] = Mathf.MoveTowards(weight[i], target, dt / 0.15f);
                bool loop = i != Jump && i != Action && i != Fall;
                if (weight[i] > 0.001f || i == want)
                {
                    float len = Mathf.Max(0.01f, slot[i].length);
                    time[i] += dt * (i == want ? rate : cfg.anim.animSpeed);
                    float t = time[i];
                    if (loop) t = Mathf.Repeat(t, len);
                    else if (i == Jump && air && oneShot != Jump) t = len * 0.75f;   // hold the in-air pose
                    else t = Mathf.Min(t, len - 0.001f);
                    plays[i].SetTime(t);
                }
                else time[i] = 0;
                mixer.SetInputWeight(i, weight[i]);
            }
            graph.Evaluate(0f);
        }

        int legacyCurrent = -1;

        void LegacyTick(int want, float rate, float dt)
        {
            if (legacy == null || slot[want] == null) return;
            var clip = slot[want];
            var state = legacy[clip.name];
            if (state == null) return;
            bool loop = want != Jump && want != Action && want != Fall;
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            state.speed = rate;
            if (want != legacyCurrent)
            {
                legacyCurrent = want;
                legacy.CrossFade(clip.name, 0.15f);
                if (!loop) state.time = 0;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CharacterLab
{
    /// <summary>
    /// The player's sound effects. Sounds are loaded from
    /// Resources/CharacterLab/Sounds/&lt;Pack&gt;/ (jump, airjump, land, dash, slide,
    /// walljump, pound, grapple, step, bounce, respawn, action, jetpack).
    /// Replace those .wav files with your own to change a sound.
    /// </summary>
    public class CLAudio
    {
        readonly CharacterLabPlayer player;
        readonly AudioSource oneShots, loop;
        SoundSettings A;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        SoundPack loadedPack = (SoundPack)(-1);
        float stepT;

        public CLAudio(CharacterLabPlayer p)
        {
            player = p;
            oneShots = p.gameObject.AddComponent<AudioSource>();
            oneShots.playOnAwake = false;
            oneShots.spatialBlend = 0f;
            loop = p.gameObject.AddComponent<AudioSource>();
            loop.playOnAwake = false;
            loop.loop = true;
            loop.spatialBlend = 0f;
        }

        public void Apply(PlayerConfig c)
        {
            A = c.audio;
            if (A.pack != loadedPack)
            {
                loadedPack = A.pack;
                clips.Clear();
                foreach (var clip in Resources.LoadAll<AudioClip>("CharacterLab/Sounds/" + A.pack))
                    clips[clip.name.ToLowerInvariant()] = clip;
                if (clips.Count == 0)   // the zip only carries the chosen pack; use whatever is there
                    foreach (var clip in Resources.LoadAll<AudioClip>("CharacterLab/Sounds"))
                        clips[clip.name.ToLowerInvariant()] = clip;
            }
        }

        public void Play(string name, float volume = 1f)
        {
            if (A == null || !A.enabled || A.volume <= 0) return;
            if (!clips.TryGetValue(name, out var clip)) return;
            oneShots.pitch = A.pitch * Random.Range(0.95f, 1.05f);
            oneShots.PlayOneShot(clip, A.volume * volume);
        }

        public void Tick(float dt)
        {
            if (A == null) return;
            // looping jetpack hiss
            bool jet = A.enabled && player.IsJetpacking;
            if (jet && clips.TryGetValue("jetpack", out var j))
            {
                if (loop.clip != j) loop.clip = j;
                loop.volume = A.volume * 0.5f;
                loop.pitch = A.pitch;
                if (!loop.isPlaying) loop.Play();
            }
            else if (loop.isPlaying) loop.Stop();

            // footsteps
            float speed = player.HorizontalSpeed;
            if (A.enabled && A.footsteps && player.IsGrounded && speed > 1f && player.State == CharacterLabPlayer.MoveState.Normal && !player.IsCrouching)
            {
                stepT -= dt * speed;
                if (stepT <= 0)
                {
                    stepT = 1.25f * player.config.model.height;
                    Play("step", 0.35f);
                }
            }
            else stepT = 0.2f;
        }
    }
}

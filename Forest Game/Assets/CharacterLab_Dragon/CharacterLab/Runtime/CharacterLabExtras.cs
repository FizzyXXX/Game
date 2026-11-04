using System.Collections.Generic;
using UnityEngine;

namespace CharacterLab
{
    /// <summary>
    /// A bounce pad: put this on any object with a collider. When the player
    /// touches it they're launched along the object's green (up) arrow.
    /// </summary>
    [AddComponentMenu("Character Lab/Bounce Pad")]
    public class CharacterLabBouncePad : MonoBehaviour
    {
        [Tooltip("How fast the player is launched (metres per second).")]
        public float launchSpeed = 18f;
        [Tooltip("If above 0: launch the player exactly this many metres high, whatever their gravity (Launch Speed is then ignored).")]
        public float launchHeight = 0f;
        [Tooltip("Squash the pad when something bounces on it.")]
        public bool animate = true;
        Vector3 baseScale;
        float t = 1f;

        void Awake() => baseScale = transform.localScale;

        public void Bounced() => t = 0f;

        void Update()
        {
            if (!animate || t >= 1f) return;
            t = Mathf.Min(1f, t + Time.deltaTime * 3f);
            float s = 1f + Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * 0.25f;
            transform.localScale = new Vector3(baseScale.x * (2f - s), baseScale.y * s, baseScale.z * (2f - s));
        }
    }

    /// <summary>
    /// Marks something the grappling hook can grab (needed when the player's
    /// "Grapple onto" setting is "Only grapple points"; also helps aiming).
    /// </summary>
    [AddComponentMenu("Character Lab/Grapple Point")]
    public class CharacterLabGrapplePoint : MonoBehaviour
    {
        public static readonly List<CharacterLabGrapplePoint> All = new List<CharacterLabGrapplePoint>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }

    /// <summary>
    /// Moves an object back and forth between its start and an offset (and can
    /// spin it). The player rides along when standing on it.
    /// </summary>
    [AddComponentMenu("Character Lab/Moving Platform")]
    [DefaultExecutionOrder(-50)]
    public class CharacterLabMovingPlatform : MonoBehaviour
    {
        [Tooltip("How far it moves from where it starts.")]
        public Vector3 offset = new Vector3(0, 0, 6);
        [Tooltip("Seconds for one trip.")]
        public float tripTime = 3f;
        [Tooltip("Spin speed in degrees per second (0 = no spin).")]
        public float spin = 0f;
        Vector3 start;
        float t;

        void Start() => start = transform.position;

        void Update()
        {
            t += Time.deltaTime;
            float k = tripTime <= 0 ? 0 : (1f - Mathf.Cos(t / tripTime * Mathf.PI)) * 0.5f;
            transform.position = start + offset * k;
            if (spin != 0f) transform.Rotate(0, spin * Time.deltaTime, 0, Space.World);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 s = Application.isPlaying ? start : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(s, s + offset);
            Gizmos.DrawWireCube(s + offset, transform.lossyScale);
        }
    }
}

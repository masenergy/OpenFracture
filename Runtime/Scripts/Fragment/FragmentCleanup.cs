using System.Collections;
using UnityEngine;

/// <summary>
/// Despawns one fracture's debris after a delay, shrinking it away first so it does not simply
/// vanish mid-shot.
///
/// The shrink is a scale, not an alpha fade, and that is a deliberate trade rather than a shortcut.
/// Alpha fading means switching each fragment's material to a transparent surface and giving every
/// fragment its own material instance, which costs a draw call each and defeats batching at exactly
/// the moment there is the most debris on screen. It also loses shadows, and transparent sorting on
/// a pile of overlapping rubble tends to look worse than the pop it replaced. Scaling needs none of
/// that, works with any shader the fragments happen to use, and reads as debris settling away.
///
/// The fragments stop simulating before they start shrinking. Shrinking a convex collider that is
/// resting on something makes it sink and jitter as the contact is recomputed each frame, and there
/// is nothing to gain from simulating debris nobody is going to look at again - so this doubles as
/// a way to hand the physics back a dozen bodies per building a moment early.
///
/// Each piece shrinks around its own geometry rather than around its transform. A fragment's pivot
/// is not where its geometry sits - the slicer leaves that offset in the mesh - so scaling the
/// transform alone drags every piece towards its pivot as it shrinks, and a field of debris appears
/// to slide away as it goes. The position is corrected each frame to hold the mesh centre still.
/// </summary>
public class FragmentCleanup : MonoBehaviour
{
    private float lifetime;
    private float fadeDuration;

    /// <summary>
    /// Starts the countdown. Call once, on the fragment root.
    /// </summary>
    /// <param name="lifetime">Total seconds from now until the debris is gone.</param>
    /// <param name="fadeDuration">How much of that is spent shrinking, at the end.</param>
    public void Begin(float lifetime, float fadeDuration)
    {
        this.lifetime = Mathf.Max(0f, lifetime);
        this.fadeDuration = Mathf.Clamp(fadeDuration, 0f, this.lifetime);

        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        float idle = lifetime - fadeDuration;

        if (idle > 0f)
        {
            yield return new WaitForSeconds(idle);
        }

        // Snapshot rather than walking the hierarchy each frame: the set cannot change from here,
        // and the scales have to be remembered because fragments do not all start at one.
        Transform[] fragments = new Transform[transform.childCount];
        Vector3[] scales = new Vector3[transform.childCount];

        // Where the geometry sits relative to its own transform, and where that lands in the
        // root's space right now. Holding the second still is what keeps a shrinking piece put.
        Vector3[] meshCentres = new Vector3[transform.childCount];
        Vector3[] anchors = new Vector3[transform.childCount];

        for (int i = 0; i < transform.childCount; i++)
        {
            fragments[i] = transform.GetChild(i);
            scales[i] = fragments[i].localScale;

            MeshFilter filter = fragments[i].GetComponent<MeshFilter>();
            meshCentres[i] = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds.center
                : Vector3.zero;

            anchors[i] = fragments[i].localPosition
                       + fragments[i].localRotation * Vector3.Scale(scales[i], meshCentres[i]);

            Rigidbody body = fragments[i].GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
            }

            Collider fragmentCollider = fragments[i].GetComponent<Collider>();
            if (fragmentCollider != null)
            {
                fragmentCollider.enabled = false;
            }
        }

        for (float elapsed = 0f; elapsed < fadeDuration; elapsed += Time.deltaTime)
        {
            float remaining = 1f - Mathf.Clamp01(elapsed / fadeDuration);

            for (int i = 0; i < fragments.Length; i++)
            {
                // A fragment can still go early - a scene unload, or something destroying it.
                if (fragments[i] == null)
                {
                    continue;
                }

                Vector3 scale = scales[i] * remaining;

                fragments[i].localScale = scale;

                // Scaling moves the geometry towards the pivot by however much it shrank. Put the
                // transform back by the same amount so the mesh centre does not budge.
                fragments[i].localPosition = anchors[i]
                    - fragments[i].localRotation * Vector3.Scale(scale, meshCentres[i]);
            }

            yield return null;
        }

        Destroy(gameObject);
    }
}

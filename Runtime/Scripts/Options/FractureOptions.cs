using System;
using UnityEngine;

[Serializable]
/// <summary>
/// Options for fracturing a mesh
/// </summary>
public class FractureOptions
{
    [Range(1, 1024)]
    [Tooltip("Maximum number of times an object and its children are recursively fractured. Larger fragment counts will result in longer computation times.")]
    public int fragmentCount;

    [Tooltip("Enables fracturing in the local X plane")]
    public bool xAxis;

    [Tooltip("Enables fracturing in the local Y plane")]
    public bool yAxis;

    [Tooltip("Enables fracturing in the local  Z plane")]
    public bool zAxis;

    [Tooltip("Enables detection of \"floating\" fragments when fracturing non-convex meshes. This setting has no effect for convex meshes and should be disabled.")]
    public bool detectFloatingFragments;

    [Tooltip("Fracturing is performed asynchronously on the main thread.")]
    public bool asynchronous;

    [Tooltip("The material to use for the inside faces")]
    public Material insideMaterial;
    
    [Tooltip("Scale factor to apply to texture coordinates")]
    public Vector2 textureScale;

    [Tooltip("Offset to apply to texture coordinates")]
    public Vector2 textureOffset;

    [Tooltip("Seconds before the fragments of one fracture are despawned. Zero keeps them forever. " +
             "Debris is cheap to make and expensive to keep - every fragment is a renderer, a " +
             "collider and a rigidbody that goes on costing after it has stopped being interesting.")]
    public float fragmentLifetime;

    [Tooltip("How much of the lifetime is spent shrinking the fragments away at the end, so debris " +
             "does not vanish mid-shot. Zero removes them instantly. Ignored if Fragment Lifetime " +
             "is zero.")]
    public float fragmentFadeDuration;

    public FractureOptions()
    {
        this.fragmentCount = 10;
        this.xAxis = true;
        this.yAxis = true;
        this.zAxis = true;
        this.detectFloatingFragments = false;
        this.asynchronous = false;
        this.insideMaterial = null;
        this.textureScale = Vector2.one;
        this.textureOffset = Vector2.zero;

        // Zero, so existing users keep the behaviour they have.
        this.fragmentLifetime = 0f;

        // Only meaningful once a lifetime is set, so a second of grace is a kinder default than a
        // pop for anyone who turns despawning on.
        this.fragmentFadeDuration = 1f;
    }
}
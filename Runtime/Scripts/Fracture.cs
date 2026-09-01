using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Rigidbody))]
public class Fracture : MonoBehaviour
{
    public TriggerOptions triggerOptions;
    public FractureOptions fractureOptions;
    public RefractureOptions refractureOptions;
    public CallbackOptions callbackOptions;

    /// <summary>
    /// The number of times this fragment has been re-fractured.
    /// </summary>
    [HideInInspector]
    public int currentRefractureCount = 0;

    /// <summary>
    /// Collector object that stores the produced fragments
    /// </summary>
    private GameObject fragmentRoot;

    /// <summary>
    /// True once a physics trigger has queued a fracture that has not run yet. Several contacts
    /// can land on one object in a single step - an explosion touching three of its colliders, or
    /// a projectile and its blast - and without this each would queue its own fracture of an object
    /// that is about to be deactivated anyway.
    /// </summary>
    private bool fractureQueued = false;

    [ContextMenu("Print Mesh Info")]
    public void PrintMeshInfo()
    {
        var mesh = this.GetComponent<MeshFilter>().mesh;
        Debug.Log("Positions");

        var positions = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;

        for (int i = 0; i < positions.Length; i++)
        {
            Debug.Log($"Vertex {i}");
            Debug.Log($"POS | X: {positions[i].x} Y: {positions[i].y} Z: {positions[i].z}");
            Debug.Log($"NRM | X: {normals[i].x} Y: {normals[i].y} Z: {normals[i].z} LEN: {normals[i].magnitude}");
            Debug.Log($"UV  | U: {uvs[i].x} V: {uvs[i].y}");
            Debug.Log("");
        }
    }

    public void CauseFracture()
    {
        callbackOptions.CallOnFracture(null, gameObject, transform.position);
        this.ComputeFracture();
    }

    void OnValidate()
    {
        if (this.transform.parent != null)
        {
            // When an object is fractured, the fragments are created as children of that object's parent.
            // Because of this, they inherit the parent transform. If the parent transform is not scaled
            // the same in all axes, the fragments will not be rendered correctly.
            var scale = this.transform.parent.localScale;
            if ((scale.x != scale.y) || (scale.x != scale.z) || (scale.y != scale.z))
            {
                Debug.LogWarning($"Warning: Parent transform of fractured object must be uniformly scaled in all axes or fragments will not render correctly.", this.transform);
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (triggerOptions.triggerType == TriggerType.Collision)
        {
            if (collision.contactCount > 0)
            {
                // Collision force must exceed the minimum force (F = I / T)
                var contact = collision.contacts[0];
                float collisionForce = collision.impulse.magnitude / Time.fixedDeltaTime;

                // Colliding object tag must be in the set of allowed collision tags if filtering by tag is enabled
                bool tagAllowed = triggerOptions.IsTagAllowed(contact.otherCollider.gameObject.tag);

                // Object is unfrozen if the colliding object has the correct tag (if tag filtering is enabled)
                // and the collision force exceeds the minimum collision force.
                if (collisionForce > triggerOptions.minimumCollisionForce &&
                   (triggerOptions.filterCollisionsByTag && tagAllowed))
                {
                    QueueFracture(contact.otherCollider, contact.point);
                }
            }
        }
    }

    void OnTriggerEnter(Collider collider)
    {
        if (triggerOptions.triggerType == TriggerType.Trigger)
        {
            // Colliding object tag must be in the set of allowed collision tags if filtering by tag is enabled
            bool tagAllowed = triggerOptions.IsTagAllowed(collider.gameObject.tag);

            if (triggerOptions.filterCollisionsByTag && tagAllowed)
            {
                QueueFracture(collider, transform.position);
            }
        }
    }

    void Update()
    {
        if (triggerOptions.triggerType == TriggerType.Keyboard)
        {
            if (Input.GetKeyDown(triggerOptions.triggerKey))
            {
                callbackOptions.CallOnFracture(null, gameObject, transform.position);
                this.ComputeFracture();
            }
        }
    }

    /// <summary>
    /// Defers a physics-triggered fracture to the next frame.
    ///
    /// OnCollisionEnter and OnTriggerEnter are dispatched from inside PhysX's contact callback, and
    /// a fracture is not a small thing to do from there: it builds meshes, spawns a GameObject per
    /// fragment with a rigidbody and a freshly cooked convex collider on each, and deactivates the
    /// source object. That rebuilds a chunk of the physics scene while the solver is walking it.
    ///
    /// One object usually survives it. A blast that catches a dozen at once does not - and the
    /// symptom is a native crash with no managed stack, typically preceded by PhysX complaining
    /// that it could not cook a hull from one of the fragments. Waiting a frame costs nothing
    /// visually and moves all of it onto a scene the solver has finished with.
    /// </summary>
    private void QueueFracture(Collider otherCollider, Vector3 point)
    {
        if (fractureQueued)
        {
            return;
        }

        fractureQueued = true;

        StartCoroutine(FractureNextFrame(otherCollider, point));
    }

    private IEnumerator FractureNextFrame(Collider otherCollider, Vector3 point)
    {
        yield return null;

        // The object can be gone by now - destroyed by whatever hit it, or by a scene change.
        if (this == null)
        {
            yield break;
        }

        callbackOptions.CallOnFracture(otherCollider, gameObject, point);
        this.ComputeFracture();
    }

    /// <summary>
    /// Compute the fracture and create the fragments
    /// </summary>
    /// <returns></returns>
    private void ComputeFracture()
    {
        var mesh = this.GetComponent<MeshFilter>().sharedMesh;

        if (mesh != null)
        {
            // If the fragment root object has not yet been created, create it now
            if (this.fragmentRoot == null)
            {
                // Create a game object to contain the fragments
                this.fragmentRoot = new GameObject($"{this.name}Fragments");
                this.fragmentRoot.transform.SetParent(this.transform.parent);

                // Each fragment will handle its own scale
                this.fragmentRoot.transform.position = this.transform.position;
                this.fragmentRoot.transform.rotation = this.transform.rotation;
                this.fragmentRoot.transform.localScale = Vector3.one;
            }

            var fragmentTemplate = CreateFragmentTemplate();

            if (fractureOptions.asynchronous)
            {
                StartCoroutine(Fragmenter.FractureAsync(
                    this.gameObject,
                    this.fractureOptions,
                    fragmentTemplate,
                    this.fragmentRoot.transform,
                    () =>
                    {
                        // Done with template, destroy it
                        GameObject.Destroy(fragmentTemplate);

                        // Deactivate the original object
                        this.gameObject.SetActive(false);

                        // Fire the completion callback
                        if ((this.currentRefractureCount == 0) ||
                            (this.currentRefractureCount > 0 && this.refractureOptions.invokeCallbacks))
                        {
                            if (callbackOptions.onCompleted != null)
                            {
                                callbackOptions.onCompleted.Invoke();
                            }
                        }
                    }
                ));
            }
            else
            {
                Fragmenter.Fracture(this.gameObject,
                                    this.fractureOptions,
                                    fragmentTemplate,
                                    this.fragmentRoot.transform);

                // Done with template, destroy it
                GameObject.Destroy(fragmentTemplate);

                // Deactivate the original object
                this.gameObject.SetActive(false);

                // Fire the completion callback
                if ((this.currentRefractureCount == 0) ||
                    (this.currentRefractureCount > 0 && this.refractureOptions.invokeCallbacks))
                {
                    if (callbackOptions.onCompleted != null)
                    {
                        callbackOptions.onCompleted.Invoke();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a template object which each fragment will derive from
    /// </summary>
    /// <param name="preFracture">True if this object is being pre-fractured. This will freeze all of the fragments.</param>
    /// <returns></returns>
    private GameObject CreateFragmentTemplate()
    {
        // If pre-fracturing, make the fragments children of this object so they can easily be unfrozen later.
        // Otherwise, parent to this object's parent
        GameObject obj = new GameObject();
        obj.name = "Fragment";
        obj.tag = this.tag;

        // Update mesh to the new sliced mesh
        obj.AddComponent<MeshFilter>();

        // Add materials. Normal material goes in slot 1, cut material in slot 2
        var meshRenderer = obj.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = new Material[2] {
            this.GetComponent<MeshRenderer>().sharedMaterial,
            this.fractureOptions.insideMaterial
        };

        // Copy collider properties to fragment
        var thisCollider = this.GetComponent<Collider>();
        var fragmentCollider = obj.AddComponent<MeshCollider>();
        fragmentCollider.convex = true;
        fragmentCollider.sharedMaterial = thisCollider.sharedMaterial;
        fragmentCollider.isTrigger = thisCollider.isTrigger;

        // Copy rigid body properties to fragment
        var thisRigidBody = this.GetComponent<Rigidbody>();
        var fragmentRigidBody = obj.AddComponent<Rigidbody>();
        fragmentRigidBody.velocity = thisRigidBody.velocity;
        fragmentRigidBody.angularVelocity = thisRigidBody.angularVelocity;
        fragmentRigidBody.drag = thisRigidBody.drag;
        fragmentRigidBody.angularDrag = thisRigidBody.angularDrag;
        fragmentRigidBody.useGravity = thisRigidBody.useGravity;

        // If refracturing is enabled, create a copy of this component and add it to the template fragment object
        if (refractureOptions.enableRefracturing &&
           (this.currentRefractureCount < refractureOptions.maxRefractureCount))
        {
            CopyFractureComponent(obj);
        }

        return obj;
    }

    /// <summary>
    /// Convenience method for copying this component to another component
    /// </summary>
    /// <param name="obj">The GameObject to copy the component to</param>
    private void CopyFractureComponent(GameObject obj)
    {
        var fractureComponent = obj.AddComponent<Fracture>();

        fractureComponent.triggerOptions = this.triggerOptions;
        fractureComponent.fractureOptions = this.fractureOptions;
        fractureComponent.refractureOptions = this.refractureOptions;
        fractureComponent.callbackOptions = this.callbackOptions;
        fractureComponent.currentRefractureCount = this.currentRefractureCount + 1;
        fractureComponent.fragmentRoot = this.fragmentRoot;
    }
}

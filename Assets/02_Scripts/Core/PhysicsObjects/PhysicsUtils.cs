using UnityEngine;

/// <summary>
/// Helper methods shared by physics-driven gameplay systems.
/// </summary>
public static class PhysicsUtils
{
    private static readonly System.Collections.Generic.Dictionary<int, Collider[]> CachedHierarchyColliders
    = new System.Collections.Generic.Dictionary<int, Collider[]>();

    /// <summary>
    /// Returns cached colliders for a hierarchy root and rebuilds the cache when requested or when entries became invalid.
    /// </summary>
    public static Collider[] GetCachedHierarchyColliders(GameObject RootObject, bool IncludeInactive = true, bool ForceRefresh = false)
    {
        if (RootObject == null)
        {
            return System.Array.Empty<Collider>();
        }

        int RootInstanceId = RootObject.GetInstanceID();

        if (!ForceRefresh && CachedHierarchyColliders.TryGetValue(RootInstanceId, out Collider[] ExistingColliders))
        {
            bool IsValid = true;

            for (int ColliderIndex = 0; ColliderIndex < ExistingColliders.Length; ColliderIndex++)
            {
                if (ExistingColliders[ColliderIndex] == null)
                {
                    IsValid = false;
                    break;
                }
            }

            if (IsValid)
            {
                return ExistingColliders;
            }
        }

        Collider[] FreshColliders = GetHierarchyColliders(RootObject, IncludeInactive);
        CachedHierarchyColliders[RootInstanceId] = FreshColliders;
        return FreshColliders;
    }

    /// <summary>
    /// Returns every physics body collider that should be considered part of a character or actor hierarchy.
    /// This includes standard colliders and CharacterController components converted to the Collider base type.
    /// </summary>
    public static Collider[] GetHierarchyColliders(GameObject RootObject, bool IncludeInactive = true)
    {
        if (RootObject == null)
        {
            return System.Array.Empty<Collider>();
        }

        System.Collections.Generic.List<Collider> Result = new System.Collections.Generic.List<Collider>();
        System.Collections.Generic.HashSet<Collider> UniqueColliders = new System.Collections.Generic.HashSet<Collider>();

        Collider[] StandardColliders = RootObject.GetComponentsInChildren<Collider>(IncludeInactive);
        for (int ColliderIndex = 0; ColliderIndex < StandardColliders.Length; ColliderIndex++)
        {
            Collider CurrentCollider = StandardColliders[ColliderIndex];
            if (CurrentCollider == null || !UniqueColliders.Add(CurrentCollider))
            {
                continue;
            }

            Result.Add(CurrentCollider);
        }

        CharacterController[] CharacterControllers = RootObject.GetComponentsInChildren<CharacterController>(IncludeInactive);
        for (int ControllerIndex = 0; ControllerIndex < CharacterControllers.Length; ControllerIndex++)
        {
            CharacterController CurrentController = CharacterControllers[ControllerIndex];
            if (CurrentController == null)
            {
                continue;
            }

            Collider ControllerCollider = CurrentController;
            if (!UniqueColliders.Add(ControllerCollider))
            {
                continue;
            }

            Result.Add(ControllerCollider);
        }

        return Result.ToArray();
    }

    /// <summary>
    /// Applies a simple explosion impulse to every rigidbody inside the given radius.
    /// </summary>
    public static void SimpleExplosion(Vector3 Position, float Radius, float Force, float UpwardsModifier = 0f)
    {
        Collider[] Hits = Physics.OverlapSphere(Position, Radius);
        for (int HitIndex = 0; HitIndex < Hits.Length; HitIndex++)
        {
            Rigidbody AttachedRigidbody = Hits[HitIndex].attachedRigidbody;
            if (AttachedRigidbody == null)
            {
                continue;
            }

            AttachedRigidbody.AddExplosionForce(Force, Position, Radius, UpwardsModifier, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Ignores or restores collisions between the full collider hierarchies of both objects.
    /// </summary>
    public static void IgnoreAllCollisions(GameObject ObjectA, GameObject ObjectB, bool Ignore)
    {
        if (ObjectA == null || ObjectB == null)
        {
            return;
        }

        Collider[] ObjectAColliders = GetHierarchyColliders(ObjectA, true);
        Collider[] ObjectBColliders = GetHierarchyColliders(ObjectB, true);
        IgnoreAllCollisions(ObjectAColliders, ObjectBColliders, Ignore);
    }

    /// <summary>
    /// Ignores or restores collisions between two collider arrays.
    /// </summary>
    public static void IgnoreAllCollisions(Collider[] ObjectAColliders, Collider[] ObjectBColliders, bool Ignore)
    {
        if (ObjectAColliders == null || ObjectBColliders == null)
        {
            return;
        }

        for (int ColliderAIndex = 0; ColliderAIndex < ObjectAColliders.Length; ColliderAIndex++)
        {
            Collider ColliderA = ObjectAColliders[ColliderAIndex];
            if (ColliderA == null)
            {
                continue;
            }

            for (int ColliderBIndex = 0; ColliderBIndex < ObjectBColliders.Length; ColliderBIndex++)
            {
                Collider ColliderB = ObjectBColliders[ColliderBIndex];
                if (ColliderB == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(ColliderA, ColliderB, Ignore);
            }
        }
    }

    /// <summary>
    /// Applies a layer recursively to an entire hierarchy.
    /// </summary>
    public static void SetLayerRecursively(GameObject TargetObject, int Layer)
    {
        if (TargetObject == null)
        {
            return;
        }

        TargetObject.layer = Layer;
        for (int ChildIndex = 0; ChildIndex < TargetObject.transform.childCount; ChildIndex++)
        {
            SetLayerRecursively(TargetObject.transform.GetChild(ChildIndex).gameObject, Layer);
        }
    }
}

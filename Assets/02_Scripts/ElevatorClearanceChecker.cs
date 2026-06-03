using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ElevatorClearanceChecker : MonoBehaviour
{
    [Header("Ejecución")]
    public bool runInEditor = true;
    public bool runInPlayMode = true;

    [Header("Modo de funcionamiento")]
    public bool showRadius = true;
    public bool changeMaterial = true;

    [Header("Dimensiones del ascensor")]
    [Min(0.01f)]
    public float elevatorDiameter = 4f;

    [Min(0.01f)]
    public float descentDepth = 20f;

    [Tooltip("Offset local desde el objeto que tiene este script. Normalmente déjalo en cero.")]
    public Vector3 localStartOffset = Vector3.zero;

    [Tooltip("El ascensor bajará siguiendo el -Y local del objeto.")]
    public bool useLocalDownDirection = true;

    [Header("Detección")]
    public LayerMask blockingLayers = ~0;
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Tooltip("Si está activo, ignora colliders hijos del objeto que tiene este script.")]
    public bool ignoreSelfHierarchy = true;

    [Tooltip("Objeto raíz a ignorar. Si está vacío, se usa este transform.")]
    public Transform ignoreRoot;

    [Tooltip("Filtro extra para reducir falsos positivos del volumen cilíndrico.")]
    public bool useApproximateCylinderFilter = true;

    [Range(3, 32)]
    public int cylinderFilterSamples = 12;

    [Min(8)]
    public int maxPhysicsHits = 256;

    [Header("Cambio de material")]
    public Material occupiedMaterial;

    [Tooltip("Busca Renderers en los hijos del collider detectado.")]
    public bool affectChildRenderers = true;

    [Tooltip("Busca Renderers en los padres del collider detectado. Útil si el collider está en un hijo y el mesh en el padre.")]
    public bool affectParentRenderers = false;

    [Tooltip("Si está activo, reemplaza todos los slots de material del Renderer.")]
    public bool replaceAllMaterialSlots = true;

    [Tooltip("Si replaceAllMaterialSlots está desactivado, este será el slot reemplazado.")]
    public int materialSlot = 0;

    [Header("Gizmos")]
    public bool drawOnlyWhenSelected = false;
    public Color gizmoColorClear = new Color(0f, 1f, 0.4f, 0.35f);
    public Color gizmoColorOccupied = new Color(1f, 0f, 0f, 0.45f);
    public int gizmoCircleSegments = 48;
    public int gizmoDepthRings = 4;

    private Collider[] hits;
    private readonly HashSet<Renderer> currentRenderers = new HashSet<Renderer>();
    private readonly HashSet<Renderer> nextRenderers = new HashSet<Renderer>();
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

    private bool hasOccupation;

    private float Radius => elevatorDiameter * 0.5f;

    private void OnEnable()
    {
        EnsureBuffer();
    }

    private void OnDisable()
    {
        RestoreAllMaterials();
    }

    private void OnDestroy()
    {
        RestoreAllMaterials();
    }

    private void OnValidate()
    {
        elevatorDiameter = Mathf.Max(0.01f, elevatorDiameter);
        descentDepth = Mathf.Max(0.01f, descentDepth);
        maxPhysicsHits = Mathf.Max(8, maxPhysicsHits);
        cylinderFilterSamples = Mathf.Clamp(cylinderFilterSamples, 3, 32);
        gizmoCircleSegments = Mathf.Max(8, gizmoCircleSegments);
        gizmoDepthRings = Mathf.Max(1, gizmoDepthRings);
        materialSlot = Mathf.Max(0, materialSlot);

        EnsureBuffer();

        if (!ShouldRunNow() || !changeMaterial)
        {
            RestoreAllMaterials();
        }
    }

    private void Update()
    {
        if (!ShouldRunNow())
        {
            RestoreAllMaterials();
            hasOccupation = false;
            return;
        }

        if (!changeMaterial)
        {
            RestoreAllMaterials();
            hasOccupation = false;
            return;
        }

        ScanAndApplyMaterials();
    }

    private bool ShouldRunNow()
    {
        return Application.isPlaying ? runInPlayMode : runInEditor;
    }

    private void EnsureBuffer()
    {
        if (hits == null || hits.Length != maxPhysicsHits)
        {
            hits = new Collider[maxPhysicsHits];
        }
    }

    private Vector3 GetStartPoint()
    {
        return transform.TransformPoint(localStartOffset);
    }

    private Vector3 GetDownDirection()
    {
        if (useLocalDownDirection)
            return -transform.up;

        return Vector3.down;
    }

    private Quaternion GetVolumeRotation()
    {
        if (useLocalDownDirection)
            return transform.rotation;

        return Quaternion.identity;
    }

    private void ScanAndApplyMaterials()
    {
        if (occupiedMaterial == null)
        {
            RestoreAllMaterials();
            hasOccupation = false;
            return;
        }

        EnsureBuffer();

        nextRenderers.Clear();

        Vector3 start = GetStartPoint();
        Vector3 direction = GetDownDirection().normalized;
        Vector3 end = start + direction * descentDepth;
        float radius = Radius;

        int count = Physics.OverlapCapsuleNonAlloc(
            start,
            end,
            radius,
            hits,
            blockingLayers,
            triggerInteraction
        );

        hasOccupation = false;

        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i];

            if (col == null)
                continue;

            if (IsIgnored(col))
                continue;

            if (useApproximateCylinderFilter)
            {
                if (!ColliderIntersectsCylinderApprox(col, start, direction, descentDepth, radius))
                    continue;
            }

            hasOccupation = true;
            CollectRenderers(col, nextRenderers);
        }

        RestoreRenderersThatLeftArea();
        ApplyMaterialToNewRenderers();
    }

    private bool IsIgnored(Collider col)
    {
        if (!ignoreSelfHierarchy)
            return false;

        Transform root = ignoreRoot != null ? ignoreRoot : transform;

        return col.transform == root || col.transform.IsChildOf(root);
    }

    private bool ColliderIntersectsCylinderApprox(
        Collider col,
        Vector3 start,
        Vector3 direction,
        float depth,
        float radius)
    {
        float radiusSqr = radius * radius;

        for (int i = 0; i <= cylinderFilterSamples; i++)
        {
            float t = i / (float)cylinderFilterSamples;
            Vector3 axisPoint = start + direction * depth * t;

            Vector3 closest = col.ClosestPoint(axisPoint);
            Vector3 fromStart = closest - start;

            float heightAlongAxis = Vector3.Dot(fromStart, direction);

            if (heightAlongAxis < 0f || heightAlongAxis > depth)
                continue;

            Vector3 radialVector = fromStart - direction * heightAlongAxis;

            if (radialVector.sqrMagnitude <= radiusSqr)
                return true;
        }

        return false;
    }

    private void CollectRenderers(Collider col, HashSet<Renderer> result)
    {
        if (affectChildRenderers)
        {
            Renderer[] childRenderers = col.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < childRenderers.Length; i++)
            {
                if (childRenderers[i] != null)
                    result.Add(childRenderers[i]);
            }
        }
        else
        {
            Renderer r = col.GetComponent<Renderer>();

            if (r != null)
                result.Add(r);
        }

        if (affectParentRenderers)
        {
            Renderer[] parentRenderers = col.GetComponentsInParent<Renderer>(true);

            for (int i = 0; i < parentRenderers.Length; i++)
            {
                if (parentRenderers[i] != null)
                    result.Add(parentRenderers[i]);
            }
        }
    }

    private void RestoreRenderersThatLeftArea()
    {
        List<Renderer> toRestore = new List<Renderer>();

        foreach (Renderer r in currentRenderers)
        {
            if (r == null || !nextRenderers.Contains(r))
                toRestore.Add(r);
        }

        for (int i = 0; i < toRestore.Count; i++)
        {
            RestoreRenderer(toRestore[i]);
        }
    }

    private void ApplyMaterialToNewRenderers()
    {
        foreach (Renderer r in nextRenderers)
        {
            if (r == null)
                continue;

            if (!currentRenderers.Contains(r))
            {
                if (!originalMaterials.ContainsKey(r))
                    originalMaterials.Add(r, r.sharedMaterials);

                ApplyOccupiedMaterial(r);
                currentRenderers.Add(r);
            }
        }
    }

    private void ApplyOccupiedMaterial(Renderer r)
    {
        Material[] current = r.sharedMaterials;

        if (current == null || current.Length == 0)
            return;

        Material[] newMaterials = new Material[current.Length];

        for (int i = 0; i < newMaterials.Length; i++)
        {
            if (replaceAllMaterialSlots)
            {
                newMaterials[i] = occupiedMaterial;
            }
            else
            {
                newMaterials[i] = i == materialSlot ? occupiedMaterial : current[i];
            }
        }

        r.sharedMaterials = newMaterials;
    }

    private void RestoreRenderer(Renderer r)
    {
        if (r != null && originalMaterials.TryGetValue(r, out Material[] mats))
        {
            r.sharedMaterials = mats;
        }

        currentRenderers.Remove(r);

        if (r != null)
            originalMaterials.Remove(r);
    }

    [ContextMenu("Restaurar materiales marcados")]
    public void RestoreAllMaterials()
    {
        List<Renderer> renderers = new List<Renderer>(currentRenderers);

        for (int i = 0; i < renderers.Count; i++)
        {
            RestoreRenderer(renderers[i]);
        }

        currentRenderers.Clear();
        nextRenderers.Clear();
        originalMaterials.Clear();
    }

    private void OnDrawGizmos()
    {
        if (drawOnlyWhenSelected)
            return;

        DrawClearanceGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawOnlyWhenSelected)
            return;

        DrawClearanceGizmo();
    }

    private void DrawClearanceGizmo()
    {
        if (!showRadius)
            return;

        if (!ShouldRunNow())
            return;

        Vector3 start = GetStartPoint();
        Vector3 direction = GetDownDirection().normalized;
        Vector3 end = start + direction * descentDepth;
        float radius = Radius;

        Gizmos.color = hasOccupation ? gizmoColorOccupied : gizmoColorClear;

        DrawCylinderGizmo(start, end, direction, radius);
    }

    private void DrawCylinderGizmo(Vector3 start, Vector3 end, Vector3 direction, float radius)
    {
        Vector3 right;
        Vector3 forward;

        if (useLocalDownDirection)
        {
            right = transform.right;
            forward = transform.forward;
        }
        else
        {
            right = Vector3.right;
            forward = Vector3.forward;
        }

        DrawCircle(start, right, forward, radius);
        DrawCircle(end, right, forward, radius);

        for (int i = 1; i < gizmoDepthRings; i++)
        {
            float t = i / (float)gizmoDepthRings;
            Vector3 ringCenter = Vector3.Lerp(start, end, t);
            DrawCircle(ringCenter, right, forward, radius);
        }

        DrawVerticalLine(start, end, right * radius);
        DrawVerticalLine(start, end, -right * radius);
        DrawVerticalLine(start, end, forward * radius);
        DrawVerticalLine(start, end, -forward * radius);
    }

    private void DrawCircle(Vector3 center, Vector3 right, Vector3 forward, float radius)
    {
        Vector3 previousPoint = center + right * radius;

        for (int i = 1; i <= gizmoCircleSegments; i++)
        {
            float angle = i / (float)gizmoCircleSegments * Mathf.PI * 2f;

            Vector3 nextPoint =
                center +
                right * Mathf.Cos(angle) * radius +
                forward * Mathf.Sin(angle) * radius;

            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }

    private void DrawVerticalLine(Vector3 start, Vector3 end, Vector3 offset)
    {
        Gizmos.DrawLine(start + offset, end + offset);
    }
}
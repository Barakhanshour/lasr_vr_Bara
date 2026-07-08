using System;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class PaintEmitterStage02 : MonoBehaviour
{
    [Header("STAGE 02 - References")]
    public SwingingBucketStage01 bucketStage01;

    [Tooltip("Where the paint squirts out from the bucket bottom.")]
    public Transform paintExitPoint;

    [Tooltip("Legacy garbage: Don't use this with the new GPU solver.")]
    public Transform paintFillVisual;

    [Header("STAGE 02 - Paint Amount")]
    [Tooltip("Keep this false for GPU fluid, otherwise it overwrites paint volume with bucket mass on awake.")]
    public bool initializeFromBucketPaintMass = false;

    [Tooltip("Syncs the physics weight back to the Stage01 bucket based on current liters.")]
    public bool updateBucketPaintMass = true;

    [Tooltip("Paint thickness/density (kg per liter).")]
    [Min(0.1f)]
    public float paintDensityKgPerLiter = 1.15f;

    [Tooltip("Current juice in liters. The GPU solver reads this directly.")]
    [Min(0.0f)]
    public float paintVolumeLiters = 5.0f;

    [Header("STAGE 02 - Bucket Frustum Shape")]
    [Tooltip("Inside radius of the bucket floor (meters).")]
    [Min(0.001f)]
    public float bucketBottomRadiusMeters = 0.055f;

    [Tooltip("Inside radius of the bucket rim (meters).")]
    [Min(0.001f)]
    public float bucketTopRadiusMeters = 0.145f;

    [Tooltip("Inside height of the bucket (meters).")]
    [Min(0.01f)]
    public float bucketHeightMeters = 0.32f;

    [Tooltip("Tiny offset above the hole so particles don't clip through the bottom.")]
    public float bottomOffsetAboveExitMeters = 0.006f;

    [Header("STAGE 02 - Hole Settings")]
    [Tooltip("Hole size in mm. Set to 0 to plug the leak completely.")]
    [Min(0.0f)]
    public float holeDiameterMillimeters = 0.0f;

    [Tooltip("Squirt direction in local space relative to the exit point.")]
    public Vector3 localExitDirection = Vector3.down;

    [Tooltip("Goo factor (0 to 1). Higher means thicker, slower flow.")]
    [Range(0.0f, 1.0f)]
    public float viscosity01 = 0.45f;

    [Tooltip("Paint color.")]
    public Color paintColor = Color.red;

    [Header("STAGE 02 - Legacy Visual / Emission")]
    [Tooltip("Legacy: Keep this false when using the GPU fluid.")]
    public bool updatePaintFillVisual = false;

    [HideInInspector] public float fillBottomLocalY = 0.0f;
    [HideInInspector] public float fillVisualThickness = 0.008f;
    [HideInInspector] public float fillRadiusScale = 0.92f;

    [Tooltip("Legacy: The GPU system ignores this entirely.")]
    public bool emissionEnabled = false;

    [HideInInspector] public float dropletRadiusMeters = 0.012f;
    [HideInInspector] public float spreadAngleDegrees = 3.0f;
    [HideInInspector] public int maxParticlesPerFrame = 80;

    [HideInInspector] public float bucketInnerRadiusMeters = 0.145f;
    [HideInInspector] public float maxPaintHeightMeters = 0.32f;

    private const float DISCHARGE_COEFFICIENT = 0.62f;
    private const float LITERS_TO_CUBIC_METERS = 0.001f;
    private const float CUBIC_METERS_TO_LITERS = 1000.0f;
    private const float MILLIMETERS_TO_METERS = 0.001f;

    public float CurrentPaintHeightMeters { get; private set; }
    public float CurrentFlowLitersPerSecond { get; private set; }
    public float CurrentExitSpeedMetersPerSecond { get; private set; }
    public float BucketCapacityLiters { get; private set; }
    public int TotalEmittedParticles { get; private set; }

    public bool HasPaint => paintVolumeLiters > 0.0001f;

    public bool IsHoleOpen =>
        holeDiameterMillimeters > 0.00001f &&
        HasPaint;

    public float HoleRadiusMeters =>
        Mathf.Max(0.0f, holeDiameterMillimeters * MILLIMETERS_TO_METERS * 0.5f);

    public struct PaintParticleEmitData
    {
        public Vector3 position;
        public Vector3 velocity;
        public Color color;
        public float radiusMeters;
        public float volumeCubicMeters;
    }

    public event Action<PaintParticleEmitData> OnPaintParticleEmitted;

    private bool initialized;

    private void OnEnable()
    {
        Stage02_UpdateAllDerivedValues();
        Stage02_DisableLegacyFillVisualIfNeeded();
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            Stage02_Initialize();
        }
    }

    private void Update()
    {
        Stage02_UpdateAllDerivedValues();

        if (Application.isPlaying)
        {
            if (!initialized)
                Stage02_Initialize();

            Stage02_UpdateBucketPaintMass();
        }

        Stage02_DisableLegacyFillVisualIfNeeded();
    }

    private void OnValidate()
    {
        paintDensityKgPerLiter = Mathf.Max(0.1f, paintDensityKgPerLiter);
        paintVolumeLiters = Mathf.Max(0.0f, paintVolumeLiters);

        bucketBottomRadiusMeters = Mathf.Max(0.001f, bucketBottomRadiusMeters);
        bucketTopRadiusMeters = Mathf.Max(bucketBottomRadiusMeters + 0.001f, bucketTopRadiusMeters);
        bucketHeightMeters = Mathf.Max(0.01f, bucketHeightMeters);

        holeDiameterMillimeters = Mathf.Max(0.0f, holeDiameterMillimeters);
        viscosity01 = Mathf.Clamp01(viscosity01);

        if (localExitDirection.sqrMagnitude < 0.000001f)
        {
            localExitDirection = Vector3.down;
        }

        bucketInnerRadiusMeters = bucketTopRadiusMeters;
        maxPaintHeightMeters = bucketHeightMeters;

        dropletRadiusMeters = Mathf.Max(0.001f, dropletRadiusMeters);
        maxParticlesPerFrame = Mathf.Clamp(maxParticlesPerFrame, 1, 300);
        fillVisualThickness = Mathf.Max(0.001f, fillVisualThickness);
        fillRadiusScale = Mathf.Clamp(fillRadiusScale, 0.5f, 1.0f);

        Stage02_UpdateAllDerivedValues();
        Stage02_DisableLegacyFillVisualIfNeeded();
    }

    [ContextMenu("STAGE 02 / Initialize")]
    public void Stage02_Initialize()
    {
        if (bucketStage01 != null && initializeFromBucketPaintMass)
        {
            paintVolumeLiters =
                bucketStage01.paintMassKg / Mathf.Max(0.1f, paintDensityKgPerLiter);
        }

        TotalEmittedParticles = 0;

        Stage02_UpdateAllDerivedValues();
        Stage02_UpdateBucketPaintMass();
        Stage02_DisableLegacyFillVisualIfNeeded();

        initialized = true;
    }

    public void Stage02_SetPaintVolumeLiters(float liters)
    {
        paintVolumeLiters = Mathf.Max(0.0f, liters);
        Stage02_UpdateAllDerivedValues();
        Stage02_UpdateBucketPaintMass();
    }

    public void Stage02_ConsumePaintVolumeLiters(float liters)
    {
        if (liters <= 0.0f)
            return;

        paintVolumeLiters = Mathf.Max(0.0f, paintVolumeLiters - liters);

        Stage02_UpdateAllDerivedValues();
        Stage02_UpdateBucketPaintMass();
    }

    public float Stage02_GetBottomLocalY()
    {
        if (paintExitPoint == null)
            return bottomOffsetAboveExitMeters;

        Transform parent = paintExitPoint.parent;

        if (parent == null)
            return bottomOffsetAboveExitMeters;

        Vector3 local = parent.InverseTransformPoint(paintExitPoint.position);
        return local.y + bottomOffsetAboveExitMeters;
    }

    public float Stage02_GetRadiusAtHeight(float heightFromBottom)
    {
        float t = Mathf.Clamp01(heightFromBottom / Mathf.Max(0.0001f, bucketHeightMeters));

        return Mathf.Lerp(
            bucketBottomRadiusMeters,
            bucketTopRadiusMeters,
            t
        );
    }

    public float Stage02_ComputeHeightFromVolumeLiters(float liters)
    {
        float targetVolumeCubicMeters =
            Mathf.Max(0.0f, liters) * LITERS_TO_CUBIC_METERS;

        float lo = 0.0f;
        float hi = bucketHeightMeters;

        for (int i = 0; i < 24; i++)
        {
            float mid = (lo + hi) * 0.5f;

            float currentVolume =
                Stage02_ComputeFrustumVolumeCubicMeters(mid);

            if (currentVolume < targetVolumeCubicMeters)
                lo = mid;
            else
                hi = mid;
        }

        return Mathf.Clamp(
            (lo + hi) * 0.5f,
            0.0f,
            bucketHeightMeters
        );
    }

    public float Stage02_ComputeCurrentFlowLitersPerSecond()
    {
        Stage02_UpdateAllDerivedValues();
        return CurrentFlowLitersPerSecond;
    }

    private void Stage02_UpdateAllDerivedValues()
    {
        BucketCapacityLiters =
            Stage02_ComputeFrustumVolumeCubicMeters(bucketHeightMeters) *
            CUBIC_METERS_TO_LITERS;

        CurrentPaintHeightMeters =
            Stage02_ComputeHeightFromVolumeLiters(paintVolumeLiters);

        Stage02_UpdateFlowEstimate();
    }

    private float Stage02_ComputeFrustumVolumeCubicMeters(float height)
    {
        height = Mathf.Clamp(height, 0.0f, bucketHeightMeters);

        if (height <= 0.0f)
            return 0.0f;

        float t =
            height /
            Mathf.Max(0.0001f, bucketHeightMeters);

        float r0 = bucketBottomRadiusMeters;

        float r1 =
            Mathf.Lerp(
                bucketBottomRadiusMeters,
                bucketTopRadiusMeters,
                t
            );

        return
            Mathf.PI *
            height *
            (r0 * r0 + r0 * r1 + r1 * r1) /
            3.0f;
    }

    private void Stage02_UpdateFlowEstimate()
    {
        if (!HasPaint || !IsHoleOpen || CurrentPaintHeightMeters <= 0.0001f)
        {
            CurrentFlowLitersPerSecond = 0.0f;
            CurrentExitSpeedMetersPerSecond = 0.0f;
            return;
        }

        float holeRadiusMeters =
            holeDiameterMillimeters * MILLIMETERS_TO_METERS * 0.5f;

        float holeArea =
            Mathf.PI * holeRadiusMeters * holeRadiusMeters;

        float gravity =
            bucketStage01 != null ? Mathf.Abs(bucketStage01.gravity) : 9.81f;

        float idealExitSpeed =
            Mathf.Sqrt(2.0f * gravity * CurrentPaintHeightMeters);

        float viscosityFactor =
            Mathf.Lerp(1.0f, 0.10f, viscosity01);

        CurrentExitSpeedMetersPerSecond =
            idealExitSpeed * viscosityFactor;

        float flowCubicMetersPerSecond =
            DISCHARGE_COEFFICIENT *
            holeArea *
            idealExitSpeed *
            viscosityFactor;

        CurrentFlowLitersPerSecond =
            Mathf.Max(0.0f, flowCubicMetersPerSecond * CUBIC_METERS_TO_LITERS);
    }

    private void Stage02_DisableLegacyFillVisualIfNeeded()
    {
        if (paintFillVisual == null)
            return;

        if (!updatePaintFillVisual)
        {
            if (paintFillVisual.gameObject.activeSelf)
                paintFillVisual.gameObject.SetActive(false);

            return;
        }

        bool hasVisiblePaint =
            paintVolumeLiters > 0.0001f &&
            CurrentPaintHeightMeters > 0.0001f;

        paintFillVisual.gameObject.SetActive(hasVisiblePaint);

        if (!hasVisiblePaint)
            return;

        Vector3 localPosition = paintFillVisual.localPosition;
        localPosition.y = fillBottomLocalY + CurrentPaintHeightMeters;
        paintFillVisual.localPosition = localPosition;

        float visualDiameter =
            bucketTopRadiusMeters *
            2.0f *
            fillRadiusScale;

        paintFillVisual.localScale = new Vector3(
            visualDiameter,
            fillVisualThickness,
            visualDiameter
        );

        Renderer renderer = paintFillVisual.GetComponent<Renderer>();

        if (renderer != null && renderer.sharedMaterial != null)
        {
            renderer.sharedMaterial.color = paintColor;
        }
    }

    private void Stage02_UpdateBucketPaintMass()
    {
        if (bucketStage01 == null)
            return;

        if (!updateBucketPaintMass)
            return;

        bucketStage01.paintMassKg =
            paintVolumeLiters * paintDensityKgPerLiter;
    }

    public PaintParticleEmitData Stage02_CreateLegacyEmitData(float volumeCubicMeters)
    {
        Vector3 exitDirection = Vector3.down;

        if (paintExitPoint != null)
        {
            exitDirection =
                paintExitPoint.TransformDirection(localExitDirection.normalized);
        }

        Vector3 bucketVelocity =
            bucketStage01 != null ? bucketStage01.BucketVelocity : Vector3.zero;

        Vector3 position =
            paintExitPoint != null ? paintExitPoint.position : transform.position;

        return new PaintParticleEmitData
        {
            position = position,
            velocity = bucketVelocity + exitDirection * CurrentExitSpeedMetersPerSecond,
            color = paintColor,
            radiusMeters = dropletRadiusMeters,
            volumeCubicMeters = volumeCubicMeters
        };
    }

    private void OnDrawGizmos()
    {
        if (paintExitPoint == null)
            return;

        Gizmos.color = paintColor;
        Gizmos.DrawSphere(paintExitPoint.position, 0.025f);

        Vector3 direction =
            paintExitPoint.TransformDirection(localExitDirection.normalized);

        Gizmos.DrawLine(
            paintExitPoint.position,
            paintExitPoint.position + direction * 0.25f
        );
    }
}
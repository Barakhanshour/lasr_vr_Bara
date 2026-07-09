using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class GpuDensityPaintFluidStage03 : MonoBehaviour
{
    public enum RuntimePaintColorMode
    {
        NewPaintOnly,
        RecolorEverything
    }

    public enum PaintCanvasSurfaceType
    {
        Cloth,
        Wood,
        Glass,
        Custom
    }

    [Header("References")]
    public SwingingBucketStage01 bucketStage01;
    public PaintEmitterStage02 paintEmitterStage02;
    public Transform bucketRoot;
    public Transform paintExitPoint;

    [Tooltip("Point the local Y-axis dead straight from the canvas towards the bucket.")]
    public Transform paintCanvasSurface;

    [Header("GPU Assets")]
    public ComputeShader fluidCompute;
    public Material particleSplatMaterial;

    [Header("Stage 04 Canvas Painting")]
    [Tooltip("Hook up the Stage04 canvas script here to handle the actual 2D splatters.")]
    public PaintCanvasStage04 paintCanvasStage04;

    [Tooltip("Hard cap for paint hits we can slam into the canvas each frame.")]
    [Range(1024, 262144)]
    public int maximumPaintImpactsPerFrame = 65536;

    [Header("Particle Controls")]
    public bool simulateFluid = true;
    public bool allowDrainFromHole = true;

    [Range(50000, 2000000)]
    public int particleCount = 250000;

    [Range(32, 128)]
    public int gridResolution = 96;

    [Range(0.001f, 0.012f)]
    public float particleVisualRadius = 0.0020f;

    [Range(1, 4)]
    public int solverSubsteps = 2;

    [Header("Fluid Solver")]
    [Range(0.0f, 1.0f)]
    public float pressureStrength = 0.22f;

    [Range(0.0f, 1.0f)]
    public float wallFriction = 0.86f;

    [Range(0.0f, 60.0f)]
    public float shapeSupportStrength = 26.0f;

    [Range(0.0f, 30.0f)]
    public float drainSuctionStrength = 10.0f;

    [Range(0.0f, 20.0f)]
    public float vortexStrength = 1.5f;

    [Range(0.01f, 0.25f)]
    public float drainInfluenceRadius = 0.065f;

    [Range(0.01f, 0.30f)]
    public float drainInfluenceHeight = 0.085f;

    [Range(1.0f, 3.0f)]
    public float streamRenderRadiusScale = 1.25f;

    [Header("Paint Canvas")]
    public Vector2 canvasSizeMeters = new Vector2(2.0f, 2.0f);

    [Range(0.0001f, 0.01f)]
    public float canvasSurfaceOffsetMeters = 0.0012f;

    public PaintCanvasSurfaceType canvasSurfaceType = PaintCanvasSurfaceType.Cloth;

    [Header("Custom Canvas Properties")]
    [Range(0.0f, 1.0f)]
    public float customCanvasAdhesion = 0.96f;

    [Range(0.0f, 2.0f)]
    public float customCanvasFlowMobility = 0.025f;

    [Range(0.0f, 5.0f)]
    public float customImpactSpread = 0.80f;

    [Range(0.0f, 1.0f)]
    public float customTangentialDamping = 0.55f;

    [Range(1.0f, 8.0f)]
    public float depositedRadiusScale = 1.90f;

    [Range(0.0f, 1.0f)]
    public float edgeReleaseSpeed = 0.04f;

    [Header("Runtime Paint Color")]
    public RuntimePaintColorMode runtimeColorMode = RuntimePaintColorMode.NewPaintOnly;

    [Header("Volume Synchronization")]
    [Range(0.05f, 0.5f)]
    public float statisticsReadbackInterval = 0.10f;

    [Header("Runtime Debug")]
    [SerializeField] private int insideBucketParticleCount;
    [SerializeField] private int airborneParticleCount;
    [SerializeField] private int depositedParticleCount;
    [SerializeField] private int inactiveParticleCount;
    [SerializeField] private float calculatedPaintVolumeLiters;
    [SerializeField] private float volumePerParticleLiters;
    [SerializeField] private float currentFlowLitersPerSecond;
    [SerializeField] private int currentDrainParticleBudget;

    private const int THREADS = 256;
    private const float LITERS_TO_CUBIC_METERS = 0.001f;
    private const float VOLUME_EDIT_THRESHOLD_LITERS = 0.02f;
    private const float FLOAT_CHANGE_THRESHOLD = 0.0001f;
    private const float MAX_BUCKET_ACCELERATION = 25.0f;


    public int ActiveParticleCount => activeParticleCount;

    public float CalculatedPaintVolumeLiters =>
        calculatedPaintVolumeLiters;

    public float CurrentFlowLitersPerSecond =>
        currentFlowLitersPerSecond;
    public int InsideBucketParticleCount =>
    insideBucketParticleCount;

    public int AirborneParticleCount =>
        airborneParticleCount;

    public int DepositedParticleCount =>
        depositedParticleCount;

    public int InactiveParticleCount =>
        inactiveParticleCount;

    public float VolumePerParticleLiters =>
        volumePerParticleLiters;

    public int CurrentDrainParticleBudget =>
        currentDrainParticleBudget;


    private struct GpuPaintParticle
    {
        public Vector4 positionState;
        public Vector4 velocityRadius;
        public Vector4 colorSeed;
    }

    private ComputeBuffer particleBuffer;
    private ComputeBuffer restPositionBuffer;
    private ComputeBuffer gridCountBuffer;
    private ComputeBuffer statisticsBuffer;
    private ComputeBuffer drainCounterBuffer;
    private ComputeBuffer drawArgsBuffer;
    private ComputeBuffer paintImpactBuffer;
    private ComputeBuffer paintImpactCountBuffer;

    private int kernelClearGrid;
    private int kernelSplatDensity;
    private int kernelSimulate;
    private int kernelClearStats;
    private int kernelCountStats;
    private int kernelApplyRuntimeColor;
    private int kernelClearDrainCounter;
    private int kernelClearPaintImpactCount;

    private bool initialized;
    private bool statisticsRequestPending;
    private bool warnedAboutReadback;

    private int activeParticleCount;
    private int bufferGeneration;

    private float initialPaintVolumeLiters;
    private float initialPaintHeightMeters;
    private float expectedSourceVolumeLiters;
    private float nextStatisticsReadTime;

    private double drainParticleFraction;
    private Vector3 previousBucketVelocity;
    private Color lastObservedPaintColor;

    private int lastBuiltParticleCount;
    private int lastBuiltGridResolution;
    private float lastBuiltParticleRadius;
    private float lastBuiltBottomRadius;
    private float lastBuiltTopRadius;
    private float lastBuiltBucketHeight;
    private float lastBuiltBottomOffset;
    private int lastBuiltMaximumPaintImpacts;

    private void Start()
    {
        if (simulateFluid)
        {
            InitializeFluid();
        }
    }

    private void LateUpdate()
    {
        if (!initialized)
            return;

        RebuildIfConfigurationChanged();

        if (!initialized)
            return;

        UpdateRuntimePaintColor();

        if (simulateFluid && activeParticleCount > 0)
        {
            DispatchFluidSimulation();
            TryUpdateParticleStatistics();
        }

        RenderParticles();
    }

    private void OnValidate()
    {
        particleCount = Mathf.Clamp(particleCount, 50000, 2000000);
        gridResolution = Mathf.Clamp(gridResolution, 32, 128);
        particleVisualRadius = Mathf.Clamp(particleVisualRadius, 0.001f, 0.012f);
        solverSubsteps = Mathf.Clamp(solverSubsteps, 1, 4);
        canvasSizeMeters.x = Mathf.Max(0.01f, canvasSizeMeters.x);
        canvasSizeMeters.y = Mathf.Max(0.01f, canvasSizeMeters.y);
        statisticsReadbackInterval = Mathf.Clamp(statisticsReadbackInterval, 0.05f, 0.5f);
        maximumPaintImpactsPerFrame = Mathf.Clamp(
            maximumPaintImpactsPerFrame,
            1024,
            262144
        );
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    [ContextMenu("GPU PAINT / Initialize From Stage 02")]
    public void InitializeFluid()
    {
        if (!HasValidReferences())
            return;

        ReleaseBuffers();
        RemoveOldGeneratedTube();

        FindKernels();
        CreateBuffers();

        initialPaintVolumeLiters = Mathf.Max(0.0f, paintEmitterStage02.paintVolumeLiters);
        drainParticleFraction = 0.0;
        currentDrainParticleBudget = 0;
        currentFlowLitersPerSecond = 0.0f;

        initialPaintHeightMeters =
            paintEmitterStage02.Stage02_ComputeHeightFromVolumeLiters(initialPaintVolumeLiters);

        expectedSourceVolumeLiters = initialPaintVolumeLiters;

        FillInitialParticleVolume();
        BindBuffers();

        previousBucketVelocity = bucketStage01.BucketVelocity;
        lastObservedPaintColor = paintEmitterStage02.paintColor;
        nextStatisticsReadTime = Time.unscaledTime + statisticsReadbackInterval;

        StoreBuiltConfiguration();
        initialized = true;

        Debug.Log(
            "GPU Paint initialized. Active particles: " + activeParticleCount +
            ", volume: " + initialPaintVolumeLiters.ToString("0.000") +
            " L, particle volume: " + volumePerParticleLiters.ToString("0.000000") + " L"
        );
    }

    private bool HasValidReferences()
    {
        if (bucketStage01 == null)
        {
            Debug.LogError("GpuDensityPaintFluidStage03: Bucket Stage 01 is missing.");
            return false;
        }

        if (paintEmitterStage02 == null)
        {
            Debug.LogError("GpuDensityPaintFluidStage03: Paint Emitter Stage 02 is missing.");
            return false;
        }

        if (bucketRoot == null)
        {
            Debug.LogError("GpuDensityPaintFluidStage03: Bucket Root is missing.");
            return false;
        }

        if (paintExitPoint == null)
        {
            Debug.LogError("GpuDensityPaintFluidStage03: Paint Exit Point is missing.");
            return false;
        }

        if (paintCanvasSurface == null)
        {
            Debug.LogError("GpuDensityPaintFluidStage03: Paint Canvas Surface is missing.");
            return false;
        }

        if (fluidCompute == null)
        {
            Debug.LogError("GpuDensityPaintFluidStage03: Fluid Compute Shader is missing.");
            return false;
        }

        if (particleSplatMaterial == null)
        {
            Debug.LogError("GpuDensityPaintFluidStage03: Particle Splat Material is missing.");
            return false;
        }

        return true;
    }

    private void FindKernels()
    {
        kernelClearGrid = fluidCompute.FindKernel("KernelClearGrid");
        kernelSplatDensity = fluidCompute.FindKernel("KernelSplatDensity");
        kernelSimulate = fluidCompute.FindKernel("KernelSimulate");
        kernelClearStats = fluidCompute.FindKernel("KernelClearStats");
        kernelCountStats = fluidCompute.FindKernel("KernelCountStats");
        kernelApplyRuntimeColor = fluidCompute.FindKernel("KernelApplyRuntimeColor");
        kernelClearDrainCounter = fluidCompute.FindKernel("KernelClearDrainCounter");
        kernelClearPaintImpactCount =
            fluidCompute.FindKernel("KernelClearPaintImpactCount");
    }

    private void CreateBuffers()
    {
        int safeParticleCount = Mathf.Max(1, particleCount);
        int totalCells = gridResolution * gridResolution * gridResolution;

        particleBuffer = new ComputeBuffer(
            safeParticleCount,
            sizeof(float) * 12,
            ComputeBufferType.Structured
        );

        restPositionBuffer = new ComputeBuffer(
            safeParticleCount,
            sizeof(float) * 4,
            ComputeBufferType.Structured
        );

        gridCountBuffer = new ComputeBuffer(
            totalCells,
            sizeof(uint),
            ComputeBufferType.Structured
        );

        statisticsBuffer = new ComputeBuffer(
            4,
            sizeof(uint),
            ComputeBufferType.Structured
        );

        drainCounterBuffer = new ComputeBuffer(
            1,
            sizeof(uint),
            ComputeBufferType.Structured
        );

        drawArgsBuffer = new ComputeBuffer(
            4,
            sizeof(uint),
            ComputeBufferType.IndirectArguments
        );

        drawArgsBuffer.SetData(new uint[] { 6, 0, 0, 0 });

        paintImpactBuffer = new ComputeBuffer(
            Mathf.Max(1, maximumPaintImpactsPerFrame),
            sizeof(float) * 12,
            ComputeBufferType.Structured
        );

        paintImpactCountBuffer = new ComputeBuffer(
            1,
            sizeof(uint),
            ComputeBufferType.Structured
        );
    }

    private void BindBuffers()
    {
        fluidCompute.SetBuffer(kernelClearGrid, "_GridCounts", gridCountBuffer);

        fluidCompute.SetBuffer(kernelSplatDensity, "_Particles", particleBuffer);
        fluidCompute.SetBuffer(kernelSplatDensity, "_GridCounts", gridCountBuffer);

        fluidCompute.SetBuffer(kernelSimulate, "_Particles", particleBuffer);
        fluidCompute.SetBuffer(kernelSimulate, "_RestPositions", restPositionBuffer);
        fluidCompute.SetBuffer(kernelSimulate, "_GridCounts", gridCountBuffer);
        fluidCompute.SetBuffer(kernelSimulate, "_DrainCounter", drainCounterBuffer);

        fluidCompute.SetBuffer(kernelClearStats, "_Stats", statisticsBuffer);

        fluidCompute.SetBuffer(kernelCountStats, "_Particles", particleBuffer);
        fluidCompute.SetBuffer(kernelCountStats, "_Stats", statisticsBuffer);

        fluidCompute.SetBuffer(kernelApplyRuntimeColor, "_Particles", particleBuffer);
        fluidCompute.SetBuffer(kernelClearDrainCounter, "_DrainCounter", drainCounterBuffer);

        fluidCompute.SetBuffer(
            kernelSimulate,
            "_PaintImpacts",
            paintImpactBuffer
        );

        fluidCompute.SetBuffer(
            kernelSimulate,
            "_PaintImpactCount",
            paintImpactCountBuffer
        );

        fluidCompute.SetBuffer(
            kernelClearPaintImpactCount,
            "_PaintImpactCount",
            paintImpactCountBuffer
        );

        particleSplatMaterial.SetBuffer("_Particles", particleBuffer);
    }

    private void FillInitialParticleVolume()
    {
        GpuPaintParticle[] particles =
            new GpuPaintParticle[particleCount];

        Vector4[] restPositions =
            new Vector4[particleCount];

        for (int i = 0; i < particleCount; i++)
        {
            particles[i] = new GpuPaintParticle
            {
                positionState = new Vector4(0.0f, -9999.0f, 0.0f, 0.0f),
                velocityRadius = new Vector4(0.0f, 0.0f, 0.0f, particleVisualRadius),
                colorSeed = Vector4.zero
            };

            restPositions[i] = Vector4.zero;
        }

        float bucketCapacityLiters = Mathf.Max(
            0.0001f,
            paintEmitterStage02.BucketCapacityLiters
        );

        volumePerParticleLiters =
            bucketCapacityLiters / Mathf.Max(1, particleCount);

        int requestedActiveParticleCount = Mathf.Clamp(
            Mathf.RoundToInt(
                initialPaintVolumeLiters /
                Mathf.Max(0.000000001f, volumePerParticleLiters)
            ),
            0,
            particleCount
        );

        activeParticleCount = requestedActiveParticleCount;

        if (activeParticleCount <= 0)
        {
            calculatedPaintVolumeLiters = 0.0f;
            insideBucketParticleCount = 0;
            airborneParticleCount = 0;
            depositedParticleCount = 0;
            inactiveParticleCount = particleCount;

            particleBuffer.SetData(particles);
            restPositionBuffer.SetData(restPositions);
            drawArgsBuffer.SetData(new uint[] { 6, 0, 0, 0 });
            return;
        }

        float fillHeight = Mathf.Clamp(
            paintEmitterStage02.Stage02_ComputeHeightFromVolumeLiters(
                initialPaintVolumeLiters
            ),
            particleVisualRadius * 2.0f,
            paintEmitterStage02.bucketHeightMeters
        );

        float bottomY = GetBucketBottomLocalY();
        float usableBottomY = bottomY + particleVisualRadius;
        float usableTopY = Mathf.Max(
            usableBottomY,
            bottomY + fillHeight - particleVisualRadius
        );

        Color paintColor = paintEmitterStage02.paintColor;

        const int HEIGHT_TABLE_SIZE = 1024;
        float[] cumulativeVolume = new float[HEIGHT_TABLE_SIZE + 1];

        for (int i = 0; i <= HEIGHT_TABLE_SIZE; i++)
        {
            float t = i / (float)HEIGHT_TABLE_SIZE;
            float h = fillHeight * t;
            cumulativeVolume[i] = ComputeBucketVolumeToHeightCubicMeters(h);
        }

        float totalFillVolume = Mathf.Max(
            0.000000000001f,
            cumulativeVolume[HEIGHT_TABLE_SIZE]
        );

        const float GOLDEN_RATIO_CONJUGATE = 0.61803398875f;

        for (int i = 0; i < activeParticleCount; i++)
        {
            float jitter = Hash01((uint)i * 747796405u + 2891336453u);
            float volumeFraction = (i + 0.15f + jitter * 0.70f) /
                                   Mathf.Max(1.0f, activeParticleCount);
            volumeFraction = Mathf.Clamp01(volumeFraction);

            float targetVolume = volumeFraction * totalFillVolume;
            float sampledHeight = SampleHeightFromVolumeTable(
                targetVolume,
                cumulativeVolume,
                fillHeight
            );

            float y = Mathf.Clamp(
                bottomY + sampledHeight,
                usableBottomY,
                usableTopY
            );

            float heightFromBottom = y - bottomY;
            float radiusAtHeight = Mathf.Max(
                particleVisualRadius,
                paintEmitterStage02.Stage02_GetRadiusAtHeight(heightFromBottom) -
                particleVisualRadius
            );

            float radial01 = Mathf.Sqrt(
                Hash01((uint)i * 277803737u + 1013904223u)
            );

            float angle01 = Mathf.Repeat(
                i * GOLDEN_RATIO_CONJUGATE +
                Hash01((uint)i * 1597334677u + 3812015801u) * 0.035f,
                1.0f
            );

            float angle = angle01 * Mathf.PI * 2.0f;
            float radialDistance = radial01 * radiusAtHeight * 0.985f;

            Vector3 localPosition = new Vector3(
                Mathf.Cos(angle) * radialDistance,
                y,
                Mathf.Sin(angle) * radialDistance
            );

            particles[i] = CreateInsideParticle(localPosition, paintColor);
            restPositions[i] = new Vector4(
                localPosition.x,
                localPosition.y,
                localPosition.z,
                1.0f
            );
        }

        calculatedPaintVolumeLiters =
            activeParticleCount * volumePerParticleLiters;

        insideBucketParticleCount = activeParticleCount;
        airborneParticleCount = 0;
        depositedParticleCount = 0;
        inactiveParticleCount = particleCount - activeParticleCount;

        particleBuffer.SetData(particles);
        restPositionBuffer.SetData(restPositions);
        drawArgsBuffer.SetData(
            new uint[] { 6, (uint)activeParticleCount, 0, 0 }
        );

        Debug.Log(
            "Paint initialized across full volume: " +
            calculatedPaintVolumeLiters.ToString("0.000") +
            " L, active particles: " +
            activeParticleCount +
            " / " +
            particleCount +
            ", fill height: " +
            fillHeight.ToString("0.000") +
            " m"
        );
    }

    private float ComputeBucketVolumeToHeightCubicMeters(float height)
    {
        float bucketHeight = Mathf.Max(
            0.0001f,
            paintEmitterStage02.bucketHeightMeters
        );

        height = Mathf.Clamp(height, 0.0f, bucketHeight);

        if (height <= 0.0f)
            return 0.0f;

        float t = height / bucketHeight;
        float r0 = paintEmitterStage02.bucketBottomRadiusMeters;
        float r1 = Mathf.Lerp(
            paintEmitterStage02.bucketBottomRadiusMeters,
            paintEmitterStage02.bucketTopRadiusMeters,
            t
        );

        return Mathf.PI * height *
               (r0 * r0 + r0 * r1 + r1 * r1) /
               3.0f;
    }

    private static float SampleHeightFromVolumeTable(
        float targetVolume,
        float[] cumulativeVolume,
        float fillHeight
    )
    {
        int low = 0;
        int high = cumulativeVolume.Length - 1;

        while (high - low > 1)
        {
            int mid = (low + high) >> 1;

            if (cumulativeVolume[mid] < targetVolume)
                low = mid;
            else
                high = mid;
        }

        float v0 = cumulativeVolume[low];
        float v1 = cumulativeVolume[high];
        float blend = Mathf.InverseLerp(v0, v1, targetVolume);

        float t0 = low / (float)(cumulativeVolume.Length - 1);
        float t1 = high / (float)(cumulativeVolume.Length - 1);

        return Mathf.Lerp(t0, t1, blend) * fillHeight;
    }

    private static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7feb352du;
        value ^= value >> 15;
        value *= 0x846ca68bu;
        value ^= value >> 16;

        return (value & 0x00FFFFFFu) / 16777216.0f;
    }

    private GpuPaintParticle CreateInsideParticle(
        Vector3 localPosition,
        Color paintColor
    )
    {
        return new GpuPaintParticle
        {
            positionState = new Vector4(
                localPosition.x,
                localPosition.y,
                localPosition.z,
                1.0f
            ),
            velocityRadius = new Vector4(
                0,
                0,
                0,
                particleVisualRadius
            ),
            colorSeed = new Vector4(
                paintColor.r,
                paintColor.g,
                paintColor.b,
                Random.value
            )
        };
    }

    private void StoreBuiltConfiguration()
    {
        lastBuiltParticleCount = particleCount;
        lastBuiltGridResolution = gridResolution;
        lastBuiltParticleRadius = particleVisualRadius;
        lastBuiltBottomRadius = paintEmitterStage02.bucketBottomRadiusMeters;
        lastBuiltTopRadius = paintEmitterStage02.bucketTopRadiusMeters;
        lastBuiltBucketHeight = paintEmitterStage02.bucketHeightMeters;
        lastBuiltBottomOffset = paintEmitterStage02.bottomOffsetAboveExitMeters;
        lastBuiltMaximumPaintImpacts = maximumPaintImpactsPerFrame;
    }

    private void RebuildIfConfigurationChanged()
    {
        float sourceVolume = Mathf.Max(0.0f, paintEmitterStage02.paintVolumeLiters);

        bool sourceVolumeEditedExternally =
            Mathf.Abs(sourceVolume - expectedSourceVolumeLiters) >
            VOLUME_EDIT_THRESHOLD_LITERS;

        bool configurationChanged =
            particleCount != lastBuiltParticleCount ||
            gridResolution != lastBuiltGridResolution ||
            Mathf.Abs(particleVisualRadius - lastBuiltParticleRadius) > FLOAT_CHANGE_THRESHOLD ||
            Mathf.Abs(paintEmitterStage02.bucketBottomRadiusMeters - lastBuiltBottomRadius) > FLOAT_CHANGE_THRESHOLD ||
            Mathf.Abs(paintEmitterStage02.bucketTopRadiusMeters - lastBuiltTopRadius) > FLOAT_CHANGE_THRESHOLD ||
            Mathf.Abs(paintEmitterStage02.bucketHeightMeters - lastBuiltBucketHeight) > FLOAT_CHANGE_THRESHOLD ||
            Mathf.Abs(paintEmitterStage02.bottomOffsetAboveExitMeters - lastBuiltBottomOffset) > FLOAT_CHANGE_THRESHOLD ||
            maximumPaintImpactsPerFrame != lastBuiltMaximumPaintImpacts;

        if (sourceVolumeEditedExternally || configurationChanged)
            InitializeFluid();
    }

    private void UpdateRuntimePaintColor()
    {
        Color currentColor = paintEmitterStage02.paintColor;

        if (ColorsApproximatelyEqual(currentColor, lastObservedPaintColor))
            return;

        lastObservedPaintColor = currentColor;

        if (particleBuffer == null || activeParticleCount <= 0)
            return;

        fluidCompute.SetInt("_ParticleCount", activeParticleCount);
        fluidCompute.SetInt(
            "_ColorApplyMode",
            runtimeColorMode == RuntimePaintColorMode.RecolorEverything ? 1 : 0
        );
        fluidCompute.SetVector("_CurrentPaintColor", currentColor);

        int groups = Mathf.CeilToInt(activeParticleCount / (float)THREADS);
        fluidCompute.Dispatch(kernelApplyRuntimeColor, groups, 1, 1);
    }

    private static bool ColorsApproximatelyEqual(Color a, Color b)
    {
        return
            Mathf.Abs(a.r - b.r) < 0.0001f &&
            Mathf.Abs(a.g - b.g) < 0.0001f &&
            Mathf.Abs(a.b - b.b) < 0.0001f &&
            Mathf.Abs(a.a - b.a) < 0.0001f;
    }

    private void DispatchFluidSimulation()
    {
        float frameDeltaTime = Mathf.Min(Time.deltaTime, 0.033f);
        int substeps = Mathf.Max(1, solverSubsteps);
        float substepDeltaTime = frameDeltaTime / substeps;

        Vector3 bucketVelocity = bucketStage01.BucketVelocity;
        Vector3 bucketAcceleration =
            (bucketVelocity - previousBucketVelocity) /
            Mathf.Max(frameDeltaTime, 0.0001f);

        bucketAcceleration = Vector3.ClampMagnitude(
            bucketAcceleration,
            MAX_BUCKET_ACCELERATION
        );

        previousBucketVelocity = bucketVelocity;

        bool holeOpen =
            allowDrainFromHole &&
            paintEmitterStage02 != null &&
            paintEmitterStage02.IsHoleOpen &&
            volumePerParticleLiters > 0.000000001f;

        currentFlowLitersPerSecond =
            holeOpen
                ? paintEmitterStage02.Stage02_ComputeCurrentFlowLitersPerSecond()
                : 0.0f;

        int totalCells = gridResolution * gridResolution * gridResolution;
        int cellGroups = Mathf.CeilToInt(totalCells / (float)THREADS);
        int particleGroups = Mathf.CeilToInt(activeParticleCount / (float)THREADS);

        fluidCompute.SetInt(
            "_MaximumPaintImpacts",
            maximumPaintImpactsPerFrame
        );

        fluidCompute.SetFloat(
            "_VolumePerParticleLiters",
            volumePerParticleLiters
        );

        fluidCompute.Dispatch(
            kernelClearPaintImpactCount,
            1,
            1,
            1
        );

        for (int step = 0; step < substeps; step++)
        {
            currentDrainParticleBudget = CalculateDrainParticleBudget(
                currentFlowLitersPerSecond,
                substepDeltaTime,
                holeOpen
            );

            SetComputeParameters(
                substepDeltaTime,
                bucketVelocity,
                bucketAcceleration
            );

            fluidCompute.SetInt(
                "_DrainParticleBudget",
                currentDrainParticleBudget
            );

            fluidCompute.Dispatch(kernelClearDrainCounter, 1, 1, 1);
            fluidCompute.Dispatch(kernelClearGrid, cellGroups, 1, 1);
            fluidCompute.Dispatch(kernelSplatDensity, particleGroups, 1, 1);
            fluidCompute.Dispatch(kernelSimulate, particleGroups, 1, 1);
        }

        if (paintCanvasStage04 != null)
        {
            paintCanvasStage04.ProcessPaintImpacts(
                paintImpactBuffer,
                paintImpactCountBuffer,
                maximumPaintImpactsPerFrame,
                frameDeltaTime
            );
        }
    }

    private int CalculateDrainParticleBudget(
        float flowLitersPerSecond,
        float deltaTime,
        bool holeOpen
    )
    {
        if (!holeOpen)
        {
            drainParticleFraction = 0.0;
            return 0;
        }

        if (flowLitersPerSecond <= 0.0f)
            return 0;

        if (volumePerParticleLiters <= 0.000000001f)
            return 0;

        double desiredParticleCount =
            (flowLitersPerSecond * deltaTime) /
            volumePerParticleLiters;

        desiredParticleCount += drainParticleFraction;

        int wholeParticles = Mathf.FloorToInt((float)desiredParticleCount);
        drainParticleFraction = desiredParticleCount - wholeParticles;

        return Mathf.Clamp(wholeParticles, 0, activeParticleCount);
    }

    private void SetComputeParameters(
        float deltaTime,
        Vector3 bucketVelocity,
        Vector3 bucketAcceleration
    )
    {
        bool holeOpen =
            allowDrainFromHole &&
            paintEmitterStage02.IsHoleOpen;

        Vector3 holeLocal =
            bucketRoot.InverseTransformPoint(paintExitPoint.position);

        Vector3 exitDirectionWorld =
            paintExitPoint.TransformDirection(
                paintEmitterStage02.localExitDirection.normalized
            );

        if (exitDirectionWorld.sqrMagnitude < 0.000001f)
            exitDirectionWorld = Vector3.down;

        exitDirectionWorld.Normalize();

        float currentFillHeight =
            paintEmitterStage02.CurrentPaintHeightMeters;

        float exitSpeed =
            paintEmitterStage02.CurrentExitSpeedMetersPerSecond;

        GetCanvasSurfaceParameters(
            out float adhesion,
            out float flowMobility,
            out float impactSpread,
            out float tangentialDamping
        );

        fluidCompute.SetInt("_ParticleCount", activeParticleCount);
        fluidCompute.SetInt("_GridResolution", gridResolution);
        fluidCompute.SetInt(
            "_TotalCells",
            gridResolution * gridResolution * gridResolution
        );

        fluidCompute.SetFloat("_DeltaTime", deltaTime);
        fluidCompute.SetFloat("_BottomY", GetBucketBottomLocalY());
        fluidCompute.SetFloat("_BucketHeight", paintEmitterStage02.bucketHeightMeters);
        fluidCompute.SetFloat("_BottomRadius", paintEmitterStage02.bucketBottomRadiusMeters);
        fluidCompute.SetFloat("_TopRadius", paintEmitterStage02.bucketTopRadiusMeters);
        fluidCompute.SetFloat("_CurrentFillHeight", currentFillHeight);
        fluidCompute.SetFloat(
            "_InitialFillHeight",
            Mathf.Max(particleVisualRadius * 2.0f, initialPaintHeightMeters)
        );
        fluidCompute.SetFloat("_ParticleRadius", particleVisualRadius);
        fluidCompute.SetFloat(
            "_HoleRadius",
            holeOpen ? paintEmitterStage02.HoleRadiusMeters : 0.0f
        );
        fluidCompute.SetFloat("_AllowDrain", holeOpen ? 1.0f : 0.0f);
        fluidCompute.SetFloat("_Viscosity01", Mathf.Clamp01(paintEmitterStage02.viscosity01));
        fluidCompute.SetFloat("_PressureStrength", pressureStrength);
        fluidCompute.SetFloat("_WallFriction", wallFriction);
        fluidCompute.SetFloat("_ShapeSupportStrength", shapeSupportStrength);
        fluidCompute.SetFloat("_DrainSuctionStrength", drainSuctionStrength);
        fluidCompute.SetFloat("_VortexStrength", vortexStrength);
        fluidCompute.SetFloat("_DrainInfluenceRadius", drainInfluenceRadius);
        fluidCompute.SetFloat("_DrainInfluenceHeight", drainInfluenceHeight);
        fluidCompute.SetFloat("_StreamRenderRadiusScale", streamRenderRadiusScale);
        fluidCompute.SetFloat("_DepositedRadiusScale", depositedRadiusScale);
        fluidCompute.SetFloat("_ExitSpeed", exitSpeed);
        fluidCompute.SetFloat("_SimulationTime", Time.time);

        fluidCompute.SetVector(
            "_Gravity",
            Vector3.down * Mathf.Abs(bucketStage01.gravity)
        );
        fluidCompute.SetVector("_BucketVelocity", bucketVelocity);
        fluidCompute.SetVector("_BucketAcceleration", bucketAcceleration);
        fluidCompute.SetVector("_HoleLocal", holeLocal);
        fluidCompute.SetVector("_ExitDirectionWorld", exitDirectionWorld);
        fluidCompute.SetVector("_CurrentPaintColor", paintEmitterStage02.paintColor);
        fluidCompute.SetMatrix("_BucketLocalToWorld", bucketRoot.localToWorldMatrix);
        fluidCompute.SetMatrix("_BucketWorldToLocal", bucketRoot.worldToLocalMatrix);

        fluidCompute.SetFloat("_HasCanvas", paintCanvasSurface != null ? 1.0f : 0.0f);

        if (paintCanvasSurface != null)
        {
            fluidCompute.SetMatrix(
                "_CanvasLocalToWorld",
                paintCanvasSurface.localToWorldMatrix
            );
            fluidCompute.SetMatrix(
                "_CanvasWorldToLocal",
                paintCanvasSurface.worldToLocalMatrix
            );
            fluidCompute.SetVector("_CanvasPositionWorld", paintCanvasSurface.position);
            fluidCompute.SetVector("_CanvasNormalWorld", paintCanvasSurface.up.normalized);
        }

        fluidCompute.SetVector(
            "_CanvasHalfSize",
            new Vector4(
                canvasSizeMeters.x * 0.5f,
                canvasSizeMeters.y * 0.5f,
                0,
                0
            )
        );
        fluidCompute.SetFloat("_CanvasSurfaceOffset", canvasSurfaceOffsetMeters);
        fluidCompute.SetFloat("_CanvasAdhesion", adhesion);
        fluidCompute.SetFloat("_CanvasFlowMobility", flowMobility);
        fluidCompute.SetFloat("_CanvasImpactSpread", impactSpread);
        fluidCompute.SetFloat("_CanvasTangentialDamping", tangentialDamping);
        fluidCompute.SetFloat("_EdgeReleaseSpeed", edgeReleaseSpeed);
        fluidCompute.SetFloat(
            "_UseStage04CanvasPainting",
            paintCanvasStage04 != null ? 1.0f : 0.0f
        );
    }

    private void GetCanvasSurfaceParameters(
        out float adhesion,
        out float flowMobility,
        out float impactSpread,
        out float tangentialDamping
    )
    {
        switch (canvasSurfaceType)
        {
            case PaintCanvasSurfaceType.Cloth:
                adhesion = 0.96f;
                flowMobility = 0.035f;
                impactSpread = 1.35f;
                tangentialDamping = 0.55f;
                break;

            case PaintCanvasSurfaceType.Wood:
                adhesion = 0.79f;
                flowMobility = 0.16f;
                impactSpread = 1.65f;
                tangentialDamping = 0.78f;
                break;

            case PaintCanvasSurfaceType.Glass:
                adhesion = 0.16f;
                flowMobility = 0.92f;
                impactSpread = 2.25f;
                tangentialDamping = 0.96f;
                break;

            default:
                adhesion = customCanvasAdhesion;
                flowMobility = customCanvasFlowMobility;
                impactSpread = customImpactSpread;
                tangentialDamping = customTangentialDamping;
                break;
        }
    }

    private void TryUpdateParticleStatistics()
    {
        if (statisticsRequestPending)
            return;

        if (Time.unscaledTime < nextStatisticsReadTime)
            return;

        nextStatisticsReadTime =
            Time.unscaledTime + statisticsReadbackInterval;

        DispatchStatisticsKernels();

        if (SystemInfo.supportsAsyncGPUReadback)
        {
            statisticsRequestPending = true;
            int requestGeneration = bufferGeneration;

            AsyncGPUReadback.Request(
                statisticsBuffer,
                request =>
                {
                    statisticsRequestPending = false;

                    if (requestGeneration != bufferGeneration)
                        return;

                    if (!initialized || request.hasError)
                        return;

                    var data = request.GetData<uint>();

                    if (data.Length < 4)
                        return;

                    ApplyParticleStatistics(
                        data[0],
                        data[1],
                        data[2],
                        data[3]
                    );
                }
            );

            return;
        }

        if (!warnedAboutReadback)
        {
            warnedAboutReadback = true;
            Debug.LogWarning(
                "AsyncGPUReadback is not supported. Using synchronous statistics readback."
            );
        }

        uint[] stats = new uint[4];
        statisticsBuffer.GetData(stats);
        ApplyParticleStatistics(stats[0], stats[1], stats[2], stats[3]);
    }

    private void DispatchStatisticsKernels()
    {
        fluidCompute.SetInt("_ParticleCount", activeParticleCount);
        fluidCompute.Dispatch(kernelClearStats, 1, 1, 1);

        int groups = Mathf.CeilToInt(activeParticleCount / (float)THREADS);
        fluidCompute.Dispatch(kernelCountStats, groups, 1, 1);
    }

    private void ApplyParticleStatistics(
        uint inside,
        uint airborne,
        uint deposited,
        uint inactive
    )
    {
        insideBucketParticleCount = (int)inside;
        airborneParticleCount = (int)airborne;
        depositedParticleCount = (int)deposited;
        inactiveParticleCount = (int)inactive;

        calculatedPaintVolumeLiters =
            insideBucketParticleCount * volumePerParticleLiters;

        if (insideBucketParticleCount <= 0)
            calculatedPaintVolumeLiters = 0.0f;

        expectedSourceVolumeLiters = calculatedPaintVolumeLiters;

        paintEmitterStage02.Stage02_SetPaintVolumeLiters(
            calculatedPaintVolumeLiters
        );
    }

    private void RenderParticles()
    {
        if (
            particleBuffer == null ||
            drawArgsBuffer == null ||
            activeParticleCount <= 0
        )
        {
            return;
        }

        particleSplatMaterial.SetBuffer("_Particles", particleBuffer);
        particleSplatMaterial.SetMatrix("_BucketLocalToWorld", bucketRoot.localToWorldMatrix);
        particleSplatMaterial.SetMatrix("_CanvasLocalToWorld", paintCanvasSurface.localToWorldMatrix);
        particleSplatMaterial.SetFloat("_ParticleScale", 1.10f);
        particleSplatMaterial.SetFloat("_InsideAlpha", 0.055f);
        particleSplatMaterial.SetFloat("_AirborneAlpha", 0.94f);
        particleSplatMaterial.SetFloat("_DepositedAlpha", 0.98f);

        Vector3 center =
            (bucketRoot.position + paintCanvasSurface.position) * 0.5f;

        float distance = Vector3.Distance(
            bucketRoot.position,
            paintCanvasSurface.position
        );

        float boundsSize = Mathf.Max(
            20.0f,
            distance + Mathf.Max(canvasSizeMeters.x, canvasSizeMeters.y) + 10.0f
        );

        Bounds bounds = new Bounds(center, Vector3.one * boundsSize);

        Graphics.DrawProceduralIndirect(
            particleSplatMaterial,
            bounds,
            MeshTopology.Triangles,
            drawArgsBuffer,
            0,
            null,
            null,
            ShadowCastingMode.Off,
            false,
            gameObject.layer
        );
    }

    private float GetBucketBottomLocalY()
    {
        Vector3 holeLocal =
            bucketRoot.InverseTransformPoint(paintExitPoint.position);

        return
            holeLocal.y +
            paintEmitterStage02.bottomOffsetAboveExitMeters;
    }

    private void RemoveOldGeneratedTube()
    {
        Transform oldTube = transform.Find("Generated_GPU_Continuous_Stream");

        if (oldTube == null)
            return;

        oldTube.gameObject.SetActive(false);

        if (Application.isPlaying)
            Destroy(oldTube.gameObject);
        else
            DestroyImmediate(oldTube.gameObject);
    }

    private void ReleaseBuffers()
    {
        bufferGeneration++;
        statisticsRequestPending = false;

        particleBuffer?.Release();
        restPositionBuffer?.Release();
        gridCountBuffer?.Release();
        statisticsBuffer?.Release();
        drainCounterBuffer?.Release();
        drawArgsBuffer?.Release();
        paintImpactBuffer?.Release();
        paintImpactCountBuffer?.Release();

        particleBuffer = null;
        restPositionBuffer = null;
        gridCountBuffer = null;
        statisticsBuffer = null;
        drainCounterBuffer = null;
        drawArgsBuffer = null;
        paintImpactBuffer = null;
        paintImpactCountBuffer = null;

        initialized = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (paintCanvasSurface == null)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = paintCanvasSurface.localToWorldMatrix;
        Gizmos.color = new Color(0.1f, 0.8f, 1.0f, 0.85f);

        Gizmos.DrawWireCube(
            Vector3.zero,
            new Vector3(
                canvasSizeMeters.x,
                0.002f,
                canvasSizeMeters.y
            )
        );

        Gizmos.color = Color.green;
        Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.25f);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
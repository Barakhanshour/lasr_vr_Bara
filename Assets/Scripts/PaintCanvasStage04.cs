using UnityEngine;
using UnityEngine.Experimental.Rendering;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class PaintCanvasStage04 : MonoBehaviour
{
    public enum SurfaceType
    {
        Cloth,
        Wood,
        Glass,
        Custom
    }

    public enum CanvasPlaneMode
    {
        Auto,
        LocalXZ,
        LocalXY,
        LocalYZ
    }

    public struct CanvasFrame
    {
        public Transform surfaceTransform;
        public Vector3 centerWorld;
        public Vector3 axisUWorld;
        public Vector3 axisVWorld;
        public Vector3 normalWorld;
        public float halfWidthMeters;
        public float halfHeightMeters;
        public float worldHalfWidth;
        public float worldHalfHeight;
        public Vector3 localCenter;
        public Vector3 localAxisU;
        public Vector3 localAxisV;
        public Vector3 localNormal;
        public float localHalfWidth;
        public float localHalfHeight;

        public float WidthMeters => halfWidthMeters * 2.0f;
        public float HeightMeters => halfHeightMeters * 2.0f;
    }

    private struct SurfacePhysicsProfile
    {
        public float absorption;
        public float mobility;
        public float adhesion;
        public float grainAnisotropy;

        // Minimum physical paint film that remains adhered to the board and is not
        // transported by gravity. This is a surface property, not a second paint amount.
        public float residualFilmThicknessMeters;

        // Drying is intentionally separate from absorption.
        // delaySeconds = time during which fresh paint remains fully wet.
        // wetnessHalfLifeSeconds = exponential half-life after the delay.
        public float dryingDelaySeconds;
        public float wetnessHalfLifeSeconds;

        // Rendering-only pigment shift. This never modifies the stored paint color.
        public float maxDryColorShiftFraction;
    }

    [Header("References")]
    [Tooltip("The canvas transform. Local X is width, Z is height, and Y points straight out.")]
    public Transform canvasSurface;

    [Tooltip("The actual mesh renderer taking the paint, usually a floating quad.")]
    public Renderer canvasRenderer;

    [Tooltip("The compute shader doing the heavy lifting for the splat physics.")]
    public ComputeShader canvasCompute;

    [Tooltip("The material using the Custom/PaintCanvasSurface shader.")]
    public Material canvasMaterial;

    [Tooltip("Single authoritative gravity source for the whole project. Stage 04 reads gravity from this component and does not store its own gravity value.")]
    public SwingingBucketStage01 bucketStage01;

    [Tooltip("Single authoritative paint-property source. Density and viscosity are read from Stage02; Stage04 does not store duplicate paint values.")]
    public PaintEmitterStage02 paintEmitterStage02;

    [Header("Canvas")]
    public Vector2 canvasSizeMeters = new Vector2(2.0f, 2.0f);

    [Tooltip("How the paint surface plane is oriented in the renderer mesh local space. Auto detects the thinnest mesh axis.")]
    public CanvasPlaneMode canvasPlaneMode = CanvasPlaneMode.Auto;

    [Header("Canvas Mapping Diagnostic")]
    [Tooltip("Temporarily overlays the physical paint UV border/grid on the board. Disable after the mapping test passes.")]
    public bool showMappingDiagnostic = false;

    [Header("Drying Diagnostic")]
    [Tooltip("Shows paint age/wetness as diagnostic colors. This is visual debugging only and does not change the physics.")]
    public bool showDryingDiagnostic = false;

    [Range(256, 2048)]
    public int textureResolution = 1024;

    public SurfaceType surfaceType = SurfaceType.Cloth;

    [Header("Particle / Canvas Contact")]
    [Tooltip("Small normal offset used only to keep deposited particles numerically on the canvas plane.")]
    [Range(0.0001f, 0.01f)]
    public float surfaceOffsetMeters = 0.0012f;

    [Tooltip("Multiplier applied to the deposited particle radius before Stage 04 receives the impact.")]
    [Range(1.0f, 8.0f)]
    public float depositedParticleRadiusScale = 1.90f;

    [Tooltip("Release speed used only by the legacy particle-on-surface fallback path.")]
    [Range(0.0f, 1.0f)]
    public float edgeReleaseSpeed = 0.04f;

    [Header("Custom Particle Contact Values")]
    [Range(0.0f, 5.0f)]
    public float customImpactSpread = 0.80f;

    [Range(0.0f, 1.0f)]
    public float customTangentialDamping = 0.55f;

    [Tooltip("How many sub-steps we run the ooze simulation per frame.")]
    [Range(1, 4)]
    public int surfaceSimulationSteps = 4;

    [Header("Particle Deposition")]
    [Tooltip("Freshly deposited paint wetness. 1 means fully wet.")]
    [Range(0.0f, 2.0f)]
    public float depositedWetness = 1.0f;

    [Tooltip("How much the paint smears along the velocity vector when it hits.")]
    [Range(0.0f, 2.0f)]
    public float velocityStretch = 0.35f;

    [Header("Custom Surface Values")]
    [Range(0.0f, 1.0f)]
    public float customAbsorption = 0.55f;

    [Range(0.0f, 2.0f)]
    public float customMobility = 0.15f;

    [Range(0.0f, 1.0f)]
    public float customAdhesion = 0.75f;

    [Header("Custom Drying Model")]
    [Tooltip("Fresh paint stays fully wet for at least this many seconds before drying begins.")]
    [Min(0.0f)]
    public float customDryingDelaySeconds = 60.0f;

    [Tooltip("After the delay, this is the time required for wetness to fall to half its current value. Larger values mean much slower drying.")]
    [Min(1.0f)]
    public float customWetnessHalfLifeSeconds = 4500.0f;

    [Tooltip("Maximum pigment color darkening caused by drying. 0.08 means at most 8 percent even when fully dry.")]
    [Range(0.0f, 0.10f)]
    public float customMaxDryColorShiftFraction = 0.08f;

    [Header("Wood Grain")]
    public Vector2 woodGrainDirection = new Vector2(1.0f, 0.0f);

    [Range(0.0f, 1.0f)]
    public float woodGrainAnisotropy = 0.65f;

    [Header("Paint Appearance")]
    [Tooltip("Pigment optical extinction coefficient in 1/m. Used only by the renderer with the physical film thickness: alpha = 1 - exp(-k*h).")]
    [Range(1000.0f, 60000.0f)]
    public float pigmentOpticalExtinctionPerMeter = 45000.0f;

    [Range(0.0f, 5.0f)]
    public float reliefStrength = 1.25f;

    [Range(0.0f, 1.0f)]
    public float dryPaintSmoothness = 0.25f;

    [Range(0.0f, 1.0f)]
    public float wetPaintSmoothness = 0.92f;

    public RenderTexture ColorTexture => currentColor;
    public RenderTexture ThicknessTexture => currentThickness;
    public RenderTexture WetnessTexture => currentWetness;
    public RenderTexture AgeTexture => currentAge;
    public RenderTexture VelocityTexture => currentVelocity;

    // Derived runtime diagnostics. These are read-only calculations, not duplicate inputs.
    public Vector2 SurfaceGravityMetersPerSecondSquared { get; private set; }
    public float SurfaceNormalGravityMetersPerSecondSquared { get; private set; }
    public float SurfaceGravityMagnitudeMetersPerSecondSquared => SurfaceGravityMetersPerSecondSquared.magnitude;
    public float SurfaceSlopeDegrees { get; private set; }
    public float CurrentDynamicViscosityPaS { get; private set; }
    public float CurrentPaintDensityKgPerCubicMeter { get; private set; }

    private RenderTexture colorA;
    private RenderTexture colorB;
    private RenderTexture thicknessA;
    private RenderTexture thicknessB;
    private RenderTexture wetnessA;
    private RenderTexture wetnessB;
    private RenderTexture ageA;
    private RenderTexture ageB;
    private RenderTexture velocityA;
    private RenderTexture velocityB;

    private RenderTexture currentColor;
    private RenderTexture currentThickness;
    private RenderTexture currentWetness;
    private RenderTexture currentAge;
    private RenderTexture currentVelocity;

    private RenderTexture nextColor;
    private RenderTexture nextThickness;
    private RenderTexture nextWetness;
    private RenderTexture nextAge;
    private RenderTexture nextVelocity;

    private RenderTexture accumR;
    private RenderTexture accumG;
    private RenderTexture accumB;
    private RenderTexture accumWeight;
    private RenderTexture accumThickness;
    private RenderTexture accumWetness;
    private RenderTexture accumVelocityX;
    private RenderTexture accumVelocityY;

    private int kernelClearCanvas;
    private int kernelClearAccum;
    private int kernelDeposit;
    private int kernelResolve;
    private int kernelSimulate;

    private bool initialized;
    private int builtResolution;

    private const int PIXEL_THREADS = 8;
    private const int IMPACT_THREADS = 64;

    private void OnEnable()
    {
        ResolveReferences();
        InitializeCanvas();
    }

    private void Start()
    {
        ResolveReferences();
        InitializeCanvas();
    }

    private void OnValidate()
    {
        textureResolution = Mathf.Clamp(textureResolution, 256, 2048);
        canvasSizeMeters.x = Mathf.Max(0.01f, canvasSizeMeters.x);
        canvasSizeMeters.y = Mathf.Max(0.01f, canvasSizeMeters.y);
        surfaceSimulationSteps = Mathf.Clamp(surfaceSimulationSteps, 1, 4);
        customDryingDelaySeconds = Mathf.Max(0.0f, customDryingDelaySeconds);
        customWetnessHalfLifeSeconds = Mathf.Max(1.0f, customWetnessHalfLifeSeconds);
        customMaxDryColorShiftFraction = Mathf.Clamp(customMaxDryColorShiftFraction, 0.0f, 0.10f);
        pigmentOpticalExtinctionPerMeter = Mathf.Clamp(pigmentOpticalExtinctionPerMeter, 1000.0f, 60000.0f);
    }

    private void OnDisable()
    {
        ReleaseTextures();
    }

    private void OnDestroy()
    {
        ReleaseTextures();
    }

    [ContextMenu("STAGE 04 / Initialize Canvas")]
    public void InitializeCanvas()
    {
        ResolveReferences();

        if (canvasRenderer != null && canvasSurface == null)
            canvasSurface = canvasRenderer.transform;

        if (canvasCompute == null || canvasRenderer == null || canvasMaterial == null)
            return;

        if (initialized && builtResolution == textureResolution)
        {
            UpdateMaterialTextures();
            return;
        }

        ReleaseTextures();

        if (!TryFindKernels())
        {
            initialized = false;
            return;
        }

        CreateTextures();
        ClearPersistentTextures();

        canvasRenderer.sharedMaterial = canvasMaterial;
        UpdateMaterialTextures();

        builtResolution = textureResolution;
        initialized = true;
    }

    [ContextMenu("STAGE 04 / Clear Painting")]
    public void ClearPainting()
    {
        if (!initialized)
            InitializeCanvas();

        if (!initialized)
            return;

        ClearPersistentTextures();
        UpdateMaterialTextures();
    }

    private bool TryFindKernels()
    {
        try
        {
            kernelClearCanvas = canvasCompute.FindKernel("KernelClearCanvas");
            kernelClearAccum = canvasCompute.FindKernel("KernelClearAccum");
            kernelDeposit = canvasCompute.FindKernel("KernelDepositImpacts");
            kernelResolve = canvasCompute.FindKernel("KernelResolveDeposits");
            kernelSimulate = canvasCompute.FindKernel("KernelSimulateSurface");

            bool valid =
                kernelClearCanvas >= 0 &&
                kernelClearAccum >= 0 &&
                kernelDeposit >= 0 &&
                kernelResolve >= 0 &&
                kernelSimulate >= 0;

            if (!valid)
            {
                Debug.LogError(
                    "[Stage04] One or more compute kernels could not be found. " +
                    "Check PaintCanvasStage04.compute for compile errors before running the simulation.",
                    this
                );
            }

            return valid;
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                "[Stage04] PaintCanvasStage04.compute could not be initialized. " +
                exception.Message,
                this
            );
            return false;
        }
    }

    private void CreateTextures()
    {
        colorA = CreateFloatTexture("Paint_Color_A", GraphicsFormat.R16G16B16A16_SFloat);
        colorB = CreateFloatTexture("Paint_Color_B", GraphicsFormat.R16G16B16A16_SFloat);

        thicknessA = CreateFloatTexture("Paint_Thickness_A", GraphicsFormat.R16_SFloat);
        thicknessB = CreateFloatTexture("Paint_Thickness_B", GraphicsFormat.R16_SFloat);

        wetnessA = CreateFloatTexture("Paint_Wetness_A", GraphicsFormat.R16_SFloat);
        wetnessB = CreateFloatTexture("Paint_Wetness_B", GraphicsFormat.R16_SFloat);

        ageA = CreateFloatTexture("Paint_AgeSeconds_A", GraphicsFormat.R16_SFloat);
        ageB = CreateFloatTexture("Paint_AgeSeconds_B", GraphicsFormat.R16_SFloat);

        velocityA = CreateFloatTexture("Paint_Velocity_A", GraphicsFormat.R16G16_SFloat);
        velocityB = CreateFloatTexture("Paint_Velocity_B", GraphicsFormat.R16G16_SFloat);

        accumR = CreateIntegerTexture("Paint_Accum_R", GraphicsFormat.R32_UInt);
        accumG = CreateIntegerTexture("Paint_Accum_G", GraphicsFormat.R32_UInt);
        accumB = CreateIntegerTexture("Paint_Accum_B", GraphicsFormat.R32_UInt);
        accumWeight = CreateIntegerTexture("Paint_Accum_Weight", GraphicsFormat.R32_UInt);
        accumThickness = CreateIntegerTexture("Paint_Accum_Thickness", GraphicsFormat.R32_UInt);
        accumWetness = CreateIntegerTexture("Paint_Accum_Wetness", GraphicsFormat.R32_UInt);
        accumVelocityX = CreateIntegerTexture("Paint_Accum_VelocityX", GraphicsFormat.R32_SInt);
        accumVelocityY = CreateIntegerTexture("Paint_Accum_VelocityY", GraphicsFormat.R32_SInt);

        currentColor = colorA;
        currentThickness = thicknessA;
        currentWetness = wetnessA;
        currentAge = ageA;
        currentVelocity = velocityA;

        nextColor = colorB;
        nextThickness = thicknessB;
        nextWetness = wetnessB;
        nextAge = ageB;
        nextVelocity = velocityB;
    }

    private RenderTexture CreateFloatTexture(string textureName, GraphicsFormat format)
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
            textureResolution,
            textureResolution
        );

        descriptor.graphicsFormat = format;
        descriptor.depthBufferBits = 0;
        descriptor.msaaSamples = 1;
        descriptor.enableRandomWrite = true;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.sRGB = false;

        RenderTexture texture = new RenderTexture(descriptor);
        texture.name = textureName;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Create();

        return texture;
    }

    private RenderTexture CreateIntegerTexture(string textureName, GraphicsFormat format)
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
            textureResolution,
            textureResolution
        );

        descriptor.graphicsFormat = format;
        descriptor.depthBufferBits = 0;
        descriptor.msaaSamples = 1;
        descriptor.enableRandomWrite = true;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.sRGB = false;

        RenderTexture texture = new RenderTexture(descriptor);
        texture.name = textureName;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Point;
        texture.Create();

        return texture;
    }

    public void ProcessPaintImpacts(
        ComputeBuffer impactBuffer,
        ComputeBuffer impactCountBuffer,
        int maximumImpacts,
        float deltaTime
    )
    {
        if (!initialized)
            InitializeCanvas();

        if (
            !initialized ||
            impactBuffer == null ||
            impactCountBuffer == null ||
            maximumImpacts <= 0
        )
        {
            return;
        }

        deltaTime = Mathf.Min(Mathf.Max(deltaTime, 0.0001f), 0.033f);

        SetCommonParameters(deltaTime);
        ClearAccumulationTextures();

        BindAccumulationWriteTextures(kernelDeposit);
        canvasCompute.SetBuffer(kernelDeposit, "_PaintImpacts", impactBuffer);
        canvasCompute.SetBuffer(kernelDeposit, "_PaintImpactCount", impactCountBuffer);
        canvasCompute.SetInt("_MaximumImpacts", maximumImpacts);

        int impactGroups = Mathf.CeilToInt(maximumImpacts / (float)IMPACT_THREADS);
        canvasCompute.Dispatch(kernelDeposit, impactGroups, 1, 1);

        ResolveDeposits(deltaTime);

        int simulationSteps = Mathf.Max(1, surfaceSimulationSteps);
        float simulationDt = deltaTime / simulationSteps;

        for (int i = 0; i < simulationSteps; i++)
            SimulateSurface(simulationDt);

        UpdateMaterialTextures();
    }

    private void ClearPersistentTextures()
    {
        BindPersistentWriteTextures(
            kernelClearCanvas,
            colorA,
            thicknessA,
            wetnessA,
            ageA,
            velocityA
        );

        DispatchPixels(kernelClearCanvas);

        BindPersistentWriteTextures(
            kernelClearCanvas,
            colorB,
            thicknessB,
            wetnessB,
            ageB,
            velocityB
        );

        DispatchPixels(kernelClearCanvas);
        ClearAccumulationTextures();
    }

    private void ClearAccumulationTextures()
    {
        BindAccumulationWriteTextures(kernelClearAccum);
        DispatchPixels(kernelClearAccum);
    }

    private void ResolveDeposits(float deltaTime)
    {
        SetCommonParameters(deltaTime);

        canvasCompute.SetTexture(kernelResolve, "_ColorRead", currentColor);
        canvasCompute.SetTexture(kernelResolve, "_ThicknessRead", currentThickness);
        canvasCompute.SetTexture(kernelResolve, "_WetnessRead", currentWetness);
        canvasCompute.SetTexture(kernelResolve, "_AgeRead", currentAge);
        canvasCompute.SetTexture(kernelResolve, "_VelocityRead", currentVelocity);

        BindPersistentWriteTextures(
            kernelResolve,
            nextColor,
            nextThickness,
            nextWetness,
            nextAge,
            nextVelocity
        );

        BindAccumulationReadTextures(kernelResolve);
        DispatchPixels(kernelResolve);
        SwapPersistentTextures();
    }

    private void SimulateSurface(float deltaTime)
    {
        SetCommonParameters(deltaTime);

        canvasCompute.SetTexture(kernelSimulate, "_ColorRead", currentColor);
        canvasCompute.SetTexture(kernelSimulate, "_ThicknessRead", currentThickness);
        canvasCompute.SetTexture(kernelSimulate, "_WetnessRead", currentWetness);
        canvasCompute.SetTexture(kernelSimulate, "_AgeRead", currentAge);
        canvasCompute.SetTexture(kernelSimulate, "_VelocityRead", currentVelocity);

        BindPersistentWriteTextures(
            kernelSimulate,
            nextColor,
            nextThickness,
            nextWetness,
            nextAge,
            nextVelocity
        );

        DispatchPixels(kernelSimulate);
        SwapPersistentTextures();
    }

    private void BindPersistentWriteTextures(
        int kernel,
        RenderTexture color,
        RenderTexture thickness,
        RenderTexture wetness,
        RenderTexture age,
        RenderTexture velocity
    )
    {
        canvasCompute.SetTexture(kernel, "_ColorWrite", color);
        canvasCompute.SetTexture(kernel, "_ThicknessWrite", thickness);
        canvasCompute.SetTexture(kernel, "_WetnessWrite", wetness);
        canvasCompute.SetTexture(kernel, "_AgeWrite", age);
        canvasCompute.SetTexture(kernel, "_VelocityWrite", velocity);
    }

    private void BindAccumulationWriteTextures(int kernel)
    {
        canvasCompute.SetTexture(kernel, "_AccumRWrite", accumR);
        canvasCompute.SetTexture(kernel, "_AccumGWrite", accumG);
        canvasCompute.SetTexture(kernel, "_AccumBWrite", accumB);
        canvasCompute.SetTexture(kernel, "_AccumWeightWrite", accumWeight);
        canvasCompute.SetTexture(kernel, "_AccumThicknessWrite", accumThickness);
        canvasCompute.SetTexture(kernel, "_AccumWetnessWrite", accumWetness);
        canvasCompute.SetTexture(kernel, "_AccumVelocityXWrite", accumVelocityX);
        canvasCompute.SetTexture(kernel, "_AccumVelocityYWrite", accumVelocityY);
    }

    private void BindAccumulationReadTextures(int kernel)
    {
        canvasCompute.SetTexture(kernel, "_AccumRRead", accumR);
        canvasCompute.SetTexture(kernel, "_AccumGRead", accumG);
        canvasCompute.SetTexture(kernel, "_AccumBRead", accumB);
        canvasCompute.SetTexture(kernel, "_AccumWeightRead", accumWeight);
        canvasCompute.SetTexture(kernel, "_AccumThicknessRead", accumThickness);
        canvasCompute.SetTexture(kernel, "_AccumWetnessRead", accumWetness);
        canvasCompute.SetTexture(kernel, "_AccumVelocityXRead", accumVelocityX);
        canvasCompute.SetTexture(kernel, "_AccumVelocityYRead", accumVelocityY);
    }

    private void DispatchPixels(int kernel)
    {
        int groups = Mathf.CeilToInt(textureResolution / (float)PIXEL_THREADS);
        canvasCompute.Dispatch(kernel, groups, groups, 1);
    }

    private void SetCommonParameters(float deltaTime)
    {
        SurfacePhysicsProfile profile = GetSurfacePhysicsProfile();
        ResolveReferences();

        float viscosity01 = paintEmitterStage02 != null
            ? Mathf.Clamp01(paintEmitterStage02.viscosity01)
            : 0.62f;

        // A single normalized viscosity control is mapped to a physically
        // meaningful dynamic viscosity range for architectural paint.
        CurrentDynamicViscosityPaS = 0.4f * Mathf.Pow(20.0f, viscosity01);
        CurrentPaintDensityKgPerCubicMeter = paintEmitterStage02 != null
            ? Mathf.Max(100.0f, paintEmitterStage02.paintDensityKgPerLiter * 1000.0f)
            : 1200.0f;

        Vector2 grain = woodGrainDirection.sqrMagnitude > 0.0001f
            ? woodGrainDirection.normalized
            : Vector2.right;

        Vector2 surfaceGravityMetersPerSecondSquared = Vector2.zero;
        float surfaceNormalGravityMetersPerSecondSquared = 0.0f;
        Vector2 physicalCanvasSize = canvasSizeMeters;
        SurfaceSlopeDegrees = 0.0f;

        if (TryGetCanvasFrame(out CanvasFrame frame))
        {
            physicalCanvasSize = new Vector2(
                Mathf.Max(0.001f, frame.WidthMeters),
                Mathf.Max(0.001f, frame.HeightMeters)
            );

            Vector3 worldGravity = GetWorldGravity();
            Vector3 normal = frame.normalWorld.normalized;

            // Exact vector projection of gravity onto the canvas tangent plane:
            // g_parallel = g - (g dot n) n
            float gravityNormalSigned = Vector3.Dot(worldGravity, normal);
            surfaceNormalGravityMetersPerSecondSquared = Mathf.Abs(gravityNormalSigned);

            Vector3 gravityParallelWorld =
                worldGravity -
                gravityNormalSigned * normal;

            surfaceGravityMetersPerSecondSquared.x =
                Vector3.Dot(gravityParallelWorld, frame.axisUWorld);

            surfaceGravityMetersPerSecondSquared.y =
                Vector3.Dot(gravityParallelWorld, frame.axisVWorld);

            float gravityMagnitude = worldGravity.magnitude;
            if (gravityMagnitude > 0.000001f)
            {
                float ratio = Mathf.Clamp01(
                    gravityParallelWorld.magnitude / gravityMagnitude
                );

                SurfaceSlopeDegrees =
                    Mathf.Asin(ratio) * Mathf.Rad2Deg;
            }
        }

        SurfaceGravityMetersPerSecondSquared =
            surfaceGravityMetersPerSecondSquared;
        SurfaceNormalGravityMetersPerSecondSquared =
            surfaceNormalGravityMetersPerSecondSquared;

        canvasCompute.SetInt("_TextureResolution", textureResolution);
        canvasCompute.SetFloat("_DeltaTime", deltaTime);
        canvasCompute.SetVector(
            "_CanvasSizeMeters",
            new Vector4(physicalCanvasSize.x, physicalCanvasSize.y, 0, 0)
        );

        canvasCompute.SetFloat("_DepositedWetness", depositedWetness);
        canvasCompute.SetFloat("_VelocityStretch", velocityStretch);

        canvasCompute.SetFloat("_SurfaceAbsorption", profile.absorption);
        canvasCompute.SetFloat("_SurfaceMobility", profile.mobility);
        canvasCompute.SetFloat("_SurfaceAdhesion", profile.adhesion);
        canvasCompute.SetFloat(
            "_ResidualFilmThicknessMeters",
            Mathf.Max(0.0f, profile.residualFilmThicknessMeters)
        );

        // Phase 3 drying model. There is no independent evaporation coefficient anymore.
        canvasCompute.SetFloat("_DryingDelaySeconds", profile.dryingDelaySeconds);
        canvasCompute.SetFloat("_WetnessHalfLifeSeconds", profile.wetnessHalfLifeSeconds);

        canvasCompute.SetVector(
            "_SurfaceGravityMS2",
            new Vector4(
                surfaceGravityMetersPerSecondSquared.x,
                surfaceGravityMetersPerSecondSquared.y,
                0,
                0
            )
        );
        canvasCompute.SetFloat(
            "_SurfaceNormalGravityMS2",
            surfaceNormalGravityMetersPerSecondSquared
        );
        canvasCompute.SetFloat("_DynamicViscosityPaS", CurrentDynamicViscosityPaS);
        canvasCompute.SetFloat("_PaintDensityKgM3", CurrentPaintDensityKgPerCubicMeter);

        canvasCompute.SetVector(
            "_WoodGrainDirection",
            new Vector4(grain.x, grain.y, 0, 0)
        );

        canvasCompute.SetFloat("_WoodGrainAnisotropy", profile.grainAnisotropy);
    }

    private void ResolveReferences()
    {
        if (bucketStage01 == null)
            bucketStage01 = FindFirstObjectByType<SwingingBucketStage01>();

        if (paintEmitterStage02 == null)
            paintEmitterStage02 = FindFirstObjectByType<PaintEmitterStage02>();
    }

    private Vector3 GetWorldGravity()
    {
        ResolveReferences();

        if (bucketStage01 == null)
            return Vector3.zero;

        return Vector3.down * Mathf.Abs(bucketStage01.gravity);
    }

    [ContextMenu("STAGE 04 / Print Tilt Physics Debug")]
    public void PrintTiltPhysicsDebug()
    {
        if (!TryGetCanvasFrame(out CanvasFrame frame))
        {
            Debug.LogWarning("[Stage04] Canvas frame could not be resolved.", this);
            return;
        }

        Vector3 worldGravity = GetWorldGravity();
        Vector3 normal = frame.normalWorld.normalized;
        Vector3 gravityParallelWorld =
            worldGravity - Vector3.Dot(worldGravity, normal) * normal;

        Vector2 gravitySurface = new Vector2(
            Vector3.Dot(gravityParallelWorld, frame.axisUWorld),
            Vector3.Dot(gravityParallelWorld, frame.axisVWorld)
        );

        float slope = 0.0f;
        if (worldGravity.magnitude > 0.000001f)
        {
            slope = Mathf.Asin(
                Mathf.Clamp01(
                    gravityParallelWorld.magnitude / worldGravity.magnitude
                )
            ) * Mathf.Rad2Deg;
        }

        Debug.Log(
            $"[Stage04 Tilt Physics] Slope={slope:F2} deg | " +
            $"g_parallel(U,V)=({gravitySurface.x:F3}, {gravitySurface.y:F3}) m/s^2 | " +
            $"|g_parallel|={gravitySurface.magnitude:F3} m/s^2 | " +
            $"gravity source={worldGravity.magnitude:F3} m/s^2",
            this
        );
    }

    public void GetParticleImpactSurfaceParameters(
        out float adhesion,
        out float flowMobility,
        out float impactSpread,
        out float tangentialDamping
    )
    {
        SurfacePhysicsProfile profile = GetSurfacePhysicsProfile();

        adhesion = profile.adhesion;
        flowMobility = profile.mobility;

        switch (surfaceType)
        {
            case SurfaceType.Cloth:
                impactSpread = 1.35f;
                tangentialDamping = 0.55f;
                break;

            case SurfaceType.Wood:
                impactSpread = 1.65f;
                tangentialDamping = 0.78f;
                break;

            case SurfaceType.Glass:
                impactSpread = 2.25f;
                tangentialDamping = 0.96f;
                break;

            default:
                impactSpread = customImpactSpread;
                tangentialDamping = customTangentialDamping;
                break;
        }
    }

    private SurfacePhysicsProfile GetSurfacePhysicsProfile()
    {
        SurfacePhysicsProfile profile = default;

        switch (surfaceType)
        {
            case SurfaceType.Cloth:
                profile.absorption = 0.88f;
                profile.mobility = 0.035f;
                profile.adhesion = 0.97f;
                profile.grainAnisotropy = 0.10f;
                profile.residualFilmThicknessMeters = 0.000070f; // 0.070 mm

                // Fresh paint remains visually wet for one full minute.
                // After that, drying is deliberately very slow.
                profile.dryingDelaySeconds = 60.0f;
                profile.wetnessHalfLifeSeconds = 5400.0f; // 90 minutes
                profile.maxDryColorShiftFraction = 0.08f; // <= 8%
                break;

            case SurfaceType.Wood:
                profile.absorption = 0.48f;
                profile.mobility = 0.14f;
                profile.adhesion = 0.80f;
                profile.grainAnisotropy = woodGrainAnisotropy;
                profile.residualFilmThicknessMeters = 0.000055f; // 0.055 mm

                profile.dryingDelaySeconds = 60.0f;
                profile.wetnessHalfLifeSeconds = 7200.0f; // 120 minutes
                profile.maxDryColorShiftFraction = 0.08f; // <= 8%
                break;

            case SurfaceType.Glass:
                profile.absorption = 0.01f;
                profile.mobility = 0.92f;
                profile.adhesion = 0.18f;
                profile.grainAnisotropy = 0.0f;
                profile.residualFilmThicknessMeters = 0.000050f; // 0.050 mm

                profile.dryingDelaySeconds = 60.0f;
                profile.wetnessHalfLifeSeconds = 14400.0f; // 240 minutes
                profile.maxDryColorShiftFraction = 0.03f; // <= 3%
                break;

            default:
                profile.absorption = customAbsorption;
                profile.mobility = customMobility;
                profile.adhesion = customAdhesion;
                profile.grainAnisotropy = 0.0f;
                profile.residualFilmThicknessMeters = Mathf.Lerp(0.000040f, 0.000080f, Mathf.Clamp01(customAdhesion));
                profile.dryingDelaySeconds = Mathf.Max(0.0f, customDryingDelaySeconds);
                profile.wetnessHalfLifeSeconds = Mathf.Max(1.0f, customWetnessHalfLifeSeconds);
                profile.maxDryColorShiftFraction =
                    Mathf.Clamp(customMaxDryColorShiftFraction, 0.0f, 0.10f);
                break;
        }

        return profile;
    }

    [ContextMenu("FINAL / Apply Recommended Surface Defaults")]
    public void ApplyRecommendedFinalDefaults()
    {
        surfaceSimulationSteps = 2;
        depositedWetness = 1.0f;
        velocityStretch = 0.20f;
        pigmentOpticalExtinctionPerMeter = 45000.0f;
        woodGrainAnisotropy = 0.20f;
        customDryingDelaySeconds = 60.0f;
        customWetnessHalfLifeSeconds = 7200.0f;
        customMaxDryColorShiftFraction = 0.08f;

        if (Application.isPlaying && initialized)
            UpdateMaterialTextures();

        Debug.Log("[FINAL Stage04] Recommended stable paint defaults applied. Canvas size and paint source values were not changed.", this);
    }

    [ContextMenu("FINAL / Print Paint Physics Debug")]
    public void PrintFinalPaintPhysicsDebug()
    {
        SetCommonParameters(1.0f / 60.0f);
        Debug.Log(
            $"[FINAL Paint Physics] Surface={surfaceType} | Slope={SurfaceSlopeDegrees:F2} deg | " +
            $"g_parallel=({SurfaceGravityMetersPerSecondSquared.x:F3}, {SurfaceGravityMetersPerSecondSquared.y:F3}) m/s^2 | " +
            $"g_normal={SurfaceNormalGravityMetersPerSecondSquared:F3} m/s^2 | " +
            $"Density={CurrentPaintDensityKgPerCubicMeter:F1} kg/m^3 | " +
            $"Dynamic viscosity={CurrentDynamicViscosityPaS:F3} Pa.s | " +
            $"Residual film={GetSurfacePhysicsProfile().residualFilmThicknessMeters * 1000.0f:F3} mm",
            this
        );
    }

    [ContextMenu("STAGE 04 / Print Drying Physics Debug")]
    public void PrintDryingPhysicsDebug()
    {
        SurfacePhysicsProfile profile = GetSurfacePhysicsProfile();

        float oneMinuteAfterDelayWetness =
            Mathf.Pow(
                0.5f,
                60.0f / Mathf.Max(1.0f, profile.wetnessHalfLifeSeconds)
            );

        Debug.Log(
            $"[Stage04 Drying Physics] Surface={surfaceType} | " +
            $"Drying delay={profile.dryingDelaySeconds:F1} s | " +
            $"Wetness half-life={profile.wetnessHalfLifeSeconds:F1} s " +
            $"({profile.wetnessHalfLifeSeconds / 60.0f:F1} min) | " +
            $"Wetness after first 60 s of active drying={oneMinuteAfterDelayWetness * 100.0f:F2}% | " +
            $"Max pigment color shift={profile.maxDryColorShiftFraction * 100.0f:F1}%",
            this
        );
    }

    private void SwapPersistentTextures()
    {
        (currentColor, nextColor) = (nextColor, currentColor);
        (currentThickness, nextThickness) = (nextThickness, currentThickness);
        (currentWetness, nextWetness) = (nextWetness, currentWetness);
        (currentAge, nextAge) = (nextAge, currentAge);
        (currentVelocity, nextVelocity) = (nextVelocity, currentVelocity);
    }

    private void UpdateMaterialTextures()
    {
        if (canvasMaterial == null)
            return;

        SurfacePhysicsProfile profile = GetSurfacePhysicsProfile();

        canvasMaterial.SetTexture("_PaintColorMap", currentColor);
        canvasMaterial.SetTexture("_PaintThicknessMap", currentThickness);
        canvasMaterial.SetTexture("_PaintWetnessMap", currentWetness);
        canvasMaterial.SetTexture("_PaintAgeMap", currentAge);
        canvasMaterial.SetFloat("_PigmentOpticalExtinctionPerMeter", pigmentOpticalExtinctionPerMeter);
        canvasMaterial.SetFloat("_ReliefStrength", reliefStrength);
        canvasMaterial.SetFloat("_DryPaintSmoothness", dryPaintSmoothness);
        canvasMaterial.SetFloat("_WetPaintSmoothness", wetPaintSmoothness);
        canvasMaterial.SetFloat("_DryingDelaySeconds", profile.dryingDelaySeconds);
        canvasMaterial.SetFloat("_MaxDryColorShiftFraction", profile.maxDryColorShiftFraction);
        canvasMaterial.SetFloat("_DebugMapping", showMappingDiagnostic ? 1.0f : 0.0f);
        canvasMaterial.SetFloat("_DebugDrying", showDryingDiagnostic ? 1.0f : 0.0f);
        canvasMaterial.SetVector(
            "_PaintTexelSize",
            new Vector4(
                1.0f / textureResolution,
                1.0f / textureResolution,
                textureResolution,
                textureResolution
            )
        );

        Vector2 physicalSizeForMaterial = canvasSizeMeters;
        if (TryGetCanvasFrame(out CanvasFrame physicalFrame))
        {
            physicalSizeForMaterial = new Vector2(
                Mathf.Max(0.001f, physicalFrame.WidthMeters),
                Mathf.Max(0.001f, physicalFrame.HeightMeters)
            );
        }

        canvasMaterial.SetVector(
            "_PaintMetersPerTexel",
            new Vector4(
                physicalSizeForMaterial.x / textureResolution,
                physicalSizeForMaterial.y / textureResolution,
                0.0f,
                0.0f
            )
        );

        if (TryGetCanvasFrame(out CanvasFrame frame))
        {
            canvasMaterial.SetVector(
                "_PaintLocalCenter",
                new Vector4(
                    frame.localCenter.x,
                    frame.localCenter.y,
                    frame.localCenter.z,
                    1.0f
                )
            );

            canvasMaterial.SetVector(
                "_PaintLocalAxisU",
                new Vector4(
                    frame.localAxisU.x,
                    frame.localAxisU.y,
                    frame.localAxisU.z,
                    0.0f
                )
            );

            canvasMaterial.SetVector(
                "_PaintLocalAxisV",
                new Vector4(
                    frame.localAxisV.x,
                    frame.localAxisV.y,
                    frame.localAxisV.z,
                    0.0f
                )
            );

            canvasMaterial.SetVector(
                "_PaintLocalHalfSize",
                new Vector4(
                    Mathf.Max(0.000001f, frame.localHalfWidth),
                    Mathf.Max(0.000001f, frame.localHalfHeight),
                    0.0f,
                    0.0f
                )
            );
        }
    }

    public bool TryGetCanvasFrame(out CanvasFrame frame)
    {
        frame = default;

        Renderer renderer = canvasRenderer;
        Transform surfaceTransform =
            renderer != null
                ? renderer.transform
                : canvasSurface;

        if (surfaceTransform == null)
            return false;

        if (canvasSurface == null)
            canvasSurface = surfaceTransform;

        Bounds localBounds;
        bool hasMeshBounds = false;

        MeshFilter meshFilter = surfaceTransform.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            localBounds = meshFilter.sharedMesh.bounds;
            hasMeshBounds = true;
        }
        else
        {
            localBounds = new Bounds(
                Vector3.zero,
                new Vector3(
                    Mathf.Max(0.01f, canvasSizeMeters.x),
                    0.001f,
                    Mathf.Max(0.01f, canvasSizeMeters.y)
                )
            );
        }

        ResolveLocalPlane(
            localBounds,
            out Vector3 localAxisU,
            out Vector3 localAxisV,
            out Vector3 localNormal,
            out float localHalfWidth,
            out float localHalfHeight
        );

        Vector3 axisUVector = surfaceTransform.TransformVector(localAxisU);
        Vector3 axisVVector = surfaceTransform.TransformVector(localAxisV);
        Vector3 normalVector = surfaceTransform.TransformVector(localNormal);

        float axisUScale = axisUVector.magnitude;
        float axisVScale = axisVVector.magnitude;

        if (axisUScale <= 0.000001f || axisVScale <= 0.000001f)
            return false;

        frame.surfaceTransform = surfaceTransform;
        frame.centerWorld = surfaceTransform.TransformPoint(localBounds.center);
        frame.axisUWorld = axisUVector / axisUScale;
        frame.axisVWorld = axisVVector / axisVScale;
        frame.normalWorld = normalVector.sqrMagnitude > 0.000001f
            ? normalVector.normalized
            : Vector3.Cross(frame.axisVWorld, frame.axisUWorld).normalized;

        frame.localCenter = localBounds.center;
        frame.localAxisU = localAxisU;
        frame.localAxisV = localAxisV;
        frame.localNormal = localNormal;
        frame.localHalfWidth = Mathf.Max(0.000001f, localHalfWidth);
        frame.localHalfHeight = Mathf.Max(0.000001f, localHalfHeight);

        // IMPORTANT: canvasSizeMeters is the ONE authoritative physical size.
        // Mesh bounds are used only to know where the visible board edges are in the scene.
        frame.halfWidthMeters = Mathf.Max(0.005f, canvasSizeMeters.x * 0.5f);
        frame.halfHeightMeters = Mathf.Max(0.005f, canvasSizeMeters.y * 0.5f);

        frame.worldHalfWidth = Mathf.Max(0.000001f, frame.localHalfWidth * axisUScale);
        frame.worldHalfHeight = Mathf.Max(0.000001f, frame.localHalfHeight * axisVScale);

        return true;
    }

    private void ResolveLocalPlane(
        Bounds localBounds,
        out Vector3 localAxisU,
        out Vector3 localAxisV,
        out Vector3 localNormal,
        out float localHalfWidth,
        out float localHalfHeight
    )
    {
        CanvasPlaneMode mode = canvasPlaneMode;

        if (mode == CanvasPlaneMode.Auto)
        {
            Vector3 e = localBounds.extents;

            if (e.y <= e.x && e.y <= e.z)
                mode = CanvasPlaneMode.LocalXZ;
            else if (e.z <= e.x && e.z <= e.y)
                mode = CanvasPlaneMode.LocalXY;
            else
                mode = CanvasPlaneMode.LocalYZ;
        }

        switch (mode)
        {
            case CanvasPlaneMode.LocalXY:
                localAxisU = Vector3.right;
                localAxisV = Vector3.up;
                localNormal = Vector3.forward;
                localHalfWidth = localBounds.extents.x;
                localHalfHeight = localBounds.extents.y;
                break;

            case CanvasPlaneMode.LocalYZ:
                localAxisU = Vector3.up;
                localAxisV = Vector3.forward;
                localNormal = Vector3.right;
                localHalfWidth = localBounds.extents.y;
                localHalfHeight = localBounds.extents.z;
                break;

            default:
                localAxisU = Vector3.right;
                localAxisV = Vector3.forward;
                localNormal = Vector3.up;
                localHalfWidth = localBounds.extents.x;
                localHalfHeight = localBounds.extents.z;
                break;
        }

        // Meshes with a degenerate plane axis can still fall back to the configured physical size.
        if (localHalfWidth <= 0.000001f)
            localHalfWidth = Mathf.Max(0.005f, canvasSizeMeters.x * 0.5f);

        if (localHalfHeight <= 0.000001f)
            localHalfHeight = Mathf.Max(0.005f, canvasSizeMeters.y * 0.5f);
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryGetCanvasFrame(out CanvasFrame frame))
            return;

        Color previousColor = Gizmos.color;

        Vector3 u = frame.axisUWorld * frame.worldHalfWidth;
        Vector3 v = frame.axisVWorld * frame.worldHalfHeight;
        Vector3 c = frame.centerWorld + frame.normalWorld * 0.002f;

        Vector3 p0 = c - u - v;
        Vector3 p1 = c + u - v;
        Vector3 p2 = c + u + v;
        Vector3 p3 = c - u + v;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(c - u, c + u);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(c - v, c + v);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(c, c + frame.normalWorld * 0.25f);

        Vector3 worldGravity = GetWorldGravity();
        Vector3 gravityParallelWorld =
            worldGravity -
            Vector3.Dot(worldGravity, frame.normalWorld) * frame.normalWorld;

        if (gravityParallelWorld.sqrMagnitude > 0.000001f)
        {
            float arrowLength = Mathf.Clamp(
                gravityParallelWorld.magnitude * 0.08f,
                0.08f,
                0.80f
            );

            Vector3 arrowEnd =
                c + gravityParallelWorld.normalized * arrowLength;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(c, arrowEnd);
            Gizmos.DrawSphere(arrowEnd, 0.025f);
        }

        Gizmos.color = previousColor;
    }

    private void ReleaseTextures()
    {
        ReleaseTexture(ref colorA);
        ReleaseTexture(ref colorB);
        ReleaseTexture(ref thicknessA);
        ReleaseTexture(ref thicknessB);
        ReleaseTexture(ref wetnessA);
        ReleaseTexture(ref wetnessB);
        ReleaseTexture(ref ageA);
        ReleaseTexture(ref ageB);
        ReleaseTexture(ref velocityA);
        ReleaseTexture(ref velocityB);

        ReleaseTexture(ref accumR);
        ReleaseTexture(ref accumG);
        ReleaseTexture(ref accumB);
        ReleaseTexture(ref accumWeight);
        ReleaseTexture(ref accumThickness);
        ReleaseTexture(ref accumWetness);
        ReleaseTexture(ref accumVelocityX);
        ReleaseTexture(ref accumVelocityY);

        currentColor = null;
        currentThickness = null;
        currentWetness = null;
        currentAge = null;
        currentVelocity = null;

        nextColor = null;
        nextThickness = null;
        nextWetness = null;
        nextAge = null;
        nextVelocity = null;

        initialized = false;
    }

    private static void ReleaseTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();

        if (Application.isPlaying)
            Object.Destroy(texture);
        else
            Object.DestroyImmediate(texture);

        texture = null;
    }
}

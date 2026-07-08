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

    [Header("References")]
    [Tooltip("The canvas transform. Local X is width, Z is height, and Y points straight out.")]
    public Transform canvasSurface;

    [Tooltip("The actual mesh renderer taking the paint, usually a floating quad.")]
    public Renderer canvasRenderer;

    [Tooltip("The compute shader doing the heavy lifting for the splat physics.")]
    public ComputeShader canvasCompute;

    [Tooltip("The material using the Custom/PaintCanvasSurface shader.")]
    public Material canvasMaterial;

    [Header("Canvas")]
    public Vector2 canvasSizeMeters = new Vector2(2.0f, 2.0f);

    [Range(256, 2048)]
    public int textureResolution = 1024;

    public SurfaceType surfaceType = SurfaceType.Cloth;

    [Tooltip("How many sub-steps we run the ooze simulation per frame.")]
    [Range(1, 4)]
    public int surfaceSimulationSteps = 1;

    [Tooltip("Gravity pulling the paint drips down the canvas.")]
    public float gravity = 9.81f;

    [Header("Particle Deposition")]
    [Tooltip("Beefs up the thickness of each splat without spawning more particles.")]
    [Range(0.01f, 50.0f)]
    public float depositionThicknessScale = 8.0f;

    [Tooltip("Fattens the radius of the paint drops hitting the surface.")]
    [Range(0.25f, 8.0f)]
    public float depositionRadiusScale = 1.8f;

    [Tooltip("How much juice is added when fresh paint slaps the canvas.")]
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
    public float customDiffusion = 0.12f;

    [Range(0.0f, 1.0f)]
    public float customEvaporation = 0.04f;

    [Range(0.0f, 1.0f)]
    public float customAdhesion = 0.75f;

    [Header("Wood Grain")]
    public Vector2 woodGrainDirection = new Vector2(1.0f, 0.0f);

    [Range(0.0f, 1.0f)]
    public float woodGrainAnisotropy = 0.65f;

    [Header("Paint Appearance")]
    [Range(0.1f, 20.0f)]
    public float thicknessVisibility = 5.0f;

    [Range(0.0f, 5.0f)]
    public float reliefStrength = 1.25f;

    [Range(0.0f, 1.0f)]
    public float dryPaintSmoothness = 0.25f;

    [Range(0.0f, 1.0f)]
    public float wetPaintSmoothness = 0.92f;

    public RenderTexture ColorTexture => currentColor;
    public RenderTexture ThicknessTexture => currentThickness;
    public RenderTexture WetnessTexture => currentWetness;
    public RenderTexture VelocityTexture => currentVelocity;

    private RenderTexture colorA;
    private RenderTexture colorB;
    private RenderTexture thicknessA;
    private RenderTexture thicknessB;
    private RenderTexture wetnessA;
    private RenderTexture wetnessB;
    private RenderTexture velocityA;
    private RenderTexture velocityB;

    private RenderTexture currentColor;
    private RenderTexture currentThickness;
    private RenderTexture currentWetness;
    private RenderTexture currentVelocity;

    private RenderTexture nextColor;
    private RenderTexture nextThickness;
    private RenderTexture nextWetness;
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
        InitializeCanvas();
    }

    private void Start()
    {
        InitializeCanvas();
    }

    private void OnValidate()
    {
        textureResolution = Mathf.Clamp(textureResolution, 256, 2048);
        canvasSizeMeters.x = Mathf.Max(0.01f, canvasSizeMeters.x);
        canvasSizeMeters.y = Mathf.Max(0.01f, canvasSizeMeters.y);
        surfaceSimulationSteps = Mathf.Clamp(surfaceSimulationSteps, 1, 4);
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
        if (canvasCompute == null || canvasRenderer == null || canvasMaterial == null)
            return;

        if (initialized && builtResolution == textureResolution)
        {
            UpdateMaterialTextures();
            return;
        }

        ReleaseTextures();
        FindKernels();
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

    private void FindKernels()
    {
        kernelClearCanvas = canvasCompute.FindKernel("KernelClearCanvas");
        kernelClearAccum = canvasCompute.FindKernel("KernelClearAccum");
        kernelDeposit = canvasCompute.FindKernel("KernelDepositImpacts");
        kernelResolve = canvasCompute.FindKernel("KernelResolveDeposits");
        kernelSimulate = canvasCompute.FindKernel("KernelSimulateSurface");
    }

    private void CreateTextures()
    {
        colorA = CreateFloatTexture("Paint_Color_A", GraphicsFormat.R16G16B16A16_SFloat);
        colorB = CreateFloatTexture("Paint_Color_B", GraphicsFormat.R16G16B16A16_SFloat);

        thicknessA = CreateFloatTexture("Paint_Thickness_A", GraphicsFormat.R16_SFloat);
        thicknessB = CreateFloatTexture("Paint_Thickness_B", GraphicsFormat.R16_SFloat);

        wetnessA = CreateFloatTexture("Paint_Wetness_A", GraphicsFormat.R16_SFloat);
        wetnessB = CreateFloatTexture("Paint_Wetness_B", GraphicsFormat.R16_SFloat);

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
        currentVelocity = velocityA;

        nextColor = colorB;
        nextThickness = thicknessB;
        nextWetness = wetnessB;
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

        BindAccumulationTextures(kernelDeposit);
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
            velocityA
        );

        DispatchPixels(kernelClearCanvas);

        BindPersistentWriteTextures(
            kernelClearCanvas,
            colorB,
            thicknessB,
            wetnessB,
            velocityB
        );

        DispatchPixels(kernelClearCanvas);
        ClearAccumulationTextures();
    }

    private void ClearAccumulationTextures()
    {
        BindAccumulationTextures(kernelClearAccum);
        DispatchPixels(kernelClearAccum);
    }

    private void ResolveDeposits(float deltaTime)
    {
        SetCommonParameters(deltaTime);

        canvasCompute.SetTexture(kernelResolve, "_ColorRead", currentColor);
        canvasCompute.SetTexture(kernelResolve, "_ThicknessRead", currentThickness);
        canvasCompute.SetTexture(kernelResolve, "_WetnessRead", currentWetness);
        canvasCompute.SetTexture(kernelResolve, "_VelocityRead", currentVelocity);

        BindPersistentWriteTextures(
            kernelResolve,
            nextColor,
            nextThickness,
            nextWetness,
            nextVelocity
        );

        BindAccumulationTextures(kernelResolve);
        DispatchPixels(kernelResolve);
        SwapPersistentTextures();
    }

    private void SimulateSurface(float deltaTime)
    {
        SetCommonParameters(deltaTime);

        canvasCompute.SetTexture(kernelSimulate, "_ColorRead", currentColor);
        canvasCompute.SetTexture(kernelSimulate, "_ThicknessRead", currentThickness);
        canvasCompute.SetTexture(kernelSimulate, "_WetnessRead", currentWetness);
        canvasCompute.SetTexture(kernelSimulate, "_VelocityRead", currentVelocity);

        BindPersistentWriteTextures(
            kernelSimulate,
            nextColor,
            nextThickness,
            nextWetness,
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
        RenderTexture velocity
    )
    {
        canvasCompute.SetTexture(kernel, "_ColorWrite", color);
        canvasCompute.SetTexture(kernel, "_ThicknessWrite", thickness);
        canvasCompute.SetTexture(kernel, "_WetnessWrite", wetness);
        canvasCompute.SetTexture(kernel, "_VelocityWrite", velocity);
    }

    private void BindAccumulationTextures(int kernel)
    {
        canvasCompute.SetTexture(kernel, "_AccumR", accumR);
        canvasCompute.SetTexture(kernel, "_AccumG", accumG);
        canvasCompute.SetTexture(kernel, "_AccumB", accumB);
        canvasCompute.SetTexture(kernel, "_AccumWeight", accumWeight);
        canvasCompute.SetTexture(kernel, "_AccumThickness", accumThickness);
        canvasCompute.SetTexture(kernel, "_AccumWetness", accumWetness);
        canvasCompute.SetTexture(kernel, "_AccumVelocityX", accumVelocityX);
        canvasCompute.SetTexture(kernel, "_AccumVelocityY", accumVelocityY);
    }

    private void DispatchPixels(int kernel)
    {
        int groups = Mathf.CeilToInt(textureResolution / (float)PIXEL_THREADS);
        canvasCompute.Dispatch(kernel, groups, groups, 1);
    }

    private void SetCommonParameters(float deltaTime)
    {
        GetSurfaceValues(
            out float absorption,
            out float mobility,
            out float diffusion,
            out float evaporation,
            out float adhesion,
            out float grainAnisotropy
        );

        Vector2 grain = woodGrainDirection.sqrMagnitude > 0.0001f
            ? woodGrainDirection.normalized
            : Vector2.right;

        Vector2 gravityUv = Vector2.zero;

        if (canvasSurface != null)
        {
            Vector3 worldGravity = Vector3.down * Mathf.Abs(gravity);

            gravityUv.x =
                Vector3.Dot(worldGravity, canvasSurface.right.normalized) /
                Mathf.Max(0.01f, canvasSizeMeters.x);

            gravityUv.y =
                Vector3.Dot(worldGravity, canvasSurface.forward.normalized) /
                Mathf.Max(0.01f, canvasSizeMeters.y);
        }

        canvasCompute.SetInt("_TextureResolution", textureResolution);
        canvasCompute.SetFloat("_DeltaTime", deltaTime);
        canvasCompute.SetVector(
            "_CanvasSizeMeters",
            new Vector4(canvasSizeMeters.x, canvasSizeMeters.y, 0, 0)
        );

        canvasCompute.SetFloat("_DepositionThicknessScale", depositionThicknessScale);
        canvasCompute.SetFloat("_DepositionRadiusScale", depositionRadiusScale);
        canvasCompute.SetFloat("_DepositedWetness", depositedWetness);
        canvasCompute.SetFloat("_VelocityStretch", velocityStretch);

        canvasCompute.SetFloat("_SurfaceAbsorption", absorption);
        canvasCompute.SetFloat("_SurfaceMobility", mobility);
        canvasCompute.SetFloat("_SurfaceDiffusion", diffusion);
        canvasCompute.SetFloat("_SurfaceEvaporation", evaporation);
        canvasCompute.SetFloat("_SurfaceAdhesion", adhesion);

        canvasCompute.SetVector(
            "_SurfaceGravityUV",
            new Vector4(gravityUv.x, gravityUv.y, 0, 0)
        );

        canvasCompute.SetVector(
            "_WoodGrainDirection",
            new Vector4(grain.x, grain.y, 0, 0)
        );

        canvasCompute.SetFloat("_WoodGrainAnisotropy", grainAnisotropy);
    }

    private void GetSurfaceValues(
        out float absorption,
        out float mobility,
        out float diffusion,
        out float evaporation,
        out float adhesion,
        out float grainAnisotropy
    )
    {
        switch (surfaceType)
        {
            case SurfaceType.Cloth:
                absorption = 0.88f;
                mobility = 0.035f;
                diffusion = 0.24f;
                evaporation = 0.065f;
                adhesion = 0.97f;
                grainAnisotropy = 0.10f;
                break;

            case SurfaceType.Wood:
                absorption = 0.48f;
                mobility = 0.14f;
                diffusion = 0.13f;
                evaporation = 0.035f;
                adhesion = 0.80f;
                grainAnisotropy = woodGrainAnisotropy;
                break;

            case SurfaceType.Glass:
                absorption = 0.01f;
                mobility = 0.92f;
                diffusion = 0.025f;
                evaporation = 0.012f;
                adhesion = 0.18f;
                grainAnisotropy = 0.0f;
                break;

            default:
                absorption = customAbsorption;
                mobility = customMobility;
                diffusion = customDiffusion;
                evaporation = customEvaporation;
                adhesion = customAdhesion;
                grainAnisotropy = woodGrainAnisotropy;
                break;
        }
    }

    private void SwapPersistentTextures()
    {
        (currentColor, nextColor) = (nextColor, currentColor);
        (currentThickness, nextThickness) = (nextThickness, currentThickness);
        (currentWetness, nextWetness) = (nextWetness, currentWetness);
        (currentVelocity, nextVelocity) = (nextVelocity, currentVelocity);
    }

    private void UpdateMaterialTextures()
    {
        if (canvasMaterial == null)
            return;

        canvasMaterial.SetTexture("_PaintColorMap", currentColor);
        canvasMaterial.SetTexture("_PaintThicknessMap", currentThickness);
        canvasMaterial.SetTexture("_PaintWetnessMap", currentWetness);
        canvasMaterial.SetFloat("_ThicknessVisibility", thicknessVisibility);
        canvasMaterial.SetFloat("_ReliefStrength", reliefStrength);
        canvasMaterial.SetFloat("_DryPaintSmoothness", dryPaintSmoothness);
        canvasMaterial.SetFloat("_WetPaintSmoothness", wetPaintSmoothness);
        canvasMaterial.SetVector(
            "_PaintTexelSize",
            new Vector4(
                1.0f / textureResolution,
                1.0f / textureResolution,
                textureResolution,
                textureResolution
            )
        );
    }

    private void ReleaseTextures()
    {
        ReleaseTexture(ref colorA);
        ReleaseTexture(ref colorB);
        ReleaseTexture(ref thicknessA);
        ReleaseTexture(ref thicknessB);
        ReleaseTexture(ref wetnessA);
        ReleaseTexture(ref wetnessB);
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
        currentVelocity = null;

        nextColor = null;
        nextThickness = null;
        nextWetness = null;
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
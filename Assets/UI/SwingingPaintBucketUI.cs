using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Collections;
using System.IO;

[ExecuteInEditMode]
public class SwingingPaintBucketUI : MonoBehaviour
{

    [Header("References")]
    public SwingingBucketStage01 bucketStage01;
    public PaintEmitterStage02 paintEmitter;
    public GpuDensityPaintFluidStage03 fluidStage;
    public PaintCanvasStage04 canvasStage;

    [Header("Persistence And Results")]
    public ExperimentSaveResultsController experimentResultsController;

    [Header("Canvas Visual References")]
    public Transform boardVisual;
    public Transform paintSurfaceQuad;

    [Header("Canvas Tilt")]
    public PaintBoardTiltController boardTiltController;

    [Header("Final Image Export")]
    public Camera boardSaveCamera;

    private ExperimentState experimentState =
        ExperimentState.Ready;

    private float experimentElapsedTime;

    public float ExperimentElapsedTime =>
        experimentElapsedTime;


    [Range(256, 4096)]
    public int saveImageWidth = 2048;

    [Range(256, 4096)]
    public int saveImageHeight = 2048;

    private Vector2 initialCanvasSize = Vector2.one;
    private Vector3 initialBoardVisualScale = Vector3.one;
    private Vector3 initialPaintSurfaceQuadScale = Vector3.one;


    // ========== ألوان عصرية (Modern Dark Slate) ==========
    private readonly Color colBg = new Color(0.118f, 0.161f, 0.231f, 0.96f); // #1E293B
    private readonly Color colBgDarker = new Color(0.071f, 0.094f, 0.129f, 1.00f); // #121929
    private readonly Color colAccent = new Color(0.231f, 0.510f, 0.965f);          // #3B82F6
    private readonly Color colGreen = new Color(0.063f, 0.729f, 0.506f);          // #10B981
    private readonly Color colRed = new Color(0.937f, 0.267f, 0.267f);          // #EF4444
    private readonly Color colOrange = new Color(0.957f, 0.620f, 0.043f);          // #F59E0B
    private readonly Color colTextPrimary = new Color(0.969f, 0.980f, 0.988f);          // #F8FAFC
    private readonly Color colTextSecondary = new Color(0.580f, 0.639f, 0.718f);          // #94A3B8
    private readonly Color colSliderBg = new Color(0.204f, 0.255f, 0.333f);          // #334155

    private RectTransform leftPanel;
    private RectTransform rightPanel;
    private RectTransform scrollContent;
    private readonly Dictionary<string, TextMeshProUGUI> liveTexts = new Dictionary<string, TextMeshProUGUI>();
    private readonly string[] setupOnlySliderKeys =
    {
    "bucketMass",
    "paintVolume",
    "paintDensity",
    "bucketBottomRadius",
    "bucketTopRadius",
    "bucketHeight",

    "ropeLength",
    "ropeStiffness",
    "startAngle",
    "startDirection",
    "initialSpeedX",
    "initialSpeedZ",

    "particleCount",
    "gridResolution",
    "canvasWidth",
    "canvasHeight"
};
    private static readonly string[] surfaceTypeNames =
{
    "Cloth",
    "Wood",
    "Glass",
    "Custom"
};

    private int currentSurfaceTypeIndex;
    private TextMeshProUGUI surfaceTypeButtonText;

    private readonly Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
    private readonly Dictionary<string, TextMeshProUGUI> valueLabels = new Dictionary<string, TextMeshProUGUI>();


    private float fpsAccumulator = 0f;
    private int fpsFrames = 0;
    private int pendingParticleCount;
    private int pendingGridResolution;
    private float configuredPaintVolumeLiters;
    // private bool isSavingImage;
    private enum ExperimentState
    {
        Ready,
        Running,
        Paused,
        Stopped
    }
    private readonly Dictionary<string, Image>
    sliderFillImages =
        new Dictionary<string, Image>();

    private readonly Dictionary<string, Image>
        sliderHandleImages =
            new Dictionary<string, Image>();

    private readonly Dictionary<string, TextMeshProUGUI>
        sliderLabelTexts =
            new Dictionary<string, TextMeshProUGUI>();

    private readonly Color colLockedSlider =
        new Color(
            0.36f,
            0.40f,
            0.47f,
            1.0f
        );

    private readonly Color colLockedText =
        new Color(
            0.47f,
            0.51f,
            0.58f,
            1.0f
        );


    void Awake()
    {
        CacheCanvasVisualSettings();

        if (Application.isPlaying)
        {
            BuildUI();
            PrepareReadyState();
            SetSetupControlsInteractable(true);
        }

    }
    void CacheCanvasVisualSettings()
    {
        if (canvasStage != null)
        {
            initialCanvasSize = canvasStage.canvasSizeMeters;

            initialCanvasSize.x =
                Mathf.Max(0.01f, initialCanvasSize.x);

            initialCanvasSize.y =
                Mathf.Max(0.01f, initialCanvasSize.y);
        }

        if (boardVisual != null)
        {
            initialBoardVisualScale =
                boardVisual.localScale;
        }

        if (paintSurfaceQuad != null)
        {
            initialPaintSurfaceQuadScale =
                paintSurfaceQuad.localScale;
        }
    }
    void ApplyCanvasSize(float width, float height)
    {
        width = Mathf.Max(0.1f, width);
        height = Mathf.Max(0.1f, height);

        Vector2 size =
            new Vector2(width, height);

        if (canvasStage != null)
        {
            canvasStage.canvasSizeMeters =
                size;
        }

        float widthRatio =
            width /
            Mathf.Max(0.01f, initialCanvasSize.x);

        float heightRatio =
            height /
            Mathf.Max(0.01f, initialCanvasSize.y);

        if (boardVisual != null)
        {
            boardVisual.localScale =
                new Vector3(
                    initialBoardVisualScale.x *
                    widthRatio,

                    initialBoardVisualScale.y,

                    initialBoardVisualScale.z *
                    heightRatio
                );
        }

        if (paintSurfaceQuad != null)
        {
            paintSurfaceQuad.localScale =
                new Vector3(
                    initialPaintSurfaceQuadScale.x *
                    widthRatio,

                    initialPaintSurfaceQuadScale.y,

                    initialPaintSurfaceQuadScale.z *
                    heightRatio
                );
        }
    }
    void BuildUI()
    {
        // ---------- Canvas ----------
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = gameObject.GetComponent<GraphicRaycaster>();
        if (raycaster == null) raycaster = gameObject.AddComponent<GraphicRaycaster>();

        // ========== LEFT PANEL (الجانب الأيسر) ==========
        leftPanel = CreatePanel("LeftPanel", transform, colBg);
        leftPanel.anchorMin = new Vector2(0, 0);
        leftPanel.anchorMax = new Vector2(0, 1);
        leftPanel.pivot = new Vector2(0, 1);
        leftPanel.anchoredPosition = Vector2.zero;
        leftPanel.sizeDelta = new Vector2(380, 0);

        VerticalLayoutGroup leftVLG = leftPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        leftVLG.padding = new RectOffset(16, 16, 16, 16);
        leftVLG.spacing = 12;
        leftVLG.childAlignment = TextAnchor.UpperLeft;
        leftVLG.childControlWidth = true;
        leftVLG.childControlHeight = true;
        leftVLG.childForceExpandWidth = true;
        leftVLG.childForceExpandHeight = false;

        // Title
        CreateTitle(leftPanel, "CONTROL PANEL", "Swinging Paint Bucket");
        leftPanel.GetChild(leftPanel.childCount - 1).SetAsFirstSibling();


        GameObject scrollObj = new GameObject(
    "ScrollView",
    typeof(RectTransform),
    typeof(ScrollRect),
    typeof(LayoutElement)
);
        scrollObj.transform.SetParent(
            leftPanel,
            false
        );

        scrollObj.transform.SetSiblingIndex(1);

        RectTransform scrollRT =
            scrollObj.GetComponent<RectTransform>();

        scrollRT.localScale = Vector3.one;

        LayoutElement scrollLayout =
            scrollObj.GetComponent<LayoutElement>();

        scrollLayout.minHeight = 100.0f;
        scrollLayout.preferredHeight = 400.0f;
        scrollLayout.flexibleHeight = 1.0f;

        ScrollRect scroll =
            scrollObj.GetComponent<ScrollRect>();

        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.scrollSensitivity = 25.0f;

        GameObject viewport = new GameObject(
            "Viewport",
            typeof(RectTransform),
            typeof(RectMask2D)
        );

        viewport.transform.SetParent(scrollObj.transform, false);

        RectTransform viewportRT =
            viewport.GetComponent<RectTransform>();

        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewportRT.localScale = Vector3.one;

        GameObject contentObj = new GameObject(
            "Content",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );

        contentObj.transform.SetParent(viewport.transform, false);

        scrollContent =
            contentObj.GetComponent<RectTransform>();

        scrollContent.anchorMin = new Vector2(0.0f, 1.0f);
        scrollContent.anchorMax = new Vector2(1.0f, 1.0f);
        scrollContent.pivot = new Vector2(0.5f, 1.0f);
        scrollContent.anchoredPosition = Vector2.zero;
        scrollContent.sizeDelta = Vector2.zero;
        scrollContent.localScale = Vector3.one;

        VerticalLayoutGroup contentLayout =
            contentObj.GetComponent<VerticalLayoutGroup>();

        contentLayout.padding =
            new RectOffset(0, 0, 4, 12);

        contentLayout.spacing = 8.0f;
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter contentFitter =
            contentObj.GetComponent<ContentSizeFitter>();

        contentFitter.horizontalFit =
            ContentSizeFitter.FitMode.Unconstrained;

        contentFitter.verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRT;
        scroll.content = scrollContent;

        // ---------- SECTIONS ----------
        // 1. Bucket & Paint
        CreateSectionHeader(scrollContent, "▼ 1. Bucket & Paint");
        //CreateSliderRow(scrollContent, "Bucket Mass", "bucketMass", 0.5f, 20f, 2f, "F2", " kg");

        //CreateSliderRow(scrollContent, "Paint Volume", "paintVolume", 0f, 10f, 5f, "F2", " L");
        //CreateSliderRow(scrollContent, "Hole Diameter", "holeDiameter", 0f, 20f, 0f, "F1", " mm");
        //CreateSliderRow(scrollContent, "Viscosity", "viscosity", 0f, 1f, 0.45f, "F2", "");
        //CreateColorRow(scrollContent, "Paint Color");
        CreateSliderRow(
    scrollContent,
    "Bucket Dry Mass",
    "bucketMass",
    0.5f,
    20.0f,
    2.0f,
    "F2",
    " kg"
);

        CreateSliderRow(
            scrollContent,
            "Paint Volume",
            "paintVolume",
            0.0f,
            60.0f,
            5.0f,
            "F2",
            " L"
        );

        CreateSliderRow(
            scrollContent,
            "Paint Density",
            "paintDensity",
            0.5f,
            2.5f,
            1.2f,
            "F2",
            " kg/L"
        );

        CreateSliderRow(
            scrollContent,
            "Bottom Radius",
            "bucketBottomRadius",
            0.05f,
            0.5f,
            0.165f,
            "F3",
            " m"
        );

        CreateSliderRow(
            scrollContent,
            "Top Radius",
            "bucketTopRadius",
            0.05f,
            0.5f,
            0.21f,
            "F3",
            " m"
        );

        CreateSliderRow(
            scrollContent,
            "Bucket Height",
            "bucketHeight",
            0.1f,
            1.0f,
            0.5f,
            "F3",
            " m"
        );

        CreateSliderRow(
            scrollContent,
            "Hole Diameter",
            "holeDiameter",
            0.0f,
            30.0f,
            5.0f,
            "F1",
            " mm"
        );

        CreateSliderRow(
            scrollContent,
            "Viscosity",
            "viscosity",
            0.0f,
            1.0f,
            0.62f,
            "F2",
            ""
        );

        CreateToggleRow(
            scrollContent,
            "Allow Paint Drain",
            "allowDrain",
            fluidStage != null && fluidStage.allowDrainFromHole
        );

        CreateColorRow(
            scrollContent,
            "Paint Color"
        );

        // 2. Suspension & Motion
        CreateSectionHeader(scrollContent, "▼ 2. Suspension & Motion");
        CreateSliderRow(scrollContent, "Rope Length", "ropeLength", 0.5f, 5f, 3f, "F2", " m");
        CreateSliderRow(
    scrollContent,
    "Rope Stiffness",
    "ropeStiffness",
    300.0f,
    2500.0f,
    bucketStage01 != null
        ? bucketStage01.ropeSpringConstant
        : 650.0f,
    "F0",
    ""
);
        CreateSliderRow(
    scrollContent,
    "Start Angle",
    "startAngle",
    0.0f,
    85.0f,
    35.0f,
    "F1",
    "°"
);
        CreateSliderRow(
    scrollContent,
    "Start Direction",
    "startDirection",
    0.0f,
    360.0f,
    bucketStage01 != null
        ? bucketStage01.startDirectionDegrees
        : 0.0f,
    "F0",
    "°"
);

        CreateSliderRow(
            scrollContent,
            "Initial Speed X",
            "initialSpeedX",
            -3.0f,
            3.0f,
            bucketStage01 != null
                ? bucketStage01.initialHandleVelocity.x
                : 0.0f,
            "F2",
            " m/s"
        );

        CreateSliderRow(
            scrollContent,
            "Initial Speed Z",
            "initialSpeedZ",
            -3.0f,
            3.0f,
            bucketStage01 != null
                ? bucketStage01.initialHandleVelocity.z
                : 0.0f,
            "F2",
            " m/s"
        );
        CreateSliderRow(scrollContent, "Gravity", "gravity", 1f, 20f, 9.81f, "F2", "");
        CreateSliderRow(scrollContent, "Air Drag", "airDrag", 0f, 1f, 0.08f, "F2", "");

        // 3. Particle Simulation
        CreateSectionHeader(scrollContent, "▼ 3. Particle Simulation");
        CreateSliderRow(scrollContent, "Particle Count", "particleCount", 50000f, 2000000.0f, 250000.0f, "F0", "");
        CreateSliderRow(scrollContent, "Grid Resolution", "gridResolution", 32f, 128f, 96f, "F0", "");
        CreateSliderRow(scrollContent, "Pressure Strength", "pressureStrength", 0f, 1f, 0.22f, "F2", "");
        CreateSliderRow(scrollContent, "Wall Friction", "wallFriction", 0f, 1f, 0.86f, "F2", "");
        CreateSliderRow(scrollContent, "Vortex Strength", "vortexStrength", 0f, 20f, 1.5f, "F2", "");

        // 4. Canvas Properties
        CreateSectionHeader(scrollContent, "▼ 4. Canvas Properties");
        CreateSliderRow(scrollContent, "Canvas Width", "canvasWidth", 0.5f, 5f, 2f, "F2", " m");
        CreateSliderRow(scrollContent, "Canvas Height", "canvasHeight", 0.5f, 5f, 2f, "F2", " m");
        CreateSliderRow(
    scrollContent,
    "Canvas Tilt X",
    "canvasTiltX",
    -60.0f,
    60.0f,
    boardTiltController != null
        ? boardTiltController.TiltX
        : 0.0f,
    "F1",
    "°"
);

        CreateSliderRow(
            scrollContent,
            "Canvas Tilt Z",
            "canvasTiltZ",
            -60.0f,
            60.0f,
            boardTiltController != null
                ? boardTiltController.TiltZ
                : 0.0f,
            "F1",
            "°"
        );
        // CreateDropdownRow(scrollContent, "Surface Type", new List<string> { "Cloth", "Wood", "Glass", "Custom" });

        CreateSurfaceTypeRow(
    scrollContent,
    "Surface Type"
);

        // Buttons (أسفل اليسار)
        CreateButtonsPanel(leftPanel);
        leftPanel.GetChild(leftPanel.childCount - 1).SetAsLastSibling();
        // ========== RIGHT PANEL (معلومات حية - جانب أيمن صغير) ==========
        rightPanel = CreatePanel("RightPanel", transform, colBg);
        rightPanel.anchorMin = new Vector2(1, 1);
        rightPanel.anchorMax = new Vector2(1, 1);
        rightPanel.pivot = new Vector2(1, 1);
        rightPanel.anchoredPosition = new Vector2(-16, -16);
        rightPanel.sizeDelta = new Vector2(250, 380);

        VerticalLayoutGroup rightVLG = rightPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        rightVLG.padding = new RectOffset(12, 12, 12, 12);
        rightVLG.spacing = 6;
        rightVLG.childAlignment = TextAnchor.UpperLeft;
        rightVLG.childControlWidth = true;
        rightVLG.childControlHeight = false;
        rightVLG.childForceExpandWidth = true;
        rightVLG.childForceExpandHeight = false;

        CreateLiveInfoHeader(rightPanel, "LIVE INFO");
        CreateLiveInfoRow(rightPanel, "FPS", "fps");
        CreateLiveInfoRow(
    rightPanel,
    "Total Mass (kg)",
    "totalMass"
);

        CreateLiveInfoRow(
            rightPanel,
            "Exit Speed (m/s)",
            "exitSpeed"
        );

        CreateLiveInfoRow(
            rightPanel,
            "Hole State",
            "holeState"
        );
        CreateLiveInfoRow(rightPanel, "Particles", "particles");
        CreateLiveInfoRow(rightPanel, "Paint Vol (L)", "paintVol");
        CreateLiveInfoRow(rightPanel, "Flow (L/s)", "flow");
        CreateLiveInfoRow(rightPanel, "Tension (N)", "tension");
        CreateLiveInfoRow(rightPanel, "Rope Len (m)", "ropeLen");
        CreateLiveInfoRow(rightPanel, "Bucket Vel", "bucketVel");
        CreateLiveInfoRow(rightPanel, "Sim Time", "simTime");

        // Bind
        BindInitialValues();
        Canvas.ForceUpdateCanvases();

        LayoutRebuilder.ForceRebuildLayoutImmediate(
            scrollContent
        );

        LayoutRebuilder.ForceRebuildLayoutImmediate(
            leftPanel
        );

        Canvas.ForceUpdateCanvases();
    }

    // ==================== Helpers ====================

    RectTransform CreatePanel(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        return go.GetComponent<RectTransform>();
    }

    void CreateTitle(
        Transform parent,
        string main,
        string sub
    )
    {
        GameObject titleObject = new GameObject(
            "Title",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(VerticalLayoutGroup)
        );

        titleObject.transform.SetParent(parent, false);

        LayoutElement titleLayout =
            titleObject.GetComponent<LayoutElement>();

        titleLayout.minHeight = 82.0f;
        titleLayout.preferredHeight = 82.0f;
        titleLayout.flexibleHeight = 0.0f;

        VerticalLayoutGroup layout =
            titleObject.GetComponent<VerticalLayoutGroup>();

        layout.padding = new RectOffset(0, 0, 2, 4);
        layout.spacing = 4.0f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI mainText = CreateText(
            titleObject.transform,
            main,
            22,
            colTextPrimary,
            true
        );

        mainText.textWrappingMode = TextWrappingModes.NoWrap;
        mainText.alignment = TextAlignmentOptions.Left;

        LayoutElement mainLayout =
            mainText.gameObject.AddComponent<LayoutElement>();

        mainLayout.preferredHeight = 36.0f;

        TextMeshProUGUI subtitleText = CreateText(
            titleObject.transform,
            sub,
            12,
            colTextSecondary,
            false
        );

        mainText.textWrappingMode = TextWrappingModes.NoWrap;
        subtitleText.textWrappingMode = TextWrappingModes.NoWrap;

        LayoutElement subtitleLayout =
            subtitleText.gameObject.AddComponent<LayoutElement>();

        subtitleLayout.preferredHeight = 24.0f;
    }
    void CreateSectionHeader(
    Transform parent,
    string text
)
    {
        GameObject headerObject = new GameObject(
            "Header",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(Image)
        );

        headerObject.transform.SetParent(parent, false);

        LayoutElement headerLayout =
            headerObject.GetComponent<LayoutElement>();

        headerLayout.minHeight = 38.0f;
        headerLayout.preferredHeight = 38.0f;
        headerLayout.flexibleHeight = 0.0f;

        Image background =
            headerObject.GetComponent<Image>();

        background.color = new Color(
            colBgDarker.r,
            colBgDarker.g,
            colBgDarker.b,
            0.55f
        );

        TextMeshProUGUI headerText = CreateText(
            headerObject.transform,
            text,
            15,
            colAccent,
            true
        );

        RectTransform textRect =
            headerText.GetComponent<RectTransform>();

        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8.0f, 0.0f);
        textRect.offsetMax = new Vector2(-8.0f, 0.0f);

        headerText.alignment =
            TextAlignmentOptions.MidlineLeft;

        headerText.textWrappingMode = TextWrappingModes.NoWrap;
        headerText.overflowMode = TextOverflowModes.Ellipsis;
    }

    void CreateSliderRow(Transform parent, string label, string key, float min, float max, float def, string fmt, string suffix)
    {
        GameObject row = new GameObject("Row_" + key, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rt = row.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 48);

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 4, 4);
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Label
        TextMeshProUGUI labelText = CreateText(row.transform, label, 12, colTextSecondary, false);
        RectTransform labelRT = labelText.GetComponent<RectTransform>();
        labelRT.sizeDelta = new Vector2(110, 28);
        labelText.textWrappingMode = TextWrappingModes.NoWrap;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        // Slider
        GameObject sliderObj = new GameObject("Slider_" + key, typeof(Slider));
        sliderObj.transform.SetParent(row.transform, false);
        RectTransform sliderRT = sliderObj.GetComponent<RectTransform>();
        sliderRT.sizeDelta = new Vector2(145, 18);

        Slider slider = sliderObj.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = def;
        if (
            key == "particleCount" ||
            key == "gridResolution"
        )
        {
            slider.wholeNumbers = true;
        }
        // Background
        GameObject bg = new GameObject("Background", typeof(Image));
        bg.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.sizeDelta = Vector2.zero;
        bg.GetComponent<Image>().color = colSliderBg;

        // Fill Area
        GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = Vector2.zero; faRT.anchorMax = Vector2.one; faRT.sizeDelta = Vector2.zero;

        GameObject fill = new GameObject("Fill", typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fRT = fill.GetComponent<RectTransform>();
        fRT.anchorMin = Vector2.zero; fRT.anchorMax = Vector2.one; fRT.sizeDelta = Vector2.zero;
        Image fillImage =
    fill.GetComponent<Image>();

        fillImage.color =
            colAccent;

        // Handle
        GameObject handleArea = new GameObject("HandleSlideArea", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one; haRT.sizeDelta = Vector2.zero;

        GameObject handle = new GameObject("Handle", typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform hRT = handle.GetComponent<RectTransform>();
        hRT.sizeDelta = new Vector2(12, 12);
        hRT.anchorMin = new Vector2(0.5f, 0.5f); hRT.anchorMax = new Vector2(0.5f, 0.5f);
        hRT.pivot = new Vector2(0.5f, 0.5f);
        hRT.anchoredPosition = Vector2.zero;
        Image handleImage =
     handle.GetComponent<Image>();

        handleImage.color =
            Color.white;

        slider.fillRect = fRT;
        slider.handleRect = hRT;

        // Value Box
        GameObject valueBox = new GameObject("ValueBox", typeof(Image));
        valueBox.transform.SetParent(row.transform, false);
        RectTransform vbRT = valueBox.GetComponent<RectTransform>();
        vbRT.sizeDelta = new Vector2(58, 22);
        valueBox.GetComponent<Image>().color = colBgDarker;

        TextMeshProUGUI valueText = CreateText(valueBox.transform, def.ToString(fmt), 11, colAccent, false);
        valueText.alignment = TextAlignmentOptions.Center;

        sliders[key] = slider;
        valueLabels[key] = valueText;
        sliderFillImages[key] =
    fillImage;

        sliderHandleImages[key] =
            handleImage;

        sliderLabelTexts[key] =
            labelText;
        slider.onValueChanged.AddListener((v) => {
            valueText.text = v.ToString(fmt) + suffix;
            OnSliderChanged(key, v);
        });
    }
    void PrepareReadyState()
    {
        Time.timeScale = 1.0f;

        experimentState =
            ExperimentState.Ready;

        experimentElapsedTime = 0.0f;

        if (bucketStage01 != null)
        {
            bucketStage01
                .Stage01_SetSimulationRunning(false);
        }

        if (fluidStage != null)
        {
            fluidStage.simulateFluid = false;
        }
    }
    void CreateSurfaceTypeRow(
    Transform parent,
    string label
)
    {
        GameObject row = new GameObject(
            "Row_SurfaceType",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup)
        );

        row.transform.SetParent(parent, false);

        LayoutElement rowLayout =
            row.GetComponent<LayoutElement>();

        rowLayout.minHeight = 42.0f;
        rowLayout.preferredHeight = 42.0f;

        HorizontalLayoutGroup layout =
            row.GetComponent<HorizontalLayoutGroup>();

        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8.0f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI labelText = CreateText(
            row.transform,
            label,
            12,
            colTextSecondary,
            false
        );

        LayoutElement labelLayout =
            labelText.gameObject.AddComponent<LayoutElement>();

        labelLayout.preferredWidth = 105.0f;



        currentSurfaceTypeIndex = 0;

        if (canvasStage != null)
        {
            currentSurfaceTypeIndex =
    (int)canvasStage.surfaceType;
        }

        currentSurfaceTypeIndex = Mathf.Clamp(
    currentSurfaceTypeIndex,
    0,
    surfaceTypeNames.Length - 1
);

        GameObject buttonObject = new GameObject(
            "SurfaceTypeButton",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(Image),
            typeof(Button)
        );

        buttonObject.transform.SetParent(row.transform, false);

        LayoutElement buttonLayout =
            buttonObject.GetComponent<LayoutElement>();

        buttonLayout.preferredWidth = 145.0f;
        buttonLayout.preferredHeight = 30.0f;

        Image buttonImage =
            buttonObject.GetComponent<Image>();

        buttonImage.color = colBgDarker;

        Button button =
            buttonObject.GetComponent<Button>();

        button.targetGraphic = buttonImage;

        surfaceTypeButtonText = CreateText(
            buttonObject.transform,
            surfaceTypeNames[currentSurfaceTypeIndex],
            12,
            colTextPrimary,
            true
        );

        RectTransform buttonTextRect =
            surfaceTypeButtonText.GetComponent<RectTransform>();

        buttonTextRect.anchorMin =
            Vector2.zero;

        buttonTextRect.anchorMax =
            Vector2.one;

        buttonTextRect.offsetMin =
            Vector2.zero;

        buttonTextRect.offsetMax =
            Vector2.zero;

        surfaceTypeButtonText.alignment =
            TextAlignmentOptions.Center;

        button.onClick.AddListener(() =>
        {
            currentSurfaceTypeIndex++;

            if (
                currentSurfaceTypeIndex >=
                surfaceTypeNames.Length
            )
            {
                currentSurfaceTypeIndex = 0;
            }

            ApplyCurrentSurfaceType();
        });
    }
    void ApplyCurrentSurfaceType()
    {
        currentSurfaceTypeIndex = Mathf.Clamp(
            currentSurfaceTypeIndex,
            0,
            surfaceTypeNames.Length - 1
        );

        if (surfaceTypeButtonText != null)
        {
            surfaceTypeButtonText.text =
                surfaceTypeNames[
                    currentSurfaceTypeIndex
                ];
        }

        if (canvasStage != null)
        {
            canvasStage.surfaceType =
                (PaintCanvasStage04.SurfaceType)
                currentSurfaceTypeIndex;
        }
    }

    void RefreshSurfaceTypeControl()
    {
        if (canvasStage != null)
        {
            currentSurfaceTypeIndex =
                (int)canvasStage.surfaceType;
        }

        ApplyCurrentSurfaceType();
    }
    void CreateToggleRow(
    Transform parent,
    string label,
    string key,
    bool defaultValue
)
    {
        GameObject row = new GameObject(
            "Row_" + key,
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup)
        );

        row.transform.SetParent(parent, false);

        LayoutElement rowLayout =
            row.GetComponent<LayoutElement>();

        rowLayout.minHeight = 42.0f;
        rowLayout.preferredHeight = 42.0f;

        HorizontalLayoutGroup layout =
            row.GetComponent<HorizontalLayoutGroup>();

        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8.0f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI labelText = CreateText(
            row.transform,
            label,
            12,
            colTextSecondary,
            false
        );

        LayoutElement labelLayout =
            labelText.gameObject.AddComponent<LayoutElement>();

        labelLayout.minWidth = 180.0f;
        labelLayout.preferredWidth = 180.0f;

        GameObject toggleObject = new GameObject(
            "Toggle_" + key,
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(Toggle)
        );

        toggleObject.transform.SetParent(
            row.transform,
            false
        );

        LayoutElement toggleLayout =
            toggleObject.GetComponent<LayoutElement>();

        toggleLayout.minWidth = 30.0f;
        toggleLayout.preferredWidth = 30.0f;
        toggleLayout.minHeight = 28.0f;
        toggleLayout.preferredHeight = 28.0f;

        Toggle toggle =
            toggleObject.GetComponent<Toggle>();

        GameObject backgroundObject = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(Image)
        );

        backgroundObject.transform.SetParent(
            toggleObject.transform,
            false
        );

        RectTransform backgroundRect =
            backgroundObject.GetComponent<RectTransform>();

        backgroundRect.anchorMin =
            new Vector2(0.5f, 0.5f);

        backgroundRect.anchorMax =
            new Vector2(0.5f, 0.5f);

        backgroundRect.pivot =
            new Vector2(0.5f, 0.5f);

        backgroundRect.sizeDelta =
            new Vector2(24.0f, 24.0f);

        Image backgroundImage =
            backgroundObject.GetComponent<Image>();

        backgroundImage.color = colSliderBg;

        GameObject checkmarkObject = new GameObject(
            "Checkmark",
            typeof(RectTransform),
            typeof(Image)
        );

        checkmarkObject.transform.SetParent(
            backgroundObject.transform,
            false
        );

        RectTransform checkmarkRect =
            checkmarkObject.GetComponent<RectTransform>();

        checkmarkRect.anchorMin =
            new Vector2(0.5f, 0.5f);

        checkmarkRect.anchorMax =
            new Vector2(0.5f, 0.5f);

        checkmarkRect.pivot =
            new Vector2(0.5f, 0.5f);

        checkmarkRect.sizeDelta =
            new Vector2(16.0f, 16.0f);

        Image checkmarkImage =
            checkmarkObject.GetComponent<Image>();

        checkmarkImage.color = colGreen;

        toggle.targetGraphic =
            backgroundImage;

        toggle.graphic =
            checkmarkImage;

        toggle.isOn =
            defaultValue;

        toggle.onValueChanged.AddListener(
            value =>
            {
                if (
                    key == "allowDrain" &&
                    fluidStage != null
                )
                {
                    fluidStage.allowDrainFromHole =
                        value;
                }
            }
        );
    }
    void CreateColorRow(
        Transform parent,
        string label
    )
    {
        GameObject row = new GameObject(
            "Row_Color",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup)
        );

        row.transform.SetParent(parent, false);

        LayoutElement rowLayout =
            row.GetComponent<LayoutElement>();

        rowLayout.minHeight = 66.0f;
        rowLayout.preferredHeight = 66.0f;

        HorizontalLayoutGroup layout =
            row.GetComponent<HorizontalLayoutGroup>();

        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8.0f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI labelText = CreateText(
            row.transform,
            label,
            12,
            colTextSecondary,
            false
        );

        LayoutElement labelLayout =
            labelText.gameObject.AddComponent<LayoutElement>();

        labelLayout.minWidth = 96.0f;
        labelLayout.preferredWidth = 96.0f;

        GameObject colorsContainer = new GameObject(
            "ColorsContainer",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(GridLayoutGroup)
        );

        colorsContainer.transform.SetParent(row.transform, false);

        LayoutElement colorsLayout =
            colorsContainer.GetComponent<LayoutElement>();

        colorsLayout.minWidth = 130.0f;
        colorsLayout.preferredWidth = 130.0f;
        colorsLayout.minHeight = 54.0f;
        colorsLayout.preferredHeight = 54.0f;

        GridLayoutGroup grid =
            colorsContainer.GetComponent<GridLayoutGroup>();

        grid.cellSize = new Vector2(28.0f, 24.0f);
        grid.spacing = new Vector2(5.0f, 5.0f);
        grid.constraint =
            GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.MiddleLeft;

        Color[] availableColors =
        {
        Color.red,
        new Color(1.0f, 0.5f, 0.0f, 1.0f),
        Color.yellow,
        Color.green,
        Color.cyan,
        Color.blue,
        new Color(0.65f, 0.15f, 0.9f, 1.0f),
        Color.white
    };

        for (int i = 0; i < availableColors.Length; i++)
        {
            Color selectedColor = availableColors[i];

            GameObject colorButtonObject = new GameObject(
                "ColorButton_" + i,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button)
            );

            colorButtonObject.transform.SetParent(
                colorsContainer.transform,
                false
            );

            Image colorImage =
                colorButtonObject.GetComponent<Image>();

            colorImage.color = selectedColor;

            Button colorButton =
                colorButtonObject.GetComponent<Button>();

            colorButton.targetGraphic = colorImage;

            colorButton.onClick.AddListener(() =>
            {
                if (paintEmitter != null)
                {
                    paintEmitter.paintColor = selectedColor;
                }
            });
        }
    }



    void CreateButtonsPanel(Transform parent)
    {
        GameObject panel = new GameObject(
            "ButtonsPanel",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(GridLayoutGroup)
        );

        panel.transform.SetParent(parent, false);

        RectTransform rectTransform =
            panel.GetComponent<RectTransform>();

        rectTransform.sizeDelta =
            new Vector2(0.0f, 170.0f);

        LayoutElement layoutElement =
            panel.GetComponent<LayoutElement>();

        layoutElement.minHeight = 170.0f;
        layoutElement.preferredHeight = 170.0f;
        layoutElement.flexibleHeight = 0.0f;

        GridLayoutGroup grid =
            panel.GetComponent<GridLayoutGroup>();

        grid.padding =
            new RectOffset(8, 8, 8, 8);

        grid.spacing =
            new Vector2(10.0f, 10.0f);

        grid.cellSize =
            new Vector2(160.0f, 30.0f);

        grid.constraint =
            GridLayoutGroup.Constraint.FixedColumnCount;

        grid.constraintCount = 2;

        grid.startAxis =
            GridLayoutGroup.Axis.Horizontal;

        grid.childAlignment =
            TextAnchor.MiddleCenter;

        CreateButton(
            panel.transform,
            "Start",
            colAccent,
            TogglePlay
        );

        CreateButton(
            panel.transform,
            "Pause",
            colOrange,
            TogglePause
        );

        CreateButton(
            panel.transform,
            "Reset",
            colRed,
            ResetSimulation
        );
        CreateButton(
panel.transform,
"Stop",
colRed,
StopExperiment
);
        CreateButton(
    panel.transform,
    "Clear",
    colOrange,
    ClearCanvasOnly
);
        CreateButton(
    panel.transform,
    "Save Experiment",
    colGreen,
    SaveExperimentPackage
);
        CreateButton(
    panel.transform,
    "Load Experiment",
    colAccent,
    LoadLatestExperiment
);

    }
    void ClearCanvasOnly()
    {
        if (canvasStage != null)
            canvasStage.ClearPainting();
    }
    void CreateButton(
        Transform parent,
        string text,
        Color color,
        UnityEngine.Events.UnityAction action
    )
    {
        GameObject buttonObject = new GameObject(
            "Btn_" + text,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );

        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform =
            buttonObject.GetComponent<RectTransform>();

        rectTransform.sizeDelta =
            new Vector2(160.0f, 30.0f);

        rectTransform.localScale =
            Vector3.one;

        Image image =
            buttonObject.GetComponent<Image>();

        image.color = color;
        image.raycastTarget = true;

        Button button =
            buttonObject.GetComponent<Button>();

        button.targetGraphic = image;

        button.transition =
            Selectable.Transition.ColorTint;

        ColorBlock colors =
            button.colors;

        colors.normalColor = color;

        colors.highlightedColor = new Color(
            Mathf.Clamp01(color.r + 0.08f),
            Mathf.Clamp01(color.g + 0.08f),
            Mathf.Clamp01(color.b + 0.08f),
            1.0f
        );

        colors.pressedColor = new Color(
            Mathf.Clamp01(color.r - 0.10f),
            Mathf.Clamp01(color.g - 0.10f),
            Mathf.Clamp01(color.b - 0.10f),
            1.0f
        );

        colors.selectedColor =
            colors.highlightedColor;

        colors.disabledColor = new Color(
            color.r,
            color.g,
            color.b,
            0.4f
        );

        colors.colorMultiplier = 1.0f;
        colors.fadeDuration = 0.1f;

        button.colors = colors;

        TextMeshProUGUI buttonText = CreateText(
            buttonObject.transform,
            text,
            13,
            Color.white,
            true
        );

        RectTransform textRect =
            buttonText.GetComponent<RectTransform>();

        textRect.anchorMin =
            Vector2.zero;

        textRect.anchorMax =
            Vector2.one;

        textRect.offsetMin =
            Vector2.zero;

        textRect.offsetMax =
            Vector2.zero;

        buttonText.alignment =
            TextAlignmentOptions.Center;

        button.onClick.AddListener(action);
    }


    void CreateLiveInfoHeader(Transform parent, string text)
    {
        GameObject go = new GameObject("LiveHeader", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 26);
        TextMeshProUGUI tmp = CreateText(go.transform, text, 14, colAccent, true);
        tmp.alignment = TextAlignmentOptions.Left;
    }

    void CreateLiveInfoRow(Transform parent, string label, string key)
    {
        GameObject row = new GameObject("Live_" + key, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rt = row.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 20);

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 4;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI labelText = CreateText(row.transform, label + ":", 11, colTextSecondary, false);
        labelText.GetComponent<RectTransform>().sizeDelta = new Vector2(95, 18);

        TextMeshProUGUI valueText = CreateText(row.transform, "0", 11, colTextPrimary, false);
        valueText.alignment = TextAlignmentOptions.Right;
        valueText.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 18);

        liveTexts[key] = valueText;
    }

    TextMeshProUGUI CreateText(Transform parent, string text, int fontSize, Color color, bool bold)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        return tmp;
    }

    // ==================== Logic ====================

    void BindInitialValues()
    {
        if (paintEmitter != null)
        {
            configuredPaintVolumeLiters =
        paintEmitter.paintVolumeLiters;
            SetSlider(
                "paintVolume",
                paintEmitter.paintVolumeLiters,
                "F2"
            );

            SetSlider(
                "paintDensity",
                paintEmitter.paintDensityKgPerLiter,
                "F2"
            );

            SetSlider(
                "bucketBottomRadius",
                paintEmitter.bucketBottomRadiusMeters,
                "F3"
            );

            SetSlider(
                "bucketTopRadius",
                paintEmitter.bucketTopRadiusMeters,
                "F3"
            );

            SetSlider(
                "bucketHeight",
                paintEmitter.bucketHeightMeters,
                "F3"
            );

            SetSlider(
                "holeDiameter",
                paintEmitter.holeDiameterMillimeters,
                "F1"
            );

            SetSlider(
                "viscosity",
                paintEmitter.viscosity01,
                "F2"
            );
        }
        if (bucketStage01 != null)
        {
            SetSlider(
                    "bucketMass",
                    bucketStage01.bucketDryMassKg,
                    "F2"
                );
            SetSlider("ropeLength", bucketStage01.baseRopeLength, "F2");
            SetSlider(
    "ropeStiffness",
    bucketStage01.ropeSpringConstant,
    "F0"
);
            SetSlider("startAngle", bucketStage01.startAngleDegrees, "F1");
            SetSlider(
        "startDirection",
        bucketStage01.startDirectionDegrees,
        "F0"
    );

            SetSlider(
                "initialSpeedX",
                bucketStage01.initialHandleVelocity.x,
                "F2"
            );

            SetSlider(
                "initialSpeedZ",
                bucketStage01.initialHandleVelocity.z,
                "F2"
            );
            SetSlider("gravity", bucketStage01.gravity, "F2");
            SetSlider("airDrag", bucketStage01.airDragCoefficient, "F2");

        }
        if (fluidStage != null)
        {
            pendingParticleCount =
    fluidStage.particleCount;

            pendingGridResolution =
                fluidStage.gridResolution;
            SetSlider("particleCount", fluidStage.particleCount, "F0");
            SetSlider("gridResolution", fluidStage.gridResolution, "F0");
            SetSlider("pressureStrength", fluidStage.pressureStrength, "F2");
            SetSlider("wallFriction", fluidStage.wallFriction, "F2");
            SetSlider("vortexStrength", fluidStage.vortexStrength, "F2");
        }
        if (canvasStage != null)
        {
            SetSlider("canvasWidth", canvasStage.canvasSizeMeters.x, "F2");
            SetSlider("canvasHeight", canvasStage.canvasSizeMeters.y, "F2");
        }
        if (boardTiltController != null)
        {
            SetSlider(
                "canvasTiltX",
                boardTiltController.TiltX,
                "F1"
            );

            SetSlider(
                "canvasTiltZ",
                boardTiltController.TiltZ,
                "F1"
            );
        }
    }
    void SetSlider(string key, float value, string fmt)
    {
        if (!sliders.TryGetValue(key, out Slider slider))
            return;

        slider.SetValueWithoutNotify(value);

        if (valueLabels.TryGetValue(
            key,
            out TextMeshProUGUI label
        ))
        {
            label.text = value.ToString(fmt);
        }
    }

    void OnSliderChanged(string key, float value)
    {
        if (paintEmitter != null)
        {
            if (key == "paintVolume")
            {
                configuredPaintVolumeLiters = value;

                if (
                    experimentState == ExperimentState.Ready ||
                    experimentState == ExperimentState.Stopped
                )
                {
                    paintEmitter.paintVolumeLiters = value;
                }
            }

            if (key == "paintDensity")
            {
                paintEmitter.paintDensityKgPerLiter =
                    value;
            }

            if (key == "bucketBottomRadius")
            {
                paintEmitter.bucketBottomRadiusMeters =
                    value;
            }

            if (key == "bucketTopRadius")
            {
                paintEmitter.bucketTopRadiusMeters =
                    value;
            }

            if (key == "bucketHeight")
            {
                paintEmitter.bucketHeightMeters =
                    value;
            }

            if (key == "holeDiameter")
            {
                paintEmitter.holeDiameterMillimeters =
                    value;
            }

            if (key == "viscosity")
            {
                paintEmitter.viscosity01 =
                    value;
            }
        }
        if (bucketStage01 != null)
        {
            if (key == "bucketMass")
            {
                bucketStage01.bucketDryMassKg =
                    value;
            }

            if (key == "ropeLength")
            {
                bucketStage01.baseRopeLength =
                    value;
            }

            if (key == "ropeStiffness")
            {
                bucketStage01.ropeSpringConstant =
                    Mathf.Max(10.0f, value);
            }


            if (key == "startAngle")
            {
                bucketStage01
                    .Stage01_SetStartAngleAndPreview(
                        value
                    );
            }
            if (key == "startDirection")
            {
                bucketStage01.startDirectionDegrees =
                    Mathf.Repeat(value, 360.0f);

                /*
                 * نعيد وضع الدلو فوراً عند تعديل الاتجاه،
                 * لكن فقط قبل تشغيل التجربة أو بعد إيقافها.
                 */
                if (
                    experimentState == ExperimentState.Ready ||
                    experimentState == ExperimentState.Stopped
                )
                {
                    bucketStage01.startFromCurrentHandlePosition =
                        false;

                    bucketStage01.Stage01_ResetSimulation();

                    bucketStage01.Stage01_SetSimulationRunning(
                        false
                    );
                }
            }

            if (key == "initialSpeedX")
            {
                Vector3 velocity =
                    bucketStage01.initialHandleVelocity;

                velocity.x = value;

                bucketStage01.initialHandleVelocity =
                    velocity;
            }

            if (key == "initialSpeedZ")
            {
                Vector3 velocity =
                    bucketStage01.initialHandleVelocity;

                velocity.z = value;

                bucketStage01.initialHandleVelocity =
                    velocity;
            }

            if (key == "gravity")
            {
                bucketStage01.gravity =
                    value;
            }

            if (key == "airDrag")
            {
                bucketStage01.airDragCoefficient =
                    value;
            }
        }
        if (fluidStage != null)
        {
            if (key == "particleCount")
            {
                pendingParticleCount =
                    Mathf.RoundToInt(value);
            }

            if (key == "gridResolution")
            {
                pendingGridResolution =
                    Mathf.RoundToInt(value);
            }
            if (key == "pressureStrength") fluidStage.pressureStrength = value;
            if (key == "wallFriction") fluidStage.wallFriction = value;
            if (key == "vortexStrength") fluidStage.vortexStrength = value;
        }
        //if (canvasStage != null)
        //{
        //    if (key == "canvasWidth") canvasStage.canvasSizeMeters = new Vector2(value, canvasStage.canvasSizeMeters.y);
        //    if (key == "canvasHeight") canvasStage.canvasSizeMeters = new Vector2(canvasStage.canvasSizeMeters.x, value);
        //}
        if (key == "canvasWidth")
        {
            float height =
                canvasStage != null
                    ? canvasStage.canvasSizeMeters.y
                    : 2.0f;

            ApplyCanvasSize(value, height);
        }


        if (key == "canvasHeight")
        {
            float width =
                canvasStage != null
                    ? canvasStage.canvasSizeMeters.x
                    : 2.0f;

            ApplyCanvasSize(width, value);
        }
        if (boardTiltController != null)
        {
            if (key == "canvasTiltX")
            {
                boardTiltController.SetTiltX(
                    value
                );
            }

            if (key == "canvasTiltZ")
            {
                boardTiltController.SetTiltZ(
                    value
                );
            }
        }
    }

    void SetSetupControlsInteractable(
        bool interactable
    )
    {
        foreach (
            string key in
            setupOnlySliderKeys
        )
        {
            if (
                !sliders.TryGetValue(
                    key,
                    out Slider slider
                )
            )
            {
                continue;
            }

            slider.interactable =
                interactable;

            if (
                sliderFillImages.TryGetValue(
                    key,
                    out Image fillImage
                )
            )
            {
                fillImage.color =
                    interactable
                        ? colAccent
                        : colLockedSlider;
            }

            if (
                sliderHandleImages.TryGetValue(
                    key,
                    out Image handleImage
                )
            )
            {
                handleImage.color =
                    interactable
                        ? Color.white
                        : colLockedSlider;
            }

            if (
                sliderLabelTexts.TryGetValue(
                    key,
                    out TextMeshProUGUI
                        labelText
                )
            )
            {
                labelText.color =
                    interactable
                        ? colTextSecondary
                        : colLockedText;
            }

            if (
                valueLabels.TryGetValue(
                    key,
                    out TextMeshProUGUI
                        valueText
                )
            )
            {
                valueText.color =
                    interactable
                        ? colAccent
                        : colLockedText;
            }
        }
    }
    void ApplyPendingGpuSettings()
    {
        if (fluidStage == null)
            return;

        fluidStage.particleCount =
            Mathf.Clamp(
                pendingParticleCount,
                50000,
                2000000
            );

        fluidStage.gridResolution =
            Mathf.Clamp(
                pendingGridResolution,
                32,
                128
            );
    }
    void TogglePlay()
    {
        if (
            experimentState ==
                ExperimentState.Ready ||
            experimentState ==
                ExperimentState.Stopped
        )
        {
            StartNewExperiment();
            return;
        }

        ResumeExperiment();
    }
    void TogglePause()
    {
        if (
            experimentState !=
            ExperimentState.Running
        )
        {
            return;
        }

        experimentState =
            ExperimentState.Paused;

        if (bucketStage01 != null)
        {
            bucketStage01
                .Stage01_SetSimulationRunning(false);
        }

        if (fluidStage != null)
        {
            fluidStage.simulateFluid = false;
        }

        Time.timeScale = 0.0f;
    }

    void StartNewExperiment()
    {
        ApplyPendingGpuSettings();
        if (paintEmitter != null)
        {
            paintEmitter.paintVolumeLiters =
                configuredPaintVolumeLiters;
        }
        Time.timeScale = 1.0f;
        experimentElapsedTime = 0.0f;

        if (bucketStage01 != null)
        {
            bucketStage01
                .startFromCurrentHandlePosition =
                    false;

            bucketStage01
                .Stage01_ResetSimulation();

            bucketStage01
                .Stage01_SetSimulationRunning(
                    true
                );
        }

        if (fluidStage != null)
        {
            fluidStage.InitializeFluid();
            fluidStage.simulateFluid = true;
        }

        if (canvasStage != null)
            canvasStage.ClearPainting();

        experimentState =
            ExperimentState.Running;
        SetSetupControlsInteractable(false);
        if (experimentResultsController != null)
        {
            experimentResultsController.BeginExperiment(
                configuredPaintVolumeLiters
            );
        }
    }

    void ResumeExperiment()
    {
        Time.timeScale = 1.0f;

        if (bucketStage01 != null)
        {
            bucketStage01
                .Stage01_SetSimulationRunning(
                    true
                );
        }

        if (fluidStage != null)
            fluidStage.simulateFluid = true;

        experimentState =
            ExperimentState.Running;
    }

    void StopExperiment()
    {
        Time.timeScale = 1.0f;

        if (bucketStage01 != null)
        {
            bucketStage01
                .Stage01_SetSimulationRunning(false);
        }

        if (fluidStage != null)
        {
            fluidStage.simulateFluid = false;
        }

        experimentState =
            ExperimentState.Stopped;

        SetSetupControlsInteractable(true);
        if (experimentResultsController != null)
        {
            experimentResultsController
                .StopAndShowResults(
                    experimentElapsedTime
                );
        }
    }
    void ResetSimulation()
    {
        Time.timeScale = 1.0f;

        ApplyPendingGpuSettings();

        if (paintEmitter != null)
        {
            paintEmitter.paintVolumeLiters =
                configuredPaintVolumeLiters;
        }

        if (bucketStage01 != null)
        {
            bucketStage01
                .startFromCurrentHandlePosition =
                    false;

            bucketStage01
                .Stage01_ResetSimulation();

            bucketStage01
                .Stage01_SetSimulationRunning(
                    false
                );
        }

        if (fluidStage != null)
        {
            fluidStage.simulateFluid = false;
            fluidStage.InitializeFluid();
        }

        if (canvasStage != null)
        {
            canvasStage.ClearPainting();
        }

        experimentElapsedTime = 0.0f;
        experimentState = ExperimentState.Ready;

        SetSetupControlsInteractable(true);
        if (experimentResultsController != null)
        {
            experimentResultsController
                .ResetExperimentTracking();
        }
    }

    ////IEnumerator SaveCanvasImageRoutine()
    ////{
    ////    yield return new WaitForEndOfFrame();

    ////    if (canvasStage == null)
    ////    {
    ////        Debug.LogError("Canvas Stage reference is missing.");
    ////        yield break;
    ////    }

    ////    RenderTexture source =
    ////        canvasStage.ColorTexture;

    ////    if (source == null)
    ////    {
    ////        Debug.LogError(
    ////            "The canvas ColorTexture has not been initialized."
    ////        );

    ////        yield break;
    ////    }

    ////    RenderTexture previousActive =
    ////        RenderTexture.active;

    ////    RenderTexture.active = source;

    ////    Texture2D image = new Texture2D(
    ////        source.width,
    ////        source.height,
    ////        TextureFormat.RGBA32,
    ////        false,
    ////        false
    ////    );

    ////    image.ReadPixels(
    ////        new Rect(
    ////            0,
    ////            0,
    ////            source.width,
    ////            source.height
    ////        ),
    ////        0,
    ////        0,
    ////        false
    ////    );

    ////    image.Apply(false, false);

    ////    RenderTexture.active =
    ////        previousActive;

    ////    byte[] pngBytes =
    ////        image.EncodeToPNG();

    ////    Destroy(image);

    ////    string folderPath = Path.Combine(
    ////        Application.persistentDataPath,
    ////        "SwingingPaintBucketResults"
    ////    );

    ////    if (!Directory.Exists(folderPath))
    ////        Directory.CreateDirectory(folderPath);

    ////    string fileName =
    ////        "Painting_" +
    ////        System.DateTime.Now.ToString(
    ////            "yyyy-MM-dd_HH-mm-ss"
    ////        ) +
    ////        ".png";

    ////    string fullPath =
    ////        Path.Combine(folderPath, fileName);

    ////    File.WriteAllBytes(
    ////        fullPath,
    ////        pngBytes
    ////    );

    ////    Debug.Log(
    ////        "Painting saved successfully:\n" +
    ////        fullPath
    ////    );
    ////}
    //IEnumerator SaveCanvasImageRoutine()
    //{
    //    if (isSavingImage)
    //        yield break;

    //    isSavingImage = true;

    //    yield return new WaitForEndOfFrame();

    //    if (boardSaveCamera == null)
    //    {
    //        Debug.LogError(
    //            "Board Save Camera reference is missing."
    //        );

    //        isSavingImage = false;
    //        yield break;
    //    }

    //    int width = Mathf.Clamp(
    //        saveImageWidth,
    //        256,
    //        4096
    //    );

    //    int height = Mathf.Clamp(
    //        saveImageHeight,
    //        256,
    //        4096
    //    );

    //    RenderTexture temporaryTexture = null;
    //    Texture2D image = null;

    //    RenderTexture previousActive =
    //        RenderTexture.active;

    //    RenderTexture previousCameraTarget =
    //        boardSaveCamera.targetTexture;

    //    bool previousCameraEnabled =
    //        boardSaveCamera.enabled;

    //    try
    //    {
    //        temporaryTexture =
    //            RenderTexture.GetTemporary(
    //                width,
    //                height,
    //                24,
    //                RenderTextureFormat.ARGB32,
    //                RenderTextureReadWrite.sRGB
    //            );

    //        temporaryTexture.filterMode =
    //            FilterMode.Bilinear;

    //        boardSaveCamera.enabled = false;

    //        boardSaveCamera.targetTexture =
    //            temporaryTexture;

    //        boardSaveCamera.Render();

    //        RenderTexture.active =
    //            temporaryTexture;

    //        image = new Texture2D(
    //            width,
    //            height,
    //            TextureFormat.RGBA32,
    //            false,
    //            false
    //        );

    //        image.ReadPixels(
    //            new Rect(
    //                0,
    //                0,
    //                width,
    //                height
    //            ),
    //            0,
    //            0,
    //            false
    //        );

    //        image.Apply(
    //            false,
    //            false
    //        );

    //        byte[] pngBytes =
    //            image.EncodeToPNG();

    //        string folderPath = Path.Combine(
    //            Application.persistentDataPath,
    //            "SwingingPaintBucketResults"
    //        );

    //        if (!Directory.Exists(folderPath))
    //        {
    //            Directory.CreateDirectory(
    //                folderPath
    //            );
    //        }

    //        string fileName =
    //            "FinalPainting_" +
    //            System.DateTime.Now.ToString(
    //                "yyyy-MM-dd_HH-mm-ss"
    //            ) +
    //            ".png";

    //        string fullPath =
    //            Path.Combine(
    //                folderPath,
    //                fileName
    //            );

    //        File.WriteAllBytes(
    //            fullPath,
    //            pngBytes
    //        );

    //        Debug.Log(
    //            "Final painting saved successfully:\n" +
    //            fullPath
    //        );
    //    }
    //    catch (System.Exception exception)
    //    {
    //        Debug.LogError(
    //            "Failed to save final painting:\n" +
    //            exception
    //        );
    //    }
    //    finally
    //    {
    //        boardSaveCamera.targetTexture =
    //            previousCameraTarget;

    //        boardSaveCamera.enabled =
    //            previousCameraEnabled;

    //        RenderTexture.active =
    //            previousActive;

    //        if (temporaryTexture != null)
    //        {
    //            RenderTexture.ReleaseTemporary(
    //                temporaryTexture
    //            );
    //        }

    //        if (image != null)
    //        {
    //            Destroy(image);
    //        }

    //        isSavingImage = false;
    //    }
    //}
    ////void SaveImage()
    ////{
    ////    if (!Application.isPlaying)
    ////    {
    ////        Debug.LogWarning(
    ////            "Image saving works only while the application is playing."
    ////        );

    ////        return;
    ////    }
    ////    if (isSavingImage)
    ////        return;
    ////    StartCoroutine(SaveCanvasImageRoutine());
    ////}
    //void SaveImage()
    //{
    //    if (!Application.isPlaying)
    //    {
    //        Debug.LogWarning(
    //            "Image saving works only during Play Mode."
    //        );

    //        return;
    //    }

    //    if (isSavingImage)
    //    {
    //        Debug.LogWarning(
    //            "An image is already being saved."
    //        );

    //        return;
    //    }

    //    StartCoroutine(
    //        SaveCanvasImageRoutine()
    //    );
    //}


    void SaveExperimentPackage()
    {
        if (experimentResultsController == null)
        {
            Debug.LogError(
                "Experiment Results Controller is missing."
            );

            return;
        }

        experimentResultsController
            .SaveExperimentPackage();
    }

    void LoadLatestExperiment()
    {
        if (experimentResultsController == null)
        {
            Debug.LogError(
                "Experiment Results Controller is missing."
            );

            return;
        }

        experimentResultsController
            .LoadLatestExperiment();
    }
    public void ApplyLoadedSettingsFromPersistence()
    {
        Time.timeScale = 1.0f;

        if (paintEmitter != null)
        {
            configuredPaintVolumeLiters =
                paintEmitter.paintVolumeLiters;
        }

        if (fluidStage != null)
        {
            pendingParticleCount =
                fluidStage.particleCount;

            pendingGridResolution =
                fluidStage.gridResolution;
        }

        if (canvasStage != null)
        {
            ApplyCanvasSize(
                canvasStage.canvasSizeMeters.x,
                canvasStage.canvasSizeMeters.y
            );
        }

        BindInitialValues();
        RefreshSurfaceTypeControl();
        ResetSimulation();
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        fpsAccumulator += Time.unscaledDeltaTime;
        fpsFrames++;
        if (fpsAccumulator >= 0.5f)
        {
            if (liveTexts.ContainsKey("fps")) liveTexts["fps"].text = Mathf.RoundToInt(fpsFrames / fpsAccumulator).ToString();
            fpsAccumulator = 0f; fpsFrames = 0;
        }

        if (
            experimentState ==
            ExperimentState.Running
        )
        {
            experimentElapsedTime +=
                Time.deltaTime;
        }

        if (liveTexts.ContainsKey("simTime"))
        {
            liveTexts["simTime"].text =
                experimentElapsedTime
                    .ToString("F1") +
                " s";
        }

        if (fluidStage != null)
        {
            if (liveTexts.ContainsKey("particles")) liveTexts["particles"].text = fluidStage.ActiveParticleCount.ToString();
            if (liveTexts.ContainsKey("paintVol")) liveTexts["paintVol"].text = fluidStage.CalculatedPaintVolumeLiters.ToString("F3");
            if (liveTexts.ContainsKey("flow")) liveTexts["flow"].text = fluidStage.CurrentFlowLitersPerSecond.ToString("F3");
        }
        if (
    bucketStage01 != null &&
    paintEmitter != null
)
        {
            if (
    bucketStage01 != null &&
    liveTexts.ContainsKey("totalMass")
)
            {
                liveTexts["totalMass"].text =
                    bucketStage01
                        .BucketAndPaintMassKg
                        .ToString("F2");
            }
        }
        if (paintEmitter != null)
        {
            if (liveTexts.ContainsKey("exitSpeed"))
            {
                liveTexts["exitSpeed"].text =
                    paintEmitter
                        .CurrentExitSpeedMetersPerSecond
                        .ToString("F3");
            }

            bool drainAllowed =
                fluidStage != null &&
                fluidStage.allowDrainFromHole;

            bool diameterOpen =
                paintEmitter.holeDiameterMillimeters >
                0.0001f;

            bool holeOpen =
                fluidStage != null &&
                fluidStage.allowDrainFromHole &&
                paintEmitter.IsHoleOpen;

            if (liveTexts.ContainsKey("holeState"))
            {
                liveTexts["holeState"].text =
                    holeOpen
                        ? "OPEN"
                        : "CLOSED";

                liveTexts["holeState"].color =
                    holeOpen
                        ? colGreen
                        : colRed;
            }
        }
        if (bucketStage01 != null)
        {
            if (liveTexts.ContainsKey("tension")) liveTexts["tension"].text = bucketStage01.CurrentTensionNewton.ToString("F1");
            if (liveTexts.ContainsKey("ropeLen")) liveTexts["ropeLen"].text = bucketStage01.CurrentRopeLength.ToString("F2");
            if (liveTexts.ContainsKey("bucketVel")) liveTexts["bucketVel"].text = bucketStage01.BucketVelocity.magnitude.ToString("F2");
        }
    }
}

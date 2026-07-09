using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ExperimentSaveResultsController : MonoBehaviour
{
    [Header("Project References")]
    public SwingingPaintBucketUI userInterface;
    public SwingingBucketStage01 bucketStage01;
    public PaintEmitterStage02 paintEmitter;
    public GpuDensityPaintFluidStage03 fluidStage;
    public PaintCanvasStage04 canvasStage;
    public PaintBoardTiltController boardTiltController;
    public Camera boardSaveCamera;

    [Header("Export")]
    [Range(256, 4096)] public int saveImageWidth = 2048;
    [Range(256, 4096)] public int saveImageHeight = 2048;
    [Range(64, 512)] public int coverageSampleResolution = 256;
    [Range(1, 64)] public int paintPixelThreshold = 8;

    [Header("Swing Counter")]
    [Min(0.001f)] public float centerCrossingDeadZoneMeters = 0.03f;
    [Min(0.01f)] public float minimumCrossingIntervalSeconds = 0.20f;

    private readonly Color panelBackground = new Color(0.071f, 0.094f, 0.129f, 0.98f);
    private readonly Color overlayBackground = new Color(0.0f, 0.0f, 0.0f, 0.72f);
    private readonly Color accentColor = new Color(0.231f, 0.510f, 0.965f, 1.0f);
    private readonly Color primaryTextColor = new Color(0.969f, 0.980f, 0.988f, 1.0f);
    private readonly Color secondaryTextColor = new Color(0.580f, 0.639f, 0.718f, 1.0f);

    private GameObject resultsOverlay;
    private RawImage resultsPreview;
    private TextMeshProUGUI resultsText;
    private Texture2D previewTexture;

    private bool trackingExperiment;
    private bool savingPackage;
    private float initialPaintVolumeLiters;
    private float currentExperimentTime;
    private float maximumBucketSpeed;
    private float maximumRopeTensionNewton;
    private float latestCoveragePercent;

    private Vector3 swingAxisWorld = Vector3.right;
    private int previousSwingSide;
    private int centerCrossingCount;
    private int swingCount;
    private float lastCrossingUnscaledTime;

    public bool IsSavingPackage => savingPackage;
    public float MaximumBucketSpeed => maximumBucketSpeed;
    public float MaximumRopeTensionNewton => maximumRopeTensionNewton;
    public int SwingCount => swingCount;
    public float LatestCoveragePercent => latestCoveragePercent;

    private void Awake()
    {
        if (boardSaveCamera == null)
            return;

        boardSaveCamera.enabled = false;
        boardSaveCamera.targetTexture = null;

        AudioListener listener = boardSaveCamera.GetComponent<AudioListener>();
        if (listener != null)
            listener.enabled = false;
    }

    private void Update()
    {
        if (!trackingExperiment || bucketStage01 == null)
            return;

        maximumBucketSpeed = Mathf.Max(maximumBucketSpeed, bucketStage01.BucketVelocity.magnitude);
        maximumRopeTensionNewton = Mathf.Max(maximumRopeTensionNewton, bucketStage01.CurrentTensionNewton);
        UpdateSwingCounter();
    }

    public void BeginExperiment(float configuredInitialPaintVolumeLiters)
    {
        trackingExperiment = true;
        initialPaintVolumeLiters = Mathf.Max(0.0f, configuredInitialPaintVolumeLiters);
        currentExperimentTime = 0.0f;
        maximumBucketSpeed = 0.0f;
        maximumRopeTensionNewton = 0.0f;
        latestCoveragePercent = 0.0f;
        centerCrossingCount = 0;
        swingCount = 0;
        lastCrossingUnscaledTime = -1000.0f;

        CaptureSwingAxis();
        previousSwingSide = GetCurrentSwingSide();
        HideResults();
    }

    public void ResetExperimentTracking()
    {
        trackingExperiment = false;
        currentExperimentTime = 0.0f;
        maximumBucketSpeed = 0.0f;
        maximumRopeTensionNewton = 0.0f;
        latestCoveragePercent = 0.0f;
        centerCrossingCount = 0;
        swingCount = 0;
        HideResults();
    }

    public void StopAndShowResults(float experimentTimeSeconds)
    {
        trackingExperiment = false;
        currentExperimentTime = Mathf.Max(0.0f, experimentTimeSeconds);
        StartCoroutine(BuildResultsRoutine());
    }

    public void SaveExperimentPackage()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Experiment export works only during Play Mode.");
            return;
        }

        if (savingPackage)
        {
            Debug.LogWarning("An experiment package is already being saved.");
            return;
        }

        StartCoroutine(SaveExperimentPackageRoutine());
    }

    public void LoadLatestExperiment()
    {
        string folder = GetResultsFolder();

        if (!Directory.Exists(folder))
        {
            Debug.LogWarning("No saved experiment folder exists yet.");
            return;
        }

        string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
        {
            Debug.LogWarning("No saved experiment JSON file was found.");
            return;
        }

        string latestFile = files[0];
        DateTime latestTime = File.GetLastWriteTimeUtc(latestFile);

        for (int i = 1; i < files.Length; i++)
        {
            DateTime candidateTime = File.GetLastWriteTimeUtc(files[i]);
            if (candidateTime > latestTime)
            {
                latestTime = candidateTime;
                latestFile = files[i];
            }
        }

        try
        {
            SwingingPaintExperimentData data = JsonUtility.FromJson<SwingingPaintExperimentData>(File.ReadAllText(latestFile));

            if (data == null)
            {
                Debug.LogError("The saved experiment file could not be parsed.");
                return;
            }

            ApplyExperimentData(data);
            Debug.Log("Experiment settings loaded successfully:\n" + latestFile);
        }
        catch (Exception exception)
        {
            Debug.LogError("Failed to load the latest experiment:\n" + exception);
        }
    }

    public void HideResults()
    {
        if (resultsOverlay != null)
            resultsOverlay.SetActive(false);
    }

    private IEnumerator BuildResultsRoutine()
    {
        yield return new WaitForEndOfFrame();

        latestCoveragePercent = CalculateCoveragePercent();
        ReplacePreviewTexture(CaptureBoardImage(1024, 1024));
        EnsureResultsPanel();

        if (resultsOverlay == null)
            yield break;

        RefreshResultsPanel();
        resultsOverlay.SetActive(true);
        resultsOverlay.transform.SetAsLastSibling();
    }

    private IEnumerator SaveExperimentPackageRoutine()
    {
        savingPackage = true;
        yield return new WaitForEndOfFrame();

        Texture2D capturedImage = null;

        try
        {
            latestCoveragePercent = CalculateCoveragePercent();
            capturedImage = CaptureBoardImage(
                Mathf.Clamp(saveImageWidth, 256, 4096),
                Mathf.Clamp(saveImageHeight, 256, 4096)
            );

            if (capturedImage == null)
                throw new InvalidOperationException("The board image could not be captured.");

            string folder = GetResultsFolder();
            Directory.CreateDirectory(folder);

            string experimentId = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string imageFileName = "FinalPainting_" + experimentId + ".png";
            string jsonFileName = "Experiment_" + experimentId + ".json";
            string imagePath = Path.Combine(folder, imageFileName);
            string jsonPath = Path.Combine(folder, jsonFileName);

            File.WriteAllBytes(imagePath, capturedImage.EncodeToPNG());

            SwingingPaintExperimentData data = BuildExperimentData(experimentId, imageFileName);
            File.WriteAllText(jsonPath, JsonUtility.ToJson(data, true));

            Debug.Log(
                "Experiment package saved successfully:\n" +
                "Image: " + imagePath + "\n" +
                "JSON: " + jsonPath
            );
        }
        catch (Exception exception)
        {
            Debug.LogError("Failed to save the experiment package:\n" + exception);
        }
        finally
        {
            if (capturedImage != null)
                Destroy(capturedImage);

            savingPackage = false;
        }
    }

    private SwingingPaintExperimentData BuildExperimentData(string experimentId, string imageFileName)
    {
        SwingingPaintExperimentData data = new SwingingPaintExperimentData
        {
            experimentId = experimentId,
            savedAtIso = DateTime.Now.ToString("O"),
            imageFileName = imageFileName
        };

        if (bucketStage01 != null)
        {
            data.bucketDryMassKg = bucketStage01.bucketDryMassKg;
            data.ropeLengthMeters = bucketStage01.baseRopeLength;
            data.ropeSpringConstant = bucketStage01.ropeSpringConstant;
            data.startAngleDegrees = bucketStage01.startAngleDegrees;
            data.startDirectionDegrees = bucketStage01.startDirectionDegrees;
            data.initialHandleVelocity = bucketStage01.initialHandleVelocity;
            data.gravity = bucketStage01.gravity;
            data.windAcceleration = bucketStage01.windAcceleration;
        }

        if (paintEmitter != null)
        {
            data.bucketBottomRadiusMeters = paintEmitter.bucketBottomRadiusMeters;
            data.bucketTopRadiusMeters = paintEmitter.bucketTopRadiusMeters;
            data.bucketHeightMeters = paintEmitter.bucketHeightMeters;
            data.paintDensityKgPerLiter = paintEmitter.paintDensityKgPerLiter;
            data.holeDiameterMillimeters = paintEmitter.holeDiameterMillimeters;
            data.viscosity01 = paintEmitter.viscosity01;
            data.paintColor = paintEmitter.paintColor;
        }

        data.initialPaintVolumeLiters = Mathf.Max(
            0.0f,
            initialPaintVolumeLiters > 0.0f
                ? initialPaintVolumeLiters
                : paintEmitter != null ? paintEmitter.paintVolumeLiters : 0.0f
        );

        data.remainingPaintVolumeLiters = GetRemainingPaintVolumeLiters();
        data.usedPaintVolumeLiters = Mathf.Max(0.0f, data.initialPaintVolumeLiters - data.remainingPaintVolumeLiters);

        if (canvasStage != null)
        {
            data.surfaceTypeIndex = (int)canvasStage.surfaceType;
            data.surfaceTypeName = canvasStage.surfaceType.ToString();
            data.canvasSizeMeters = canvasStage.canvasSizeMeters;
        }
        else if (fluidStage != null)
        {
            data.surfaceTypeIndex = (int)fluidStage.canvasSurfaceType;
            data.surfaceTypeName = fluidStage.canvasSurfaceType.ToString();
            data.canvasSizeMeters = fluidStage.canvasSizeMeters;
        }

        if (boardTiltController != null)
        {
            data.canvasTiltX = boardTiltController.TiltX;
            data.canvasTiltZ = boardTiltController.TiltZ;
        }

        if (fluidStage != null)
        {
            data.particleCount = fluidStage.particleCount;
            data.gridResolution = fluidStage.gridResolution;
        }

        data.experimentTimeSeconds = userInterface != null
            ? userInterface.ExperimentElapsedTime
            : currentExperimentTime;

        data.maximumBucketSpeed = maximumBucketSpeed;
        data.maximumRopeTensionNewton = maximumRopeTensionNewton;
        data.swingCount = swingCount;
        data.coveragePercent = latestCoveragePercent;
        data.paintedAreaSquareMeters = data.canvasSizeMeters.x * data.canvasSizeMeters.y * data.coveragePercent * 0.01f;

        return data;
    }

    private void ApplyExperimentData(SwingingPaintExperimentData data)
    {
        Time.timeScale = 1.0f;
        trackingExperiment = false;

        if (bucketStage01 != null)
        {
            bucketStage01.bucketDryMassKg = Mathf.Max(0.01f, data.bucketDryMassKg);
            bucketStage01.baseRopeLength = Mathf.Max(0.1f, data.ropeLengthMeters);
            bucketStage01.ropeSpringConstant = Mathf.Max(10.0f, data.ropeSpringConstant);
            bucketStage01.startAngleDegrees = Mathf.Clamp(data.startAngleDegrees, 0.0f, 85.0f);
            bucketStage01.startDirectionDegrees = data.startDirectionDegrees;
            bucketStage01.initialHandleVelocity = data.initialHandleVelocity;
            bucketStage01.gravity = Mathf.Max(0.0f, data.gravity);
            bucketStage01.windAcceleration = data.windAcceleration;
        }

        if (paintEmitter != null)
        {
            paintEmitter.bucketBottomRadiusMeters = Mathf.Max(0.001f, data.bucketBottomRadiusMeters);
            paintEmitter.bucketTopRadiusMeters = Mathf.Max(0.001f, data.bucketTopRadiusMeters);
            paintEmitter.bucketHeightMeters = Mathf.Max(0.001f, data.bucketHeightMeters);
            paintEmitter.paintVolumeLiters = Mathf.Max(0.0f, data.initialPaintVolumeLiters);
            paintEmitter.paintDensityKgPerLiter = Mathf.Max(0.001f, data.paintDensityKgPerLiter);
            paintEmitter.holeDiameterMillimeters = Mathf.Max(0.0f, data.holeDiameterMillimeters);
            paintEmitter.viscosity01 = Mathf.Clamp01(data.viscosity01);
            paintEmitter.paintColor = data.paintColor;
        }

        int surfaceIndex = Mathf.Clamp(data.surfaceTypeIndex, 0, 3);
        Vector2 safeCanvasSize = new Vector2(
            Mathf.Max(0.1f, data.canvasSizeMeters.x),
            Mathf.Max(0.1f, data.canvasSizeMeters.y)
        );

        if (canvasStage != null)
        {
            canvasStage.surfaceType = (PaintCanvasStage04.SurfaceType)surfaceIndex;
            canvasStage.canvasSizeMeters = safeCanvasSize;
        }

        if (fluidStage != null)
        {
            fluidStage.canvasSurfaceType = (GpuDensityPaintFluidStage03.PaintCanvasSurfaceType)surfaceIndex;
            fluidStage.canvasSizeMeters = safeCanvasSize;
            fluidStage.particleCount = Mathf.Clamp(data.particleCount, 50000, 2000000);
            fluidStage.gridResolution = Mathf.Clamp(data.gridResolution, 32, 128);
        }

        if (boardTiltController != null)
            boardTiltController.SetTilt(data.canvasTiltX, data.canvasTiltZ);

        initialPaintVolumeLiters = Mathf.Max(0.0f, data.initialPaintVolumeLiters);
        currentExperimentTime = Mathf.Max(0.0f, data.experimentTimeSeconds);
        maximumBucketSpeed = Mathf.Max(0.0f, data.maximumBucketSpeed);
        maximumRopeTensionNewton = Mathf.Max(0.0f, data.maximumRopeTensionNewton);
        swingCount = Mathf.Max(0, data.swingCount);
        latestCoveragePercent = Mathf.Clamp(data.coveragePercent, 0.0f, 100.0f);

        if (userInterface != null)
        {
            userInterface.ApplyLoadedSettingsFromPersistence();
        }
        else
        {
            if (bucketStage01 != null)
            {
                bucketStage01.Stage01_ResetSimulation();
                bucketStage01.Stage01_SetSimulationRunning(false);
            }

            if (fluidStage != null)
            {
                fluidStage.simulateFluid = false;
                fluidStage.InitializeFluid();
            }

            if (canvasStage != null)
                canvasStage.ClearPainting();
        }

        HideResults();
    }

    private float GetRemainingPaintVolumeLiters()
    {
        if (fluidStage != null)
            return Mathf.Max(0.0f, fluidStage.CalculatedPaintVolumeLiters);

        if (paintEmitter != null)
            return Mathf.Max(0.0f, paintEmitter.paintVolumeLiters);

        return 0.0f;
    }

    private void CaptureSwingAxis()
    {
        if (bucketStage01 == null || bucketStage01.fixedAnchorPoint == null)
        {
            swingAxisWorld = Vector3.right;
            return;
        }

        Vector3 horizontalOffset = bucketStage01.HandlePosition - bucketStage01.fixedAnchorPoint.position;
        horizontalOffset.y = 0.0f;

        if (horizontalOffset.sqrMagnitude < 0.0001f)
        {
            float directionRadians = bucketStage01.startDirectionDegrees * Mathf.Deg2Rad;
            horizontalOffset = new Vector3(Mathf.Cos(directionRadians), 0.0f, Mathf.Sin(directionRadians));
        }

        swingAxisWorld = horizontalOffset.normalized;
    }

    private void UpdateSwingCounter()
    {
        int currentSide = GetCurrentSwingSide();
        if (currentSide == 0)
            return;

        if (previousSwingSide == 0)
        {
            previousSwingSide = currentSide;
            return;
        }

        if (currentSide != previousSwingSide &&
            Time.unscaledTime - lastCrossingUnscaledTime >= minimumCrossingIntervalSeconds)
        {
            centerCrossingCount++;
            swingCount = centerCrossingCount / 2;
            previousSwingSide = currentSide;
            lastCrossingUnscaledTime = Time.unscaledTime;
        }
    }

    private int GetCurrentSwingSide()
    {
        if (bucketStage01 == null || bucketStage01.fixedAnchorPoint == null)
            return 0;

        Vector3 offset = bucketStage01.HandlePosition - bucketStage01.fixedAnchorPoint.position;
        float signedDistance = Vector3.Dot(offset, swingAxisWorld);

        if (Mathf.Abs(signedDistance) < centerCrossingDeadZoneMeters)
            return 0;

        return signedDistance > 0.0f ? 1 : -1;
    }

    private float CalculateCoveragePercent()
    {
        if (canvasStage == null || canvasStage.ThicknessTexture == null)
            return 0.0f;

        int resolution = Mathf.Clamp(coverageSampleResolution, 64, 512);
        RenderTexture sampleTexture = RenderTexture.GetTemporary(
            resolution,
            resolution,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear
        );

        RenderTexture previous = RenderTexture.active;
        Texture2D readableTexture = null;

        try
        {
            Graphics.Blit(canvasStage.ThicknessTexture, sampleTexture);
            RenderTexture.active = sampleTexture;

            readableTexture = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                false,
                true
            );

            readableTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
            readableTexture.Apply(false, false);

            Color32[] pixels = readableTexture.GetPixels32();
            int paintedPixels = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                int strongestChannel = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));

                if (strongestChannel >= paintPixelThreshold)
                    paintedPixels++;
            }

            return pixels.Length == 0
                ? 0.0f
                : paintedPixels * 100.0f / pixels.Length;
        }
        finally
        {
            RenderTexture.active = previous;

            if (readableTexture != null)
                Destroy(readableTexture);

            RenderTexture.ReleaseTemporary(sampleTexture);
        }
    }

    private Texture2D CaptureBoardImage(int width, int height)
    {
        if (boardSaveCamera == null)
        {
            Debug.LogError("Board Save Camera reference is missing.");
            return null;
        }

        width = Mathf.Clamp(width, 256, 4096);
        height = Mathf.Clamp(height, 256, 4096);

        RenderTexture temporaryTexture = RenderTexture.GetTemporary(
            width,
            height,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB
        );

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = boardSaveCamera.targetTexture;
        Texture2D image = null;

        try
        {
            boardSaveCamera.enabled = false;
            boardSaveCamera.targetTexture = temporaryTexture;
            boardSaveCamera.Render();
            RenderTexture.active = temporaryTexture;

            image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            image.Apply(false, false);
            return image;
        }
        catch
        {
            if (image != null)
                Destroy(image);
            throw;
        }
        finally
        {
            boardSaveCamera.targetTexture = previousTarget;
            boardSaveCamera.enabled = false;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(temporaryTexture);
        }
    }

    private void ReplacePreviewTexture(Texture2D newTexture)
    {
        if (previewTexture != null)
            Destroy(previewTexture);

        previewTexture = newTexture;

        if (resultsPreview != null)
            resultsPreview.texture = previewTexture;
    }

    private void EnsureResultsPanel()
    {
        if (resultsOverlay != null)
            return;

        Canvas canvas = null;

        if (userInterface != null)
        {
            canvas =
                userInterface.GetComponent<Canvas>();
        }

        if (canvas == null)
        {
            canvas =
                FindAnyObjectByType<Canvas>();
        }

        resultsOverlay = new GameObject(
            "ExperimentResultsOverlay",
            typeof(RectTransform),
            typeof(Image)
        );

        resultsOverlay.transform.SetParent(canvas.transform, false);
        RectTransform overlayRect = resultsOverlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        resultsOverlay.GetComponent<Image>().color = overlayBackground;

        GameObject panel = new GameObject(
            "ResultsPanel",
            typeof(RectTransform),
            typeof(Image)
        );

        panel.transform.SetParent(resultsOverlay.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1040.0f, 680.0f);
        panel.GetComponent<Image>().color = panelBackground;

        TextMeshProUGUI title = CreateText(
            panel.transform,
            "EXPERIMENT RESULTS",
            28,
            primaryTextColor,
            FontStyles.Bold
        );

        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.0f, 1.0f);
        titleRect.anchorMax = new Vector2(1.0f, 1.0f);
        titleRect.pivot = new Vector2(0.5f, 1.0f);
        titleRect.anchoredPosition = new Vector2(0.0f, -24.0f);
        titleRect.sizeDelta = new Vector2(-48.0f, 48.0f);
        title.alignment = TextAlignmentOptions.Center;

        GameObject previewObject = new GameObject(
            "ResultPreview",
            typeof(RectTransform),
            typeof(RawImage)
        );

        previewObject.transform.SetParent(panel.transform, false);
        RectTransform previewRect = previewObject.GetComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0.0f, 0.5f);
        previewRect.anchorMax = new Vector2(0.0f, 0.5f);
        previewRect.pivot = new Vector2(0.0f, 0.5f);
        previewRect.anchoredPosition = new Vector2(36.0f, -12.0f);
        previewRect.sizeDelta = new Vector2(560.0f, 560.0f);

        resultsPreview = previewObject.GetComponent<RawImage>();
        resultsPreview.color = Color.white;
        resultsPreview.texture = previewTexture;

        resultsText = CreateText(
            panel.transform,
            string.Empty,
            18,
            secondaryTextColor,
            FontStyles.Normal
        );

        RectTransform infoRect = resultsText.rectTransform;
        infoRect.anchorMin = new Vector2(1.0f, 0.5f);
        infoRect.anchorMax = new Vector2(1.0f, 0.5f);
        infoRect.pivot = new Vector2(1.0f, 0.5f);
        infoRect.anchoredPosition = new Vector2(-36.0f, -12.0f);
        infoRect.sizeDelta = new Vector2(380.0f, 520.0f);
        resultsText.alignment = TextAlignmentOptions.TopLeft;
        resultsText.textWrappingMode = TextWrappingModes.Normal;

        CreateResultsButton(
            panel.transform,
            "SAVE EXPERIMENT",
            new Vector2(-170.0f, 26.0f),
            SaveExperimentPackage
        );

        CreateResultsButton(
            panel.transform,
            "CLOSE",
            new Vector2(170.0f, 26.0f),
            HideResults
        );
    }

    private void RefreshResultsPanel()
    {
        if (resultsText == null)
            return;

        float remaining = GetRemainingPaintVolumeLiters();
        float used = Mathf.Max(0.0f, initialPaintVolumeLiters - remaining);

        Vector2 canvasSize = canvasStage != null
            ? canvasStage.canvasSizeMeters
            : fluidStage != null ? fluidStage.canvasSizeMeters : Vector2.zero;

        float paintedArea = canvasSize.x * canvasSize.y * latestCoveragePercent * 0.01f;
        string surfaceName = canvasStage != null ? canvasStage.surfaceType.ToString() : "Unknown";

        resultsText.text =
            "<b>Time:</b> " + currentExperimentTime.ToString("F2") + " s\n\n" +
            "<b>Initial Paint:</b> " + initialPaintVolumeLiters.ToString("F3") + " L\n" +
            "<b>Remaining Paint:</b> " + remaining.ToString("F3") + " L\n" +
            "<b>Used Paint:</b> " + used.ToString("F3") + " L\n\n" +
            "<b>Maximum Bucket Speed:</b> " + maximumBucketSpeed.ToString("F3") + " m/s\n" +
            "<b>Maximum Rope Tension:</b> " + maximumRopeTensionNewton.ToString("F2") + " N\n" +
            "<b>Swing Count:</b> " + swingCount + "\n\n" +
            "<b>Coverage:</b> " + latestCoveragePercent.ToString("F2") + " %\n" +
            "<b>Painted Area:</b> " + paintedArea.ToString("F3") + " m²\n\n" +
            "<b>Surface:</b> " + surfaceName + "\n" +
            "<b>Canvas Size:</b> " + canvasSize.x.ToString("F2") + " × " + canvasSize.y.ToString("F2") + " m\n" +
            "<b>Particles:</b> " + (fluidStage != null ? fluidStage.particleCount.ToString() : "0");
    }

    private TextMeshProUGUI CreateText(
        Transform parent,
        string text,
        float fontSize,
        Color color,
        FontStyles style
    )
    {
        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );

        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.fontStyle = style;
        label.raycastTarget = false;
        return label;
    }

    private void CreateResultsButton(
        Transform parent,
        string label,
        Vector2 anchoredPosition,
        UnityEngine.Events.UnityAction action
    )
    {
        GameObject buttonObject = new GameObject(
            "Button_" + label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );

        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.0f);
        rect.anchorMax = new Vector2(0.5f, 0.0f);
        rect.pivot = new Vector2(0.5f, 0.0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(280.0f, 46.0f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = accentColor;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        TextMeshProUGUI buttonText = CreateText(
            buttonObject.transform,
            label,
            15,
            Color.white,
            FontStyles.Bold
        );

        RectTransform textRect = buttonText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        buttonText.alignment = TextAlignmentOptions.Center;
    }

    private string GetResultsFolder()
    {
        return Path.Combine(
            Application.persistentDataPath,
            "SwingingPaintBucketResults",
            "Experiments"
        );
    }

    private void OnDestroy()
    {
        if (previewTexture != null)
            Destroy(previewTexture);
    }
}

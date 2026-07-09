using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class SwingingBucketStage01 : MonoBehaviour
{
    public void Stage01_SetStartAngleAndPreview(
    float angleDegrees
)
    {
        startAngleDegrees = Mathf.Clamp(
            angleDegrees,
            0.0f,
            85.0f
        );

        /*
         * عند استخدام Start Angle يجب ألا نبدأ
         * من موضع الدلو الحالي، لأن ذلك يلغي تأثير الزاوية.
         */
        startFromCurrentHandlePosition = false;

        /*
         * لا نغيّر وضع البداية أثناء حركة التجربة.
         * يتم التطبيق المباشر فقط عندما تكون المحاكاة متوقفة.
         */
        if (
            Application.isPlaying &&
            !simulationRunning &&
            !isGrabbed
        )
        {
            Stage01_ResetSimulation();
            Stage01_SetSimulationRunning(false);
        }
    }

    [Header("STAGE 01 - References")]
    public Transform fixedAnchorPoint;
    public Transform bucketRoot;
    public Transform bucketHandlePoint;

    [Header("STAGE 01 - Mass")]
    [Min(0.01f)]
    public float bucketDryMassKg = 2.0f;

    [Min(0.0f)]
    public float paintMassKg = 3.0f;

    [Tooltip("How chunky the rope is per meter. e.g., 0.06 means 60g/m.")]
    [Min(0.0f)]
    public float ropeMassPerMeterKg = 0.06f;

    /// <summary>
    /// ///////////محمد
    /// </summary>
    [SerializeField]
    private bool simulationRunning;

    public bool IsSimulationRunning =>
        simulationRunning;

    public void Stage01_SetSimulationRunning(
        bool running
    )
    {
        simulationRunning = running;

        // عند الإيقاف نمسح الزمن الجزئي المتراكم،
        // حتى لا تحدث قفزة عند المتابعة.
        if (!running)
        {
            accumulator = 0.0f;
            BucketVelocity = Vector3.zero;
            isGrabbed = false;
        }
    }
    /// <summary>
    /// ///////////محمد
    /// </summary>

    public float BucketAndPaintMassKg
    {
        get
        {
            return Mathf.Max(0.01f, bucketDryMassKg + paintMassKg);
        }
    }

    public float RopeMassKg
    {
        get
        {
            float length = currentRopeLength > 0.0f ? currentRopeLength : baseRopeLength;
            length = Mathf.Max(0.1f, length);

            return Mathf.Max(0.0f, ropeMassPerMeterKg * length);
        }
    }

    public float EffectiveMovingMassKg
    {
        get
        {
            return Mathf.Max(0.01f, BucketAndPaintMassKg + RopeMassKg * 0.5f);
        }
    }

    [Header("STAGE 01 - Rope Physics")]
    [Min(0.1f)]
    public float baseRopeLength = 3.0f;

    [Tooltip("Rope stiffness. Higher values mean less rubber-banding.")]
    [Min(10.0f)]
    public float ropeSpringConstant = 1200.0f;

    [Tooltip("How hard we enforce the rope constraint. Just leave it at 1 usually.")]
    [Range(0.0f, 1.0f)]
    public float ropeConstraintStrength = 1.0f;

    [Tooltip("Snappiness of the rope when stretching and bouncing back.")]
    [Min(0.1f)]
    public float ropeStretchResponse = 10.0f;

    [Header("STAGE 01 - Initial Motion")]
    [Range(0.0f, 85.0f)]
    public float startAngleDegrees = 35.0f;

    [Tooltip("0 is flat along X, 90 points down Z.")]
    public float startDirectionDegrees = 0.0f;

    [Tooltip("Starting kick for the bucket handle. Usually best to keep it at zero.")]
    public Vector3 initialHandleVelocity = Vector3.zero;

    [Header("STAGE 01 - Environment")]
    public float gravity = 9.81f;

    [Tooltip("Slight numerical drag to stop the physics blowing up. Keep it close to 1.")]
    [Range(0.90f, 1.0f)]
    public float numericalDampingPerStep = 0.999f;

    [Tooltip("Air resistance. Heavier stuff punches through it easier.")]
    [Min(0.0f)]
    public float airDragCoefficient = 0.08f;

    public Vector3 windAcceleration = Vector3.zero;

    [Header("STAGE 01 - Solver")]
    [Range(30, 240)]
    public int simulationHz = 120;

    [Range(1, 16)]
    public int constraintIterations = 4;

    [Range(1, 20)]
    public int maxStepsPerFrame = 8;

    [Header("STAGE 01 - Bucket Orientation")]
    public Vector3 bucketLocalUpAxis = Vector3.up;

    [Header("STAGE 01 - Manual Grab")]
    public bool allowMouseGrab = true;

    [Tooltip("Leave empty to just fallback to Camera.main.")]
    public Camera inputCamera;

    [Tooltip("Screen-space grab radius around the handle in pixels.")]
    [Range(10.0f, 200.0f)]
    public float grabScreenRadiusPixels = 80.0f;

    [Tooltip("Check this to start from the bucket's current world spot instead of forcing the start angle.")]
    public bool startFromCurrentHandlePosition = false;

    [Header("STAGE 01 - Rope Visual Mesh")]
    public bool autoCreateRopeMesh = true;

    [Tooltip("Girth of the rope. If it looks too thick, dial it down to 0.006 or something.")]
    [Min(0.001f)]
    public float ropeRadius = 0.008f;

    [Range(6, 32)]
    public int ropeMeshSegments = 10;

    public Material ropeMaterial;

    public Color runtimeRopeColor = new Color(0.35f, 0.22f, 0.12f, 1.0f);

    private const float INTERNAL_MAX_STRETCH_RATIO = 0.08f;
    private const float INTERNAL_MIN_MAX_STRETCH = 0.03f;
    private const float INTERNAL_ABSOLUTE_MAX_STRETCH = 0.35f;

    public Vector3 BucketVelocity { get; private set; }
    public Vector3 HandlePosition => handlePosition;
    public float CurrentRopeLength => currentRopeLength;
    public float CurrentRopeStretch => currentRopeLength - baseRopeLength;
    public float CurrentTensionNewton => currentTensionNewton;
    public bool IsGrabbed => isGrabbed;

    private Vector3 handlePosition;
    private Vector3 previousHandlePosition;
    private Vector3 frameStartHandlePosition;

    private Quaternion initialBucketRotation;

    private float currentRopeLength;
    private float currentTensionNewton;
    private float accumulator;

    private bool initialized;

    private GameObject ropeVisualObject;

    private Mesh ropeMesh;

    private bool isGrabbed;
    private Plane grabPlane;
    private Vector3 grabWorldOffset;
    private Vector3 grabVelocity;
    private Vector3 lastGrabHandlePosition;

    private void OnEnable()
    {
        Stage01_CreateOrRefreshRopeVisual();
        Stage01_UpdateEditorRopeVisual();
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            Stage01_ResetSimulation();

            // لا تبدأ الحركة حتى يضغط المستخدم Start.
            simulationRunning = false;
        }
    }
    private void Update()
    {
        if (Application.isPlaying)
        {
            if (!Stage01_HasValidReferences())
                return;

            if (!initialized)
            {
                Stage01_ResetSimulation();
                return;
            }

            // لا نسمح بالإمساك إلا أثناء تشغيل التجربة.
            if (simulationRunning)
            {
                Stage01_HandleManualGrabInput(
                    Time.deltaTime
                );
            }

            if (isGrabbed)
            {
                Stage01_UpdateGrabbedMotion(
                    Time.deltaTime
                );

                Stage01_ApplyBucketTransform();

                Stage01_UpdateRopeVisual(
                    handlePosition
                );
            }
            else if (simulationRunning)
            {
                Stage01_Tick(
                    Time.deltaTime
                );
            }
            else
            {
                Stage01_ApplyBucketTransform();

                Stage01_UpdateRopeVisual(
                    handlePosition
                );
            }
        }
        else
        {
            Stage01_CreateOrRefreshRopeVisual();
            Stage01_UpdateEditorRopeVisual();
        }
    }

    private void OnValidate()
    {
        baseRopeLength = Mathf.Max(0.1f, baseRopeLength);

        bucketDryMassKg = Mathf.Max(0.01f, bucketDryMassKg);
        paintMassKg = Mathf.Max(0.0f, paintMassKg);
        ropeMassPerMeterKg = Mathf.Max(0.0f, ropeMassPerMeterKg);

        ropeSpringConstant = Mathf.Max(10.0f, ropeSpringConstant);
        ropeConstraintStrength = Mathf.Clamp01(ropeConstraintStrength);
        ropeStretchResponse = Mathf.Max(0.1f, ropeStretchResponse);

        ropeRadius = Mathf.Max(0.001f, ropeRadius);
        ropeMeshSegments = Mathf.Clamp(ropeMeshSegments, 6, 32);

        simulationHz = Mathf.Clamp(simulationHz, 30, 240);
        constraintIterations = Mathf.Clamp(constraintIterations, 1, 16);
        maxStepsPerFrame = Mathf.Clamp(maxStepsPerFrame, 1, 20);

        grabScreenRadiusPixels = Mathf.Clamp(grabScreenRadiusPixels, 10.0f, 200.0f);

        Stage01_CreateOrRefreshRopeVisual();
        Stage01_UpdateEditorRopeVisual();
    }

    [ContextMenu("STAGE 01 / Reset Simulation")]
    public void Stage01_ResetSimulation()
    {
        if (!Stage01_HasValidReferences())
            return;

        initialBucketRotation = bucketRoot.rotation;

        currentRopeLength = baseRopeLength;
        currentTensionNewton = EffectiveMovingMassKg * Mathf.Abs(gravity);

        float angleRad = startAngleDegrees * Mathf.Deg2Rad;
        float directionRad = startDirectionDegrees * Mathf.Deg2Rad;

        Vector3 horizontalDirection = new Vector3(
            Mathf.Cos(directionRad),
            0.0f,
            Mathf.Sin(directionRad)
        ).normalized;

        if (startFromCurrentHandlePosition && bucketHandlePoint != null)
        {
            Vector3 fromAnchor = bucketHandlePoint.position - fixedAnchorPoint.position;

            if (fromAnchor.sqrMagnitude < 0.000001f)
                fromAnchor = Vector3.down;

            handlePosition =
                fixedAnchorPoint.position +
                fromAnchor.normalized * currentRopeLength;
        }
        else
        {
            Vector3 startOffset =
                horizontalDirection * Mathf.Sin(angleRad) * currentRopeLength +
                Vector3.down * Mathf.Cos(angleRad) * currentRopeLength;

            handlePosition = fixedAnchorPoint.position + startOffset;
        }

        currentRopeLength = Stage01_ComputeTargetRopeLength(Vector3.zero);

        Vector3 correctedDirection = handlePosition - fixedAnchorPoint.position;

        if (correctedDirection.sqrMagnitude < 0.000001f)
            correctedDirection = Vector3.down;

        handlePosition =
            fixedAnchorPoint.position +
            correctedDirection.normalized * currentRopeLength;

        float dt = 1.0f / Mathf.Max(30, simulationHz);
        previousHandlePosition = handlePosition - initialHandleVelocity * dt;

        BucketVelocity = Vector3.zero;
        accumulator = 0.0f;
        isGrabbed = false;
        initialized = true;

        Stage01_ApplyBucketTransform();
        Stage01_UpdateRopeVisual(handlePosition);
    }

    private void Stage01_Tick(float deltaTime)
    {
        if (!Stage01_HasValidReferences())
            return;

        if (!initialized)
        {
            Stage01_ResetSimulation();
            return;
        }

        deltaTime = Mathf.Min(deltaTime, 0.05f);

        float fixedDt = 1.0f / Mathf.Max(30, simulationHz);

        accumulator += deltaTime;

        int steps = 0;
        frameStartHandlePosition = handlePosition;

        while (accumulator >= fixedDt && steps < maxStepsPerFrame)
        {
            Stage01_SimulateStep(fixedDt);
            accumulator -= fixedDt;
            steps++;
        }

        if (steps >= maxStepsPerFrame)
        {
            accumulator = 0.0f;
        }

        if (steps > 0)
        {
            float simulatedTime = steps * fixedDt;
            BucketVelocity = (handlePosition - frameStartHandlePosition) / simulatedTime;
        }

        Stage01_ApplyBucketTransform();
        Stage01_UpdateRopeVisual(handlePosition);
    }

    private void Stage01_SimulateStep(float dt)
    {
        Vector3 positionDelta = handlePosition - previousHandlePosition;
        Vector3 estimatedVelocity = positionDelta / Mathf.Max(0.0001f, dt);

        float targetLength = Stage01_ComputeTargetRopeLength(estimatedVelocity);

        float stretchBlend = 1.0f - Mathf.Exp(-ropeStretchResponse * dt);
        currentRopeLength = Mathf.Lerp(currentRopeLength, targetLength, stretchBlend);

        Vector3 velocityLike = handlePosition - previousHandlePosition;
        velocityLike *= numericalDampingPerStep;

        Vector3 acceleration =
            Vector3.down * gravity +
            windAcceleration;

        float speed = estimatedVelocity.magnitude;

        if (speed > 0.0001f && airDragCoefficient > 0.0f)
        {
            Vector3 dragAcceleration =
                -estimatedVelocity.normalized *
                airDragCoefficient *
                speed *
                speed /
                EffectiveMovingMassKg;

            acceleration += dragAcceleration;
        }

        Vector3 newPosition =
            handlePosition +
            velocityLike +
            acceleration * dt * dt;

        previousHandlePosition = handlePosition;
        handlePosition = newPosition;

        for (int i = 0; i < constraintIterations; i++)
        {
            Stage01_ApplyRopeConstraint();
        }
    }

    private void Stage01_ApplyRopeConstraint()
    {
        Vector3 fromAnchor = handlePosition - fixedAnchorPoint.position;

        if (fromAnchor.sqrMagnitude < 0.000001f)
        {
            fromAnchor = Vector3.down;
        }

        Vector3 direction = fromAnchor.normalized;

        Vector3 constrainedPosition =
            fixedAnchorPoint.position +
            direction * currentRopeLength;

        handlePosition = Vector3.Lerp(
            handlePosition,
            constrainedPosition,
            ropeConstraintStrength
        );
    }

    private float Stage01_ComputeTargetRopeLength(Vector3 estimatedVelocity)
    {
        if (fixedAnchorPoint == null)
            return baseRopeLength;

        Vector3 fromAnchor = handlePosition - fixedAnchorPoint.position;

        if (fromAnchor.sqrMagnitude < 0.000001f)
        {
            fromAnchor = Vector3.down;
        }

        Vector3 ropeDirectionDown = fromAnchor.normalized;

        float verticalFactor = Vector3.Dot(ropeDirectionDown, Vector3.down);
        verticalFactor = Mathf.Max(0.0f, verticalFactor);

        float safeLength = currentRopeLength > 0.0f ? currentRopeLength : baseRopeLength;
        safeLength = Mathf.Max(0.1f, safeLength);

        float speed = estimatedVelocity.magnitude;

        float ropeMass =
            Mathf.Max(0.0f, ropeMassPerMeterKg * safeLength);

        float effectiveMass =
            Mathf.Max(0.01f, BucketAndPaintMassKg + ropeMass * 0.5f);

        float gravityTension =
            effectiveMass *
            Mathf.Abs(gravity) *
            verticalFactor;

        float centripetalTension =
            effectiveMass *
            speed *
            speed /
            safeLength;

        currentTensionNewton = Mathf.Max(
            0.0f,
            gravityTension + centripetalTension
        );

        float stretch =
            currentTensionNewton /
            Mathf.Max(10.0f, ropeSpringConstant);

        float internalMaxStretch =
            Mathf.Clamp(
                baseRopeLength * INTERNAL_MAX_STRETCH_RATIO,
                INTERNAL_MIN_MAX_STRETCH,
                INTERNAL_ABSOLUTE_MAX_STRETCH
            );

        stretch = Mathf.Clamp(stretch, 0.0f, internalMaxStretch);

        return baseRopeLength + stretch;
    }

    private void Stage01_ApplyBucketTransform()
    {
        if (bucketRoot == null || bucketHandlePoint == null || fixedAnchorPoint == null)
            return;

        Vector3 ropeUpDirection = fixedAnchorPoint.position - handlePosition;

        if (ropeUpDirection.sqrMagnitude < 0.000001f)
            ropeUpDirection = Vector3.up;

        ropeUpDirection.Normalize();

        Vector3 localUp = bucketLocalUpAxis;

        if (localUp.sqrMagnitude < 0.000001f)
            localUp = Vector3.up;

        localUp.Normalize();

        Vector3 initialUpWorld = initialBucketRotation * localUp;

        Quaternion correction =
            Quaternion.FromToRotation(initialUpWorld, ropeUpDirection);

        Quaternion desiredRotation =
            correction * initialBucketRotation;

        bucketRoot.rotation = desiredRotation;

        Vector3 handleCorrection =
            handlePosition - bucketHandlePoint.position;

        bucketRoot.position += handleCorrection;
    }

    private bool Stage01_MouseButtonDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private bool Stage01_MouseButtonUp()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(0);
#endif
    }

    private Vector2 Stage01_MouseScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
            return Vector2.zero;

        return Mouse.current.position.ReadValue();
#else
        return Input.mousePosition;
#endif
    }

    private Ray Stage01_MouseRay(Camera cam)
    {
        Vector2 mousePosition = Stage01_MouseScreenPosition();
        return cam.ScreenPointToRay(new Vector3(mousePosition.x, mousePosition.y, 0.0f));
    }

    private void Stage01_HandleManualGrabInput(float deltaTime)
    {
        if (!allowMouseGrab)
            return;

        Camera cam = Stage01_GetInputCamera();

        if (cam == null)
            return;

        if (Stage01_MouseButtonDown())
        {
            Vector3 screenPoint = cam.WorldToScreenPoint(handlePosition);

            if (screenPoint.z > 0.0f)
            {
                Vector2 mouse = Stage01_MouseScreenPosition();
                Vector2 handleScreen = new Vector2(screenPoint.x, screenPoint.y);

                float distancePixels = Vector2.Distance(mouse, handleScreen);

                if (distancePixels <= grabScreenRadiusPixels)
                {
                    Stage01_BeginGrab(cam);
                }
            }
        }

        if (isGrabbed && Stage01_MouseButtonUp())
        {
            Stage01_EndGrab();
        }
    }

    private Camera Stage01_GetInputCamera()
    {
        if (inputCamera != null)
            return inputCamera;

        return Camera.main;
    }

    private void Stage01_BeginGrab(Camera cam)
    {
        isGrabbed = true;
        accumulator = 0.0f;

        grabVelocity = BucketVelocity;
        lastGrabHandlePosition = handlePosition;

        grabPlane = new Plane(-cam.transform.forward, handlePosition);

        Ray ray = Stage01_MouseRay(cam);

        if (grabPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            grabWorldOffset = handlePosition - hitPoint;
        }
        else
        {
            grabWorldOffset = Vector3.zero;
        }

        previousHandlePosition = handlePosition;
    }

    private void Stage01_UpdateGrabbedMotion(float deltaTime)
    {
        if (!isGrabbed)
            return;

        Camera cam = Stage01_GetInputCamera();

        if (cam == null)
            return;

        float dt = Mathf.Max(0.0001f, deltaTime);

        Ray ray = Stage01_MouseRay(cam);

        if (!grabPlane.Raycast(ray, out float enter))
            return;

        Vector3 hitPoint = ray.GetPoint(enter);
        Vector3 desiredHandlePosition = hitPoint + grabWorldOffset;

        Vector3 oldPosition = handlePosition;

        Vector3 rawVelocity =
            (desiredHandlePosition - oldPosition) / dt;

        float targetLength = Stage01_ComputeTargetRopeLength(rawVelocity);

        float stretchBlend = 1.0f - Mathf.Exp(-ropeStretchResponse * dt);
        currentRopeLength = Mathf.Lerp(currentRopeLength, targetLength, stretchBlend);

        Vector3 fromAnchor =
            desiredHandlePosition - fixedAnchorPoint.position;

        if (fromAnchor.sqrMagnitude < 0.000001f)
        {
            fromAnchor = handlePosition - fixedAnchorPoint.position;

            if (fromAnchor.sqrMagnitude < 0.000001f)
                fromAnchor = Vector3.down;
        }

        handlePosition =
            fixedAnchorPoint.position +
            fromAnchor.normalized * currentRopeLength;

        grabVelocity =
            (handlePosition - lastGrabHandlePosition) / dt;

        BucketVelocity = grabVelocity;

        lastGrabHandlePosition = handlePosition;

        previousHandlePosition = handlePosition;
    }

    private void Stage01_EndGrab()
    {
        isGrabbed = false;

        float fixedDt = 1.0f / Mathf.Max(30, simulationHz);

        previousHandlePosition =
            handlePosition - grabVelocity * fixedDt;

        BucketVelocity = grabVelocity;
        accumulator = 0.0f;
    }

    private void Stage01_UpdateEditorRopeVisual()
    {
        if (Application.isPlaying)
            return;

        if (fixedAnchorPoint == null || bucketHandlePoint == null)
            return;

        Stage01_UpdateRopeVisual(bucketHandlePoint.position);
    }

    private void Stage01_CreateOrRefreshRopeVisual()
    {
        if (!autoCreateRopeMesh)
            return;

        if (ropeVisualObject == null)
        {
            Transform existing = transform.Find("Generated_Rope_Mesh_No_LineRenderer");

            if (existing != null)
            {
                ropeVisualObject = existing.gameObject;
            }
            else
            {
                ropeVisualObject = new GameObject("Generated_Rope_Mesh_No_LineRenderer");
                ropeVisualObject.transform.SetParent(transform, false);
            }
        }

        MeshFilter meshFilter = ropeVisualObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = ropeVisualObject.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer = ropeVisualObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = ropeVisualObject.AddComponent<MeshRenderer>();

        if (ropeMesh == null || ropeMesh.vertexCount != ropeMeshSegments * 2)
        {
            ropeMesh = Stage01_BuildUnitRopeMesh();
            meshFilter.sharedMesh = ropeMesh;
        }

        if (ropeMaterial != null)
        {
            meshRenderer.sharedMaterial = ropeMaterial;
        }
        else if (meshRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Standard");

            Material generatedMaterial = new Material(shader);
            generatedMaterial.name = "Runtime_Rope_Material";
            generatedMaterial.color = runtimeRopeColor;

            meshRenderer.sharedMaterial = generatedMaterial;
        }
    }

    private Mesh Stage01_BuildUnitRopeMesh()
    {
        int segments = Mathf.Clamp(ropeMeshSegments, 6, 32);

        Vector3[] vertices = new Vector3[segments * 2];
        Vector3[] normals = new Vector3[segments * 2];
        Vector2[] uvs = new Vector2[segments * 2];

        int[] triangles = new int[segments * 6];

        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / segments;
            float angle = t * Mathf.PI * 2.0f;

            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);

            int bottom = i * 2;
            int top = bottom + 1;

            vertices[bottom] = new Vector3(x, 0.0f, z);
            vertices[top] = new Vector3(x, 1.0f, z);

            Vector3 normal = new Vector3(x, 0.0f, z).normalized;

            normals[bottom] = normal;
            normals[top] = normal;

            uvs[bottom] = new Vector2(t, 0.0f);
            uvs[top] = new Vector2(t, 1.0f);
        }

        int tri = 0;

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;

            int bottomA = i * 2;
            int topA = bottomA + 1;

            int bottomB = next * 2;
            int topB = bottomB + 1;

            triangles[tri++] = bottomA;
            triangles[tri++] = topA;
            triangles[tri++] = bottomB;

            triangles[tri++] = bottomB;
            triangles[tri++] = topA;
            triangles[tri++] = topB;
        }

        Mesh mesh = new Mesh();
        mesh.name = "Procedural_Unit_Rope_Mesh";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    private void Stage01_UpdateRopeVisual(Vector3 ropeEndPosition)
    {
        if (!autoCreateRopeMesh)
            return;

        if (ropeVisualObject == null)
            Stage01_CreateOrRefreshRopeVisual();

        if (ropeVisualObject == null || fixedAnchorPoint == null)
            return;

        Vector3 start = fixedAnchorPoint.position;
        Vector3 end = ropeEndPosition;

        Vector3 ropeVector = end - start;
        float visualLength = ropeVector.magnitude;

        if (visualLength < 0.001f)
        {
            ropeVisualObject.SetActive(false);
            return;
        }

        ropeVisualObject.SetActive(true);

        Vector3 ropeDirection = ropeVector / visualLength;

        ropeVisualObject.transform.position = start;
        ropeVisualObject.transform.rotation =
            Quaternion.FromToRotation(Vector3.up, ropeDirection);

        ropeVisualObject.transform.localScale =
            new Vector3(ropeRadius, visualLength, ropeRadius);
    }

    private bool Stage01_HasValidReferences()
    {
        if (fixedAnchorPoint == null)
        {
            Debug.LogError("Stage 01 Missing: Fixed Anchor Point.");
            return false;
        }

        if (bucketRoot == null)
        {
            Debug.LogError("Stage 01 Missing: Bucket Root.");
            return false;
        }

        if (bucketHandlePoint == null)
        {
            Debug.LogError("Stage 01 Missing: Bucket Handle Point.");
            return false;
        }

        if (!bucketHandlePoint.IsChildOf(bucketRoot))
        {
            Debug.LogError("BucketHandlePoint must be inside BucketRoot.");
            return false;
        }

        return true;
    }

    private void OnDrawGizmos()
    {
        if (fixedAnchorPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(fixedAnchorPoint.position, 0.08f);
        }

        if (bucketHandlePoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(bucketHandlePoint.position, 0.06f);
        }

        if (fixedAnchorPoint != null && bucketHandlePoint != null)
        {
            Gizmos.color = Color.white;

            Vector3 end = Application.isPlaying && initialized
                ? handlePosition
                : bucketHandlePoint.position;

            Gizmos.DrawLine(fixedAnchorPoint.position, end);
        }
    }
}
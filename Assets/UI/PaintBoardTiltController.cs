using UnityEngine;

/// <summary>
/// يتحكم بميل لوحة الرسم كاملة.
/// يعمل في Edit Mode وفي Play Mode.
/// لا يحتاج Rigidbody أو Collider.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class PaintBoardTiltController : MonoBehaviour
{
    [Header("Board Root")]

    [Tooltip(
        "اسحب هنا الجذر الكامل للوحة PaintBoard. " +
        "إذا تركته فارغاً فسيستخدم الكائن الذي يحمل السكربت."
    )]
    public Transform boardRoot;

    [Header("Board Tilt")]

    [Tooltip("ميل اللوحة حول محورها المحلي X.")]
    [Range(-60.0f, 60.0f)]
    public float tiltX;

    [Tooltip("ميل اللوحة حول محورها المحلي Z.")]
    [Range(-60.0f, 60.0f)]
    public float tiltZ;

    // دوران اللوحة الأصلي قبل إضافة الميل.
    [SerializeField, HideInInspector]
    private Quaternion baseLocalRotation =
        Quaternion.identity;

    // المرجع الذي تم حفظ الدوران الأساسي له.
    [SerializeField, HideInInspector]
    private Transform capturedBoardRoot;

    [SerializeField, HideInInspector]
    private bool baseRotationCaptured;

    public float TiltX => tiltX;
    public float TiltZ => tiltZ;

    private void Reset()
    {
        boardRoot = transform;

        tiltX = 0.0f;
        tiltZ = 0.0f;

        CaptureCurrentRotationAsBase();
        ApplyTilt();
    }

    private void OnEnable()
    {
        EnsureBaseRotation();
        ApplyTilt();
    }

    private void OnValidate()
    {
        tiltX = Mathf.Clamp(
            tiltX,
            -60.0f,
            60.0f
        );

        tiltZ = Mathf.Clamp(
            tiltZ,
            -60.0f,
            60.0f
        );

        EnsureBaseRotation();
        ApplyTilt();
    }

    /// <summary>
    /// تغيير ميل X من الواجهة أثناء التشغيل.
    /// </summary>
    public void SetTiltX(float value)
    {
        tiltX = Mathf.Clamp(
            value,
            -60.0f,
            60.0f
        );

        ApplyTilt();
    }

    /// <summary>
    /// تغيير ميل Z من الواجهة أثناء التشغيل.
    /// </summary>
    public void SetTiltZ(float value)
    {
        tiltZ = Mathf.Clamp(
            value,
            -60.0f,
            60.0f
        );

        ApplyTilt();
    }

    /// <summary>
    /// تغيير الميلين معاً.
    /// </summary>
    public void SetTilt(float x, float z)
    {
        tiltX = Mathf.Clamp(
            x,
            -60.0f,
            60.0f
        );

        tiltZ = Mathf.Clamp(
            z,
            -60.0f,
            60.0f
        );

        ApplyTilt();
    }

    /// <summary>
    /// يطبّق الميل نسبة إلى الدوران الأصلي،
    /// وليس فوق الدوران السابق.
    /// لذلك لا يوجد تراكم أو زيادة غير مقصودة في الزوايا.
    /// </summary>
    public void ApplyTilt()
    {
        if (boardRoot == null)
            return;

        EnsureBaseRotation();

        Quaternion tiltRotation =
            Quaternion.Euler(
                tiltX,
                0.0f,
                tiltZ
            );

        boardRoot.localRotation =
            baseLocalRotation *
            tiltRotation;
    }

    /// <summary>
    /// احفظ دوران اللوحة الحالي كدوران أساسي.
    /// استخدمها بعد ضبط وضعية اللوحة الأصلية.
    /// </summary>
    [ContextMenu(
        "Board Tilt / Capture Current Rotation As Base"
    )]
    public void CaptureCurrentRotationAsBase()
    {
        if (boardRoot == null)
            boardRoot = transform;

        /*
         * إذا كان هناك ميل مطبق حالياً،
         * نزيله أولاً رياضياً قبل حفظ الدوران الأساسي.
         */
        Quaternion currentTilt =
            Quaternion.Euler(
                tiltX,
                0.0f,
                tiltZ
            );

        baseLocalRotation =
            boardRoot.localRotation *
            Quaternion.Inverse(currentTilt);

        capturedBoardRoot =
            boardRoot;

        baseRotationCaptured =
            true;

        ApplyTilt();
    }

    /// <summary>
    /// إعادة الميل إلى صفر مع الحفاظ على وضعية اللوحة الأصلية.
    /// </summary>
    [ContextMenu(
        "Board Tilt / Reset Tilt To Zero"
    )]
    public void ResetTiltToZero()
    {
        tiltX = 0.0f;
        tiltZ = 0.0f;

        ApplyTilt();
    }

    private void EnsureBaseRotation()
    {
        if (boardRoot == null)
            boardRoot = transform;

        if (
            !baseRotationCaptured ||
            capturedBoardRoot != boardRoot
        )
        {
            baseLocalRotation =
                boardRoot.localRotation;

            capturedBoardRoot =
                boardRoot;

            baseRotationCaptured =
                true;
        }
    }
}
using UnityEngine;

/// <summary>
/// RopeObject — الحبل كـ object مستقل بكل متغيراته ومنطقه
/// يُمثّل الحبل المرن (Spring-Damper) الذي يربط نقطة التعليق بالدلو.
/// مُصمَّم للتحكم الكامل في خصائص الحبل من Inspector أو من السكريبتات الأخرى.
/// </summary>
[System.Serializable]
public class RopeObject
{
    // ─────────────────────────────────────────────────────
    // ① المتغيرات الأساسية (تظهر في Inspector)
    // ─────────────────────────────────────────────────────

    [Header("Rope — Physical Properties")]

    [Tooltip("الطول الطبيعي للحبل (m) — L₀")]
    [Range(0.5f, 100f)]
    public float restLength = 5f;

    [Tooltip("ثابت مرونة الحبل — k (N/m)")]
    [Range(1f, 500f)]
    public float stiffness = 50f;

    [Tooltip("معامل التخميد — c (N·s/m)")]
    [Range(0f, 50f)]
    public float damping = 5f;

    [Tooltip("أقل طول يُسمح به للحبل (نسبة من L₀)")]
    [Range(0.1f, 0.9f)]
    public float minLengthRatio = 0.5f;

    [Tooltip("أطول طول يُسمح به للحبل (نسبة من L₀)")]
    [Range(1.1f, 5f)]
    public float maxLengthRatio = 2f;

    [Header("Rope — Visual Properties")]

    [Tooltip("عدد نقاط رسم الحبل")]
    [Range(4, 40)]
    public int segments = 20;

    [Tooltip("معامل الترهّل (Sag) — كلما زاد كلما تدلّى الحبل أكثر")]
    [Range(0f, 0.15f)]
    public float sagFactor = 0.04f;

    [Tooltip("عرض الحبل (سماكة موحدة)")]
    [Range(0.01f, 0.5f)]
    public float width = 0.08f;

    // الخصائص التالية مخصصة للتوافق مع الأكواد القديمة ومخفية عن الـ Inspector تلقائياً
    public float widthTop 
    { 
        get => width; 
        set => width = value; 
    }
    public float widthBottom 
    { 
        get => width; 
        set => width = value; 
    }

    [Tooltip("لون الحبل عند الاسترخاء — برتقالي افتراضي")]
    public Color colorRelaxed = new Color(0.85f, 0.55f, 0.10f); // برتقالي واضح

    [Tooltip("لون الحبل عند أقصى شدّ — أحمر شدّ")]
    public Color colorTensed = new Color(0.95f, 0.15f, 0.10f); // أحمر شدّ

    // ─────────────────────────────────────────────────────
    // ② الحالة الداخلية (runtime)
    // ─────────────────────────────────────────────────────

    public float CurrentLength { get; set; }
    public float Velocity { get; set; }

    /// نسبة الامتداد الحالي (0 = مرتخٍ، 1 = ممتد بالكامل إلى الحد الأقصى)
    public float TensionRatio => Mathf.Clamp01(
        (CurrentLength - restLength) / (restLength * (maxLengthRatio - 1f)));

    public bool IsUnderTension => CurrentLength > restLength;
    public float Extension => Mathf.Max(0f, CurrentLength - restLength);
    public float TensionForce => stiffness * (CurrentLength - restLength);

    // ─────────────────────────────────────────────────────
    // ③ التهيئة والوظائف المساعدة
    // ─────────────────────────────────────────────────────

    public void Initialize()
    {
        CurrentLength  = restLength;
        Velocity       = 0f;
    }

    public void ResetToLength(float newRestLength)
    {
        restLength     = Mathf.Max(0.5f, newRestLength);
        CurrentLength  = restLength;
        Velocity       = 0f;
    }

    public void ResetState()
    {
        CurrentLength  = restLength;
        Velocity       = 0f;
    }

    public float ComputeSagAmount()
    {
        float extension = Mathf.Max(0f, CurrentLength - restLength);
        return sagFactor * CurrentLength * Mathf.Exp(-extension * 5f);
    }

    public bool IsStateValid()
    {
        return !float.IsNaN(CurrentLength) && !float.IsNaN(Velocity)
            && !float.IsInfinity(CurrentLength) && !float.IsInfinity(Velocity);
    }

    public void RecoverFromNaN()
    {
        if (!IsStateValid())
            ResetState();
    }
}

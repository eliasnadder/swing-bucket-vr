# آخر التعديلات — Latest Changes

**التاريخ:** 28 يونيو 2026
**نوع الملف:** Full change summary for plan.md implementation + post-merge fixes

---

## ملخص التعديلات الإجمالي | Overall Summary

```
10 files changed, ~1260 insertions(+), ~170 deletions(-)
3 new files created
3 compilation errors fixed
```

### الملفات الجديدة | New Files

| الملف | السطور | الوصف |
|-------|--------|-------|
| `Assets/Scripts/ExperimentSaver.cs` | 336 | JSON experiment data export (inputs, runtime, particles, spread) |
| `Assets/Scripts/ExperimentComparer.cs` | 282 | Side-by-side comparison of two saved experiments |
| `Assets/Scripts/ReportGenerator.cs` | 204 | Markdown report builder for experiment results |

### الملفات المعدّلة | Modified Files

| الملف | الإضافات | الوصف |
|-------|---------|--------|
| `Assets/Scripts/SPHFluidSolver.cs` | +185 | Bernoulli flow rate, surface tension, particle coalescence |
| `Assets/Scripts/PaintCanvas.cs` | +55 | Cohesion/adhesion per-surface factors (Canvas/Wood/Metal/Paper) |
| `Assets/Scripts/SimulationController.cs` | +113/-5 | Multi-bucket emitters, multi-color sequencing |
| `Assets/Scripts/SimulationUIManager.cs` | +69 | 6 new sliders (pivot X/Y, bucket radius, swings, canvas W/H) |
| `Assets/Scripts/SwingingCoupledSpringPendulum.cs` | +4 | PivotX, PivotY, maxSwings public fields |
| `Assets/Scripts/CanvasExporter.cs` | +7 | Export JSON button hook |
| `Assets/Parthenon/Demo.unity` | ~22/-3 | Bucket dimensions converted to cm-scale |

### إصلاحات التجميع | Compilation Fixes (2026-06-27)

| الملف | الخطأ | الإصلاح |
|-------|--------|---------|
| `ExperimentComparer.cs:104` | CS0029: Texture2D → RenderTexture | Blit via `Graphics.Blit` lazily |
| `SPHFluidSolver.cs:444` | CS0136: `pb` variable shadowing | Renamed inner local to `candidate` |
| `SwingingCoupledSpringPendulum.cs:63` | CS0414: unused `currentSwingCount` | Removed the field |

### إصلاح انزلاق وحدات سم | cm-Scale Slider Fix (2026-06-28)

| الملف | المشكلة | الإصلاح |
|-------|---------|---------|
| `SimulationUIManager.cs` InitSliderValues | `*100f` double-converts cm→m→cm → clamp-and-overwrite | Removed `*100f` (fields already in cm) |
| `SimulationUIManager.cs` BindListeners | `/100f` writes 1/100th of slider value back | Removed `/100f` (identity passthrough) |
| `SimulationUIManager.cs` UpdateLabels | `v*1000f` assumes meters → shows 2000 mm for 2 cm hole | Changed to `v*10f` (cm→mm) |

**Root cause:** After the Demo.unity cm-migration, `bottomRadius`/`worldSize` were already stored in cm. The slider code still applied the old meter→cm conversion (`*100f` init, `/100f` bind). Unity's Slider clamped the oversized values to max, then the listener overwrote the correct cm values with the clamped ones (e.g. `bottomRadius` 18→0.5, `worldSize.x` 150→5). Same clamp-and-overwrite pattern as the earlier gravity/slider bug.

### إصلاحات التوثيق | Docs Fixes (2026-06-28)

| الملف | المشكلة | الإصلاح |
|-------|---------|---------|
| `PAINT_BEHAVIOR.md` | Old-vs-new table backwards (SwingingCoupledSpringPendulum→old, BucketPendulum→new) | Corrected: active system listed first, legacy second |
| `PAINT_BEHAVIOR.md` | BucketBuilder mislabeled as "previous system" | Moved to active system (`[RequireComponent]` of pendulum) |
| `PAINT_BEHAVIOR.md` | Pendulum section describes old BucketPendulum equation | Updated to SwingingCoupledSpringPendulum with RK4 |
| `SPH_README.md` | BucketPendulum listed as active script | Moved to legacy section; added active scripts |

---

## التفاصيل | Details

### 1. SPHFluidSolver.cs — فيزياء متقدمة 🔬

**جديد:**
- **تقريب برنولي** (`useBernoulliApproximation = true` افتراضي):
  سرعة التدفق: `v_out = sqrt(2·g_eff·h + ½·|v_tang|²)`. سرعة دلو تتأثر بقوة الجاذبية + القص.
- **توتر السطح** (`surfaceTensionCoeff` [0–100]): قوة تماسك Poly6 بين الجسيمات.
- **الاندماج** (`enableCoalescence = false` افتراضي): جسيمان قريبان + سرعة منخفضة → يندمجان في جسيم واحد متوسط.

### 2. PaintCanvas.cs — سلوك الطلاء 🎨

**جديد:**
- **التماسك** (`cohesionStrength` [0–1]): يزيد حجم البقعة على الأسطح الماصة.
- **الالتصاق** (`adhesionStrength` [0–1]): يقلل الانتشار على الأسطح غير الماصة.
- مصفوفات لكل نوع سطح: Canvas `{1.0, 0.3, 0.1, 0.9}` / Wood / Metal / Paper.

### 3. SimulationController.cs — دلاء متعددة 🪣

**جديد:**
- `extraPaintEmitters` قائمة دلاء إضافية.
- `extraEmitterColors` ألوان لكل دلو.
- تسلسل ألوان متعدد: كل دلو → `ChangePaintColor → Emit`.

### 4. SimulationUIManager.cs — عناصر تحكم جديدة 🎛️

**6 أشرطة تمرير جديدة:**

| الاسم | النطاق | الوحدة |
|--------|--------|--------|
| `xpivotSlider` | (-75, 75) | cm |
| `ypivotSlider` | (0, 200) | cm |
| `bucketRadiusSlider` | (5, 50) | cm |
| `numberOfSwingsSlider` | (0, 50) | count |
| `canvasWidthSlider` | (50, 500) | cm |
| `canvasHeightSlider` | (50, 500) | cm |

### 5. ملفات التجارب | Experiment Files 📊

- **ExperimentSaver**: يحفظ بيانات التجربة كملف JSON (> `Application.persistentDataPath/Experiments/<timestamp>.json`).
- **ExperimentComparer**: يقارن تجربتين جنبًا إلى جنب مع رسوم بيانية للفرق.
- **ReportGenerator**: يولد تقرير Markdown مع مدخلات + وقت + جسيمات + انتشار + نتيجة.

### 6. Demo.unity — إصلاح وحدات البلاستر 📐

جميع أبعاد `BucketBuilder` حُولت من متر إلى سم:
- `bottomRadius: 0.18 → 18`
- `topRadius: 0.22 → 22`
- `bucketHeight: 0.4 → 40`
- `wallThickness: 0.015 → 1.5`
- `maxPaintHeight: 0.3 → 30`

---

## ⚠️ مخاطر معروفة | Known Risks

1. **تقريب برنولي مفعل افتراضيًا** — سرعة التدفّق تتأثر بحركة الدلو. أوقفه إن شوه الرسم.
2. **انبعاث مزدوج** — `EmitParticles()` + `PaintEmitter.Emit()` يعملان معًا (~1.6× جسيمات).
3. **6 أشرطة تمرير + 3 MonoBehaviours** تحتاج توصيل في Unity Inspector قبل التشغيل.
4. **أشرطة تمرير ميتة** — `xpivotSlider`/`ypivotSlider`/`numberOfSwingsSlider` write to fields that nothing reads (pivot from Transform only, no maxSwings damping logic).

---

## تعليمات مفيدة | Useful Commands

```bash
# عرض جميع التعديلات
git diff

# عرض ملف محدد
git diff Assets/Scripts/SPHFluidSolver.cs

# حالة المستودع
git status
```

---

*Generated: 2026-06-28*

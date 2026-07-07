# Box SPH System - نظام SPH مغلق داخل صندوق

نظام محاكاة سائل SPH داخل صندوق متوازي مستطيلات، بديل لنظام الدلو والفوهة.

## 📁 الملفات الجديدة

| الملف | الوصف |
|-------|------|
| `Assets/Scripts/box/BoxContainer.cs` | يبني صندوق مجوّف (جدران + قاع، سقف مفتوح اختياري) |
| `Assets/Scripts/box/BoxFluidBoundary.cs` | يحسب اصطدام الجسيمات مع 6 جدران صندوق متحرك ودوار |
| `Assets/Scripts/box/BoxFluidSeeder.cs` | يملأ الصندوق بجسيمات SPH عند البداية |
| `Assets/Scripts/box/BoxRotationController.cs` | يدوّر الصندوق (tilt X/Z) عبر Sliders أو حركة تلقائية |
| `Assets/Scripts/box/BoxSimulationController.cs` | منسّق المحاكاة لمشهد الصندوق |
| `Assets/Scripts/box/BoxSceneSetup.cs` | سكريبت لتوليد المشهد تلقائيًا في المحرر |

## 🚀 كيفية الاستخدام

### الطريقة 1: توليد المشهد تلقائيًا (موصى بها)

1. افتح المشروع في Unity
2. اذهب إلى: **Tools > Setup Box SPH Scene**
3. سيتم إنشاء مشهد جديد باسم `BoxSPHDemo.unity` في `Assets/Parthenon/`
4. اضغط Play لتجربة النظام

### الطريقة 2: الإعداد اليدوي

1. **أنشئ مشهد جديد**
2. **أنشئ GameObject باسم `BoxContainer`** وأضف عليه:
   - `BoxContainer` (اضبط `innerSize`, `wallThickness`, `openTop`)
   - `BoxFluidSeeder` (اضبط `fillRatio`, `particleSpacing`)
   - `BoxRotationController` (اضبط `autoRotate`, `autoAmplitude`)

3. **أنشئ GameObject باسم `SPHSystem`** وأضف عليه:
   - `SPHFluidSolver` (اضبط:
     - `smoothingRadius = 8`
     - `restDensity = 1000`
     - `viscosity = 0.25`
     - `gravity = (0, -981, 0)`
     - **`useInternalEmission = false`** ⭐ مهم جدا
   )
   - `SPHRenderer` (اربط `solver`)
   - `BoxFluidBoundary` (اربط `solver` و `box`)
   - `BoxSimulationController` (اربط جميع المراجع)

4. **اربط المراجع:**
   - في `BoxFluidSeeder`: اربط `solver` و `box`
   - في `BoxFluidBoundary`: اربط `solver` و `box`
   - في `BoxSimulationController`: اربط جميع الحقول

5. **أضف كاميرا وضاءة**
6. **عطل `SimulationController`** إذا كان موجودًا في المشهد

## ⚙️ الإعدادات المهمة

### BoxContainer
- `innerSize`: الأبعاد الداخلية للصندوق (cm)
- `wallThickness`: سماكة الجدران
- `openTop`: إذا كان true، يسمح بانسكاب السائل عند ميلان شديد

### BoxFluidSeeder
- `fillRatio`: نسبة ملء الصندوق (0.0 - 1.0)
- `particleSpacing`: المسافة بين الجسيمات (يجب أن تكون أصغر من smoothingRadius)
- `jitter`: تباين عشوائي لمواقع الجسيمات
- `paintColor`: لون السائل

### SPHFluidSolver
- **`useInternalEmission = false`** ⭐ يجب تعطيله
- `smoothingRadius`: يجب أن يكون أكبر من particleSpacing
- `gravity`: (0, -981, 0) لمطابقة نظام الوحدات
- `viscosity`: التحكم في لزوجة السائل

### BoxFluidBoundary
- `restitution`: ارتداد الجسيمات عن الجدران (0.0 - 1.0)
- `friction`: احتكاك الجسيمات مع الجدران (0.0 - 1.0)
- `skin`: هامش صغير لمنع الجسيمات من الانغراس في الجدران

### BoxRotationController
- `autoRotate`: تفعيل الدوران التلقائي
- `autoAmplitude`: درجة الميلان القصوى
- `autoSpeed`: سرعة الدوران
- `tiltX`, `tiltZ`: ميلان يدوي (إذا كان autoRotate معطل)

## 🎯 الضبط (Tuning)

### إذا كان السائل "متيبس" أو لا يتحرك بسلاسة:
- **قلل `viscosity`** في SPHFluidSolver
- **قلل `restitution`** في BoxFluidBoundary
- **زود `friction`** في BoxFluidBoundary

### إذا كانت الجسيمات تتداخل مع الجدران:
- **زود `skin`** في BoxFluidBoundary
- **قلل `particleSpacing`** في BoxFluidSeeder

### إذا كان عدد الجسيمات قليل:
- **قلل `particleSpacing`** في BoxFluidSeeder
- **زود `fillRatio`** في BoxFluidSeeder

## 🔧 المتطلبات

- Unity 2021.3 أو أحدث
- URP (Universal Render Pipeline) أو Standard Shader
- جميع سكريبتات SPH الأصلية (SPHParticle, SPHKernel, SpatialHashGrid, SPHFluidSolver, SPHRenderer)

## 📝 ملاحظات

- النظام يستخدم نفس pipeline SPH الموجود، بدون أي تعديل على الملفات الأصلية
- `SPHFluidSolver` يعمل مع `useInternalEmission = false` لأنه لا يوجد فوهة
- `BoxFluidBoundary` يحول الإحداثيات إلى Local Space لحساب الاصطدام
- `BoxFluidSeeder` يملأ الصندوق بالجسيمات مرة واحدة عند البداية
- `BoxSimulationController` يستدعي `StepSimulation` و `ResolveContacts` كل FixedUpdate

## 🎨 أمثلة على الإعدادات

### سائل مائي (Water-like)
```
SPHFluidSolver:
- viscosity: 0.1
- restitution: 0.1
- friction: 0.9

BoxFluidSeeder:
- fillRatio: 0.5
- particleSpacing: 1.2
```

### سائل لزج (Honey-like)
```
SPHFluidSolver:
- viscosity: 0.5
- restitution: 0.3
- friction: 0.95

BoxFluidSeeder:
- fillRatio: 0.3
- particleSpacing: 1.8
```

### سائل متذبذب (Sloshing Demo)
```
BoxRotationController:
- autoRotate: true
- autoAmplitude: 30
- autoSpeed: 0.8

BoxFluidBoundary:
- restitution: 0.2
- friction: 0.8
```

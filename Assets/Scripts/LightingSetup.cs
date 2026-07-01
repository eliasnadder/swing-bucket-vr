using UnityEngine;
using UnityEngine.Rendering;
// ponytail: shadow tuning — directional gets soft HQ, spot gets soft MB

public class LightingSetup : MonoBehaviour
{
	public bool spawnLights = true;
	public bool spawnProbes = true;

	void Start()
	{
		if (spawnLights)
		{
			var key = SpawnLight("KeyLight_Directional", LightType.Directional,
				new Color(1f, 0.95f, 0.85f), 1.5f,
				Quaternion.Euler(50f, -30f, 0f), LightShadows.Soft);
			key.shadowStrength = 0.85f;
			key.shadowBias = 1f;
			key.shadowNormalBias = 0.5f;
			key.shadowResolution = LightShadowResolution.VeryHigh;

			var fill = SpawnLight("FillLight_Directional", LightType.Directional,
				new Color(0.6f, 0.72f, 1f), 0.8f,
				Quaternion.Euler(30f, 120f, 0f), LightShadows.None);

			var spot = SpawnLight("SpotLight_Overhead", LightType.Spot,
				new Color(1f, 0.98f, 0.92f), 1200f,
				Quaternion.identity, LightShadows.Soft);
			spot.transform.position = new Vector3(0f, 160f, -98f);
			spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
			spot.spotAngle = 90f;
			spot.range = 250f;
			spot.innerSpotAngle = 45f;
			spot.shadowStrength = 0.85f;
			spot.shadowBias = 3f;
			spot.shadowNormalBias = 2f;
		}

		if (spawnProbes)
		{
			SpawnProbe("ReflectionProbe_Bucket",
				new Vector3(-5f, 90f, -100f), new Vector3(60f, 80f, 60f));
			SpawnProbe("ReflectionProbe_Canvas",
				new Vector3(0f, 25f, -98f), new Vector3(170f, 50f, 270f));
		}

		enabled = false;
	}

	Light SpawnLight(string n, LightType t, Color c, float intensity,
		Quaternion rot, LightShadows s)
	{
		var go = new GameObject(n);
		var l = go.AddComponent<Light>();
		l.type = t;
		l.color = c;
		l.intensity = intensity;
		go.transform.rotation = rot;
		l.shadows = s;
		return l;
	}

	void SpawnProbe(string n, Vector3 pos, Vector3 size)
	{
		var go = new GameObject(n);
		go.transform.position = pos;
		var probe = go.AddComponent<ReflectionProbe>();
		probe.mode = ReflectionProbeMode.Baked;
		probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
		probe.resolution = 256;
		probe.size = size;
		// intensityMultiplier removed in Unity 6 — no-op, default 1.0 is correct
		probe.backgroundColor = new Color(0.19f, 0.30f, 0.47f, 1f);
		probe.hdr = true;
	}
}

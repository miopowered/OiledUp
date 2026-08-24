using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Ensures every generated or older lab scene receives the same clinical grade and room tone.
    /// Runtime bootstrapping keeps presentation upgrades working before the greybox scene is next
    /// rebuilt, while the builder persists the same profile for authored scenes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LabPresentation : MonoBehaviour
    {
        private VolumeProfile runtimeProfile;
        private float nextCameraCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<LabRuntime>() == null ||
                FindAnyObjectByType<LabPresentation>() != null) return;

            new GameObject("Praesentation_Oellabor").AddComponent<LabPresentation>();
        }

        private void Awake()
        {
            if (FindAnyObjectByType<LabAmbience>() == null)
                gameObject.AddComponent<LabAmbience>();

            var authoredVolume = GameObject.Find("PostProcessing_Labor")?.GetComponent<Volume>();
            if (authoredVolume == null)
            {
                runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                runtimeProfile.name = "LabVolumeProfile_Runtime";
                ConfigureProfile(runtimeProfile);

                var volume = gameObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f;
                volume.sharedProfile = runtimeProfile;
            }

            ConfigureLabCameras();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCameraCheck) return;
            nextCameraCheck = Time.unscaledTime + 1f;
            ConfigureLabCameras();
        }

        private void OnDestroy()
        {
            if (runtimeProfile == null) return;

            foreach (var component in runtimeProfile.components)
                if (component != null) Destroy(component);
            Destroy(runtimeProfile);
        }

        /// <summary>Populates a profile with the restrained §2.4 laboratory grade.</summary>
        public static void ConfigureProfile(VolumeProfile profile)
        {
            if (profile == null) return;

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.25f);
            bloom.intensity.Override(0.14f);
            bloom.scatter.Override(0.55f);
            bloom.highQualityFiltering.Override(true);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.34f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.08f);
            color.contrast.Override(7f);
            color.saturation.Override(-13f);
            color.colorFilter.Override(new Color(0.92f, 1f, 0.96f));

            var whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(-7f);
            whiteBalance.tint.Override(-5f);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
        }

        private static void ConfigureLabCameras()
        {
            foreach (var camera in FindObjectsByType<Camera>())
            {
                if (!camera.CompareTag("MainCamera")) continue;

                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }
    }
}

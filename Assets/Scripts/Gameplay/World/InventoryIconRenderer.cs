using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>Renders a fixed-orientation, transparent 2D thumbnail from a carryable's real mesh.</summary>
    public static class InventoryIconRenderer
    {
        private const int IconLayer = 31;
        private const int IconSize = 128;
        private static readonly Vector3 Anchor = new(8192f, 8192f, 8192f);

        public static Texture2D Render(Carryable item)
        {
            if (item == null) return null;

            var root = new GameObject($"{item.name}_IconPreview")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = IconLayer
            };
            root.transform.SetPositionAndRotation(Anchor, item.InventoryIconRotation);

            var previewRenderers = CopyVisuals(item, root.transform);
            if (previewRenderers.Count == 0)
            {
                Object.DestroyImmediate(root);
                return null;
            }

            Bounds bounds = previewRenderers[0].bounds;
            for (int i = 1; i < previewRenderers.Count; i++) bounds.Encapsulate(previewRenderers[i].bounds);

            var cameraObject = new GameObject("InventoryIconCamera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = IconLayer
            };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << IconLayer;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(0.05f, Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.18f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.transform.position = bounds.center - Vector3.forward * 5f;
            camera.transform.rotation = Quaternion.identity;

            var lightObject = new GameObject("InventoryIconLight")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = IconLayer
            };
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.cullingMask = 1 << IconLayer;
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

            var target = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 4
            };
            camera.targetTexture = target;

            var previous = RenderTexture.active;
            camera.Render();
            RenderTexture.active = target;
            var icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false, false)
            {
                name = $"{item.name}_InventoryIcon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            icon.ReadPixels(new Rect(0f, 0f, IconSize, IconSize), 0, 0, false);
            icon.Apply(false, false);
            RenderTexture.active = previous;

            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(root);
            return icon;
        }

        private static List<Renderer> CopyVisuals(Carryable item, Transform previewRoot)
        {
            var copies = new List<Renderer>();
            foreach (var source in item.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!item.IncludeInInventoryIcon(source)) continue;
                var filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                var visual = new GameObject(source.name) { layer = IconLayer };
                visual.hideFlags = HideFlags.HideAndDontSave;
                visual.transform.SetParent(previewRoot, false);
                visual.transform.localPosition = item.transform.InverseTransformPoint(source.transform.position);
                visual.transform.localRotation = Quaternion.Inverse(item.transform.rotation) * source.transform.rotation;
                visual.transform.localScale = RelativeScale(source.transform.lossyScale, item.transform.lossyScale);

                visual.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = visual.AddComponent<MeshRenderer>();
                copy.sharedMaterials = source.sharedMaterials;
                copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                copy.receiveShadows = false;

                var properties = new MaterialPropertyBlock();
                source.GetPropertyBlock(properties);
                copy.SetPropertyBlock(properties);
                copies.Add(copy);
            }
            return copies;
        }

        private static Vector3 RelativeScale(Vector3 child, Vector3 parent) => new(
            SafeDivide(child.x, parent.x), SafeDivide(child.y, parent.y), SafeDivide(child.z, parent.z));

        private static float SafeDivide(float value, float divisor) =>
            Mathf.Abs(divisor) > 0.00001f ? value / divisor : value;
    }
}

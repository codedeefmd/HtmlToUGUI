using UnityEngine;
using UnityEngine.UI;

namespace Xxhq.Htmltougui
{
    /// <summary>
    /// 让生成元素的背景 Image 与盒阴影使用同一套圆角轮廓。
    /// 保留 Image 的纹理、颜色和交互状态，只在 Shader 中裁剪圆角像素。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public sealed class RoundedImageEffect : BaseMeshEffect
    {
        [SerializeField] private CssCornerRadius _cornerRadius;
        [SerializeField] private float _borderWidth;
        [SerializeField] private Color _borderColor;
        [SerializeField] private bool _hideBackgroundFill;
        [SerializeField] private float _baseGraphicAlpha = 1f;

        private Image _image;
        private Material _previousMaterial;
        private Material _roundedMaterial;
        private Rect _lastRect;
        private Vector4 _lastRadii;
        private bool _hasParameters;

        public bool Configure(CssCornerRadius cornerRadius, float borderWidth = 0f,
            Color borderColor = default, bool hideBackgroundFill = false)
        {
            _cornerRadius = cornerRadius;
            _borderWidth = Mathf.Max(0, borderWidth);
            _borderColor = borderColor;
            _hideBackgroundFill = hideBackgroundFill;
            _hasParameters = false;
            EnsureMaterial();
            if (_image != null) _baseGraphicAlpha = _image.color.a;
            UpdateMaterialParameters();
            return _roundedMaterial != null;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterial();
            UpdateMaterialParameters();
        }

        protected override void OnDisable()
        {
            ReleaseMaterial();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            ReleaseMaterial();
            base.OnDestroy();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            UpdateMaterialParameters();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            UpdateMaterialParameters();
        }
#endif

        /// <summary>
        /// Image 的 uv0 要继续用于背景纹理，uv1 单独保存局部坐标供圆角 Shader 使用。
        /// </summary>
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            UIVertex vertex = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.uv1 = new Vector4(vertex.position.x, vertex.position.y, 0, 0);
                vh.SetUIVertex(vertex, i);
            }
        }

        private void EnsureMaterial()
        {
            if (_roundedMaterial != null) return;
            _image = GetComponent<Image>();
            if (_image == null) return;

            // 作者自定义材质可能承载额外效果；不覆盖其行为。
            Material current = _image.material;
            if (current != _image.defaultMaterial)
            {
                Debug.LogWarning("HtmlToUGUI 圆角背景未应用：Image 已使用自定义材质", this);
                return;
            }

            Shader shader = Resources.Load<Shader>("HtmlToUgui/BoxShape");
            if (shader == null)
            {
                Debug.LogError("HtmlToUGUI 圆角 Shader 未找到：Resources/HtmlToUgui/BoxShape", this);
                return;
            }

            _previousMaterial = null;
            _roundedMaterial = new Material(shader)
            {
                name = "HtmlToUGUI Rounded Background (Instance)",
                hideFlags = HideFlags.DontSave
            };
            _image.material = _roundedMaterial;
        }

        private void UpdateMaterialParameters()
        {
            if (_roundedMaterial == null || _image == null) return;
            Rect box = _image.rectTransform.rect;
            Vector4 radii = BorderRadiusParser.Resolve(_cornerRadius, box);
            if (!_hasParameters || box != _lastRect || radii != _lastRadii)
            {
                SetParameters(_roundedMaterial, box, radii);
                _lastRect = box;
                _lastRadii = radii;
                _hasParameters = true;
                _image.SetMaterialDirty();
            }

            // Mask 会缓存一份 StencilMaterial；尺寸变化时也要更新这份实际绘制材质。
            Material rendering = _image.materialForRendering;
            if (rendering != null && rendering != _roundedMaterial)
                SetParameters(rendering, box, radii);
        }

        private void SetParameters(Material material, Rect box, Vector4 radii)
        {
            material.SetFloat("_Mode", 0f);
            material.SetVector("_BoxRect", new Vector4(box.xMin, box.yMin, box.xMax, box.yMax));
            material.SetVector("_CornerRadii", radii);
            material.SetFloat("_BorderWidth", _borderWidth);
            material.SetColor("_BorderColor", _borderColor);
            material.SetFloat("_HideFill", _hideBackgroundFill ? 1f : 0f);
            material.SetFloat("_BaseGraphicAlpha", _baseGraphicAlpha);
        }

        private void ReleaseMaterial()
        {
            if (_roundedMaterial == null) return;
            if (_image != null && _image.material == _roundedMaterial)
                _image.material = _previousMaterial;
            if (Application.isPlaying) Destroy(_roundedMaterial);
            else DestroyImmediate(_roundedMaterial);
            _roundedMaterial = null;
            _hasParameters = false;
        }
    }
}

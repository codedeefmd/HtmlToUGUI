using UnityEngine;
using UnityEngine.UI;

namespace Xxhq.Htmltougui
{
    /// <summary>
    /// 在 Canvas 中绘制一层 CSS 盒阴影。每层独立成 Graphic，便于保留 CSS 多层阴影的绘制顺序。
    /// 材质在启用时临时创建，场景只保存 CSS 参数，重新打开和打包后都能重建。
    /// </summary>
    [ExecuteAlways]
    public sealed class BoxShadowGraphic : MaskableGraphic
    {
        [SerializeField] private Vector2 _offset;
        [SerializeField] private float _blur;
        [SerializeField] private float _spread;
        [SerializeField] private bool _inset;
        [SerializeField] private CssCornerRadius _cornerRadius;
        [SerializeField] private Color _cssColor = Color.white;

        private Material _shaderMaterial;

        public override Material material
        {
            get { return _shaderMaterial != null ? _shaderMaterial : base.material; }
            set { base.material = value; }
        }

        /// <summary>
        /// 配置已经转换为 Unity 坐标的阴影参数。阴影不接收射线，保证按钮和输入框继续响应。
        /// </summary>
        public void Configure(Vector2 offset, float blur, float spread, Color shadowColor,
            bool inset, CssCornerRadius cornerRadius)
        {
            _offset = offset;
            _blur = Mathf.Max(0, blur);
            _spread = spread;
            _inset = inset;
            _cornerRadius = cornerRadius;
            _cssColor = shadowColor;
            color = shadowColor;
            raycastTarget = false;
            UpdateMaterialParameters();
            SetVerticesDirty();
            SetMaterialDirty();
        }

        /// <summary>外阴影是目标同级节点，需单独跟随目标上的 CanvasGroup 透明度。</summary>
        public void SetTargetAlpha(float alpha)
        {
            Color visibleColor = _cssColor;
            visibleColor.a *= Mathf.Clamp01(alpha);
            if (color != visibleColor) color = visibleColor;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterial();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleaseMaterial();
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
            _blur = Mathf.Max(0, _blur);
            raycastTarget = false;
            UpdateMaterialParameters();
            SetVerticesDirty();
        }
#endif

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            EnsureMaterial();
            if (_shaderMaterial == null) return;

            Rect box = rectTransform.rect;
            if (box.width <= 0 || box.height <= 0) return;

            UpdateMaterialParameters();
            float marginX = _inset ? 0 : Mathf.Abs(_offset.x) + Mathf.Abs(_spread) + _blur + 2;
            float marginY = _inset ? 0 : Mathf.Abs(_offset.y) + Mathf.Abs(_spread) + _blur + 2;

            // 外阴影的模糊像素要落在网格内；扩大网格不改变元素自身参与布局的尺寸。
            float left = box.xMin - marginX;
            float right = box.xMax + marginX;
            float bottom = box.yMin - marginY;
            float top = box.yMax + marginY;
            Color32 vertexColor = color;
            AddVertex(vh, new Vector2(left, bottom), Vector2.zero, vertexColor);
            AddVertex(vh, new Vector2(left, top), Vector2.up, vertexColor);
            AddVertex(vh, new Vector2(right, top), Vector2.one, vertexColor);
            AddVertex(vh, new Vector2(right, bottom), Vector2.right, vertexColor);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private static void AddVertex(VertexHelper vh, Vector2 position, Vector2 uv, Color32 vertexColor)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = vertexColor;
            vertex.uv0 = uv;
            vertex.uv1 = new Vector4(position.x, position.y, 0, 0);
            vh.AddVert(vertex);
        }

        private void EnsureMaterial()
        {
            if (_shaderMaterial != null) return;
            Shader shader = Resources.Load<Shader>("HtmlToUgui/BoxShape");
            if (shader == null)
            {
                Debug.LogError("HtmlToUGUI 盒阴影 Shader 未找到：Resources/HtmlToUgui/BoxShape", this);
                return;
            }

            _shaderMaterial = new Material(shader)
            {
                name = "HtmlToUGUI Box Shadow (Instance)",
                hideFlags = HideFlags.DontSave
            };
            UpdateMaterialParameters();
            SetMaterialDirty();
        }

        private void UpdateMaterialParameters()
        {
            if (_shaderMaterial == null || rectTransform == null) return;
            Rect box = rectTransform.rect;
            Vector4 radii = BorderRadiusParser.Resolve(_cornerRadius, box);
            _shaderMaterial.SetFloat("_Mode", _inset ? 2f : 1f);
            _shaderMaterial.SetVector("_BoxRect", new Vector4(box.xMin, box.yMin, box.xMax, box.yMax));
            _shaderMaterial.SetVector("_CornerRadii", radii);
            _shaderMaterial.SetVector("_ShadowOffset", new Vector4(_offset.x, _offset.y, 0, 0));
            _shaderMaterial.SetFloat("_Blur", _blur);
            _shaderMaterial.SetFloat("_Spread", _spread);
            // Mask 使用缓存的 StencilMaterial，不能只改基础材质。
            Material rendering = materialForRendering;
            if (rendering != null && rendering != _shaderMaterial)
            {
                rendering.SetVector("_BoxRect", new Vector4(box.xMin, box.yMin, box.xMax, box.yMax));
                rendering.SetVector("_CornerRadii", radii);
                rendering.SetVector("_ShadowOffset", new Vector4(_offset.x, _offset.y, 0, 0));
                rendering.SetFloat("_Blur", _blur);
                rendering.SetFloat("_Spread", _spread);
                rendering.SetFloat("_Mode", _inset ? 2f : 1f);
            }
        }

        private void ReleaseMaterial()
        {
            if (_shaderMaterial == null) return;
            if (Application.isPlaying) Destroy(_shaderMaterial);
            else DestroyImmediate(_shaderMaterial);
            _shaderMaterial = null;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Xxhq.Htmltougui
{
    /// <summary>
    /// 将已解析的 CSS 盒阴影映射成原生 UGUI 层级，并为背景 Image 应用相同圆角。
    /// 外阴影放在目标前一个同级位置；内阴影放在背景之上、内容之下。
    /// </summary>
    public static class BoxShadowRenderer
    {
        public static void Apply(GameObject target, Dictionary<string, string> styles)
        {
            if (target == null || styles == null) return;
            RectTransform targetRect = target.GetComponent<RectTransform>();
            if (targetRect == null) return;

            CssCornerRadius cornerRadius = CssCornerRadius.FromStyles(styles);
            bool hasShadow = styles.TryGetValue("box-shadow", out string rawShadow);
            if (!cornerRadius.IsDefined && !hasShadow) return;

            // UGUI 默认不传递第二套 UV；它承载每个顶点的局部位置，供阴影和圆角稳定计算。
            Canvas canvas = target.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
                if (canvas.rootCanvas != canvas)
                    canvas.rootCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            }

            Image background = FindBackgroundImage(target);
            if (cornerRadius.IsDefined && background != null)
            {
                var rounded = background.GetComponent<RoundedImageEffect>()
                    ?? background.gameObject.AddComponent<RoundedImageEffect>();
                // 旧边框由 Outline 网格效果生成，圆角裁剪会切掉盒外的顶点。
                // 圆角背景改由同一个 Shader 绘制边框带，保持边框和阴影轮廓一致。
                Outline outline = target.GetComponent<Outline>();
                bool hasBorder = styles.ContainsKey("border") || styles.ContainsKey("border-width")
                    || styles.ContainsKey("border-color");
                float borderWidth = hasBorder && outline != null && outline.enabled
                    ? Mathf.Max(Mathf.Abs(outline.effectDistance.x), Mathf.Abs(outline.effectDistance.y)) : 0f;
                Color borderColor = outline != null ? outline.effectColor : Color.clear;
                if (styles.TryGetValue("opacity", out string borderOpacity))
                    borderColor.a *= Mathf.Clamp01(UnitParser.Parse(borderOpacity, 1f));
                // 仅声明边框的容器原本用透明 Image 承载 Outline；让网格保持可绘制，
                // Shader 仍隐藏填充，边框才不会因为顶点 alpha=0 而整块消失。
                bool hasBackgroundColor = ColorParser.TryParseBackgroundColor(styles, out Color backgroundColor);
                bool hasBackgroundUrl = ColorParser.TryParseBackgroundUrl(styles, out string backgroundUrl);
                bool transparentCssFill = hasBackgroundColor && backgroundColor.a <= 0.001f;
                bool implicitEmptyFill = !hasBackgroundColor && !hasBackgroundUrl
                    && background.color.r == 0f && background.color.g == 0f && background.color.b == 0f
                    && (background.color.a == 0f || styles.ContainsKey("opacity"));
                bool hideFill = borderWidth > 0f && background.sprite == null
                    && (transparentCssFill || implicitEmptyFill);
                if (hideFill && !styles.ContainsKey("opacity") && background.color.a == 0f)
                {
                    Color tint = background.color;
                    tint.a = 1f;
                    background.color = tint;
                }
                if (rounded.Configure(cornerRadius, borderWidth, borderColor, hideFill)
                    && borderWidth > 0f)
                    outline.enabled = false;
            }

            if (!hasShadow) return;
            Color currentColor = styles.TryGetValue("color", out string cssColor)
                ? ColorParser.Parse(cssColor, Color.black) : Color.black;
            if (!BoxShadowParser.TryParse(rawShadow, currentColor, out List<BoxShadowSpec> shadows))
            {
                Debug.LogWarning($"HtmlToUGUI 无法解析 box-shadow：{rawShadow}", target);
                return;
            }

            float opacity = styles.TryGetValue("opacity", out string opacityText)
                ? Mathf.Clamp01(UnitParser.Parse(opacityText, 1f)) : 1f;

            // CSS 列表先写的阴影在最前；UGUI 后绘制的同级元素在最前。
            // 从后往前创建外阴影，始终插到目标前一个位置，即可保留 CSS 顺序。
            var outerOrders = new int[shadows.Count];
            int outerCount = 0;
            for (int i = 0; i < shadows.Count; i++)
                if (!shadows[i].Inset) outerOrders[i] = outerCount++;
            for (int i = shadows.Count - 1; i >= 0; i--)
            {
                BoxShadowSpec spec = shadows[i];
                if (spec.Inset || targetRect.parent == null) continue;
                BoxShadowGraphic graphic = CreateLayer(target.name + "_OuterShadow_" + i,
                    targetRect.parent, spec, opacity, cornerRadius);
                RectTransform shadowRect = graphic.rectTransform;
                shadowRect.SetSiblingIndex(targetRect.GetSiblingIndex());
                graphic.gameObject.AddComponent<ShadowRectFollower>()
                    .SetTarget(targetRect, outerOrders[i]);
            }

            RectTransform insetParent = background != null ? background.rectTransform : targetRect;
            // 内阴影成为背景 Image 的第一个子节点：背景先绘制，内容仍在阴影之上。
            // 固定插入点会把后创建的图层推到前面，故从 CSS 第一层开始创建。
            for (int i = 0; i < shadows.Count; i++)
            {
                BoxShadowSpec spec = shadows[i];
                if (!spec.Inset) continue;
                BoxShadowGraphic graphic = CreateLayer(target.name + "_InsetShadow_" + i,
                    insetParent, spec, opacity, cornerRadius);
                RectTransform shadowRect = graphic.rectTransform;
                shadowRect.anchorMin = Vector2.zero;
                shadowRect.anchorMax = Vector2.one;
                shadowRect.offsetMin = Vector2.zero;
                shadowRect.offsetMax = Vector2.zero;
                shadowRect.SetSiblingIndex(0);
            }
        }

        private static BoxShadowGraphic CreateLayer(string name, Transform parent,
            BoxShadowSpec spec, float opacity, CssCornerRadius cornerRadius)
        {
            var shadowObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            shadowObject.transform.SetParent(parent, false);
            shadowObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var graphic = shadowObject.AddComponent<BoxShadowGraphic>();
            Color shadowColor = spec.Color;
            shadowColor.a *= opacity;
            // CSS 的正 Y 向下，UGUI RectTransform 的正 Y 向上。
            graphic.Configure(new Vector2(spec.X, -spec.Y), spec.Blur, spec.Spread,
                shadowColor, spec.Inset, cornerRadius);
            return graphic;
        }

        private static Image FindBackgroundImage(GameObject target)
        {
            // 与 UguiElementFactory.CreateContainer 选择背景 Image 的规则保持一致。
            return target.GetComponentInChildren<Image>();
        }
    }
}

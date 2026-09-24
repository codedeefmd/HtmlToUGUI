using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Xxhq.Htmltougui.Editor.Tests
{
    /// <summary>
    /// 验证 CSS 阴影落地后的 UGUI 层级、绘制顺序和布局跟随关系。
    /// 测试不依赖用户场景，避免改变已有 HTML 转换结果。
    /// </summary>
    public class BoxShadowRendererTests
    {
        private GameObject _canvas;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("ShadowTestCanvas", typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvas != null) Object.DestroyImmediate(_canvas);
        }

        [Test]
        public void Apply_CreatesLayeredOuterAndInsetGraphicsWithRoundedBackground()
        {
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)card.transform;
            rect.sizeDelta = new Vector2(240, 120);
            var styles = new Dictionary<string, string>
            {
                ["border-radius"] = "16px",
                ["box-shadow"] = "0 8px 24px rgba(0,0,0,.18), 0 2px 4px #0004, inset 0 0 6px #fff8"
            };

            BoxShadowRenderer.Apply(card, styles);

            Assert.AreEqual(3, _canvas.transform.childCount);
            Assert.AreEqual("Card_OuterShadow_1", _canvas.transform.GetChild(0).name);
            Assert.AreEqual("Card_OuterShadow_0", _canvas.transform.GetChild(1).name);
            Assert.AreEqual(card.transform, _canvas.transform.GetChild(2));
            Assert.AreEqual("Card_InsetShadow_2", card.transform.GetChild(0).name);
            Assert.IsNotNull(card.GetComponent<RoundedImageEffect>());
            Assert.AreEqual("UI/HtmlToUgui/BoxShape", card.GetComponent<Image>().material.shader.name);
            Assert.IsTrue((_canvas.GetComponent<Canvas>().additionalShaderChannels
                & AdditionalCanvasShaderChannels.TexCoord1) == AdditionalCanvasShaderChannels.TexCoord1);

            var outer = _canvas.transform.GetChild(1).GetComponent<BoxShadowGraphic>();
            Assert.IsFalse(outer.raycastTarget);
            Assert.AreEqual(1f, outer.material.GetFloat("_Mode"));
            Assert.AreEqual(-8f, outer.material.GetVector("_ShadowOffset").y, 0.001f);
            Assert.AreEqual(24f, outer.material.GetFloat("_Blur"), 0.001f);
            Assert.AreEqual(16f, outer.material.GetVector("_CornerRadii").x, 0.001f);

            var inset = card.transform.GetChild(0).GetComponent<BoxShadowGraphic>();
            Assert.IsFalse(inset.raycastTarget);
            Assert.AreEqual(2f, inset.material.GetFloat("_Mode"));
        }

        [Test]
        public void OuterShadow_FollowsTargetRectWithoutEnteringLayout()
        {
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = new Vector2(0.2f, 0.3f);
            rect.anchorMax = new Vector2(0.8f, 0.7f);
            rect.anchoredPosition = new Vector2(12, -8);
            rect.sizeDelta = new Vector2(-20, -10);

            BoxShadowRenderer.Apply(card, new Dictionary<string, string>
            {
                ["box-shadow"] = "4px 6px 12px rgba(0,0,0,.2)"
            });

            var shadow = _canvas.transform.GetChild(0);
            var shadowRect = (RectTransform)shadow;
            Assert.IsTrue(shadow.GetComponent<LayoutElement>().ignoreLayout);
            Assert.AreEqual(rect.anchorMin, shadowRect.anchorMin);
            Assert.AreEqual(rect.anchorMax, shadowRect.anchorMax);
            Assert.AreEqual(rect.anchoredPosition, shadowRect.anchoredPosition);

            rect.anchoredPosition = new Vector2(30, -40);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(rect.anchoredPosition, shadowRect.anchoredPosition);
        }

        [Test]
        public void OuterShadow_FollowsTargetVisibility()
        {
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(_canvas.transform, false);
            BoxShadowRenderer.Apply(card, new Dictionary<string, string>
            {
                ["box-shadow"] = "0 8px 16px rgba(0,0,0,.25)"
            });

            var shadow = _canvas.transform.GetChild(0).GetComponent<BoxShadowGraphic>();
            Assert.IsTrue(shadow.enabled);

            card.SetActive(false);
            // 同步由 Canvas 渲染前回调触发，在编辑器预览里也应生效。
            Canvas.ForceUpdateCanvases();
            Assert.IsFalse(shadow.enabled);

            card.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(shadow.enabled);

            var canvasGroup = card.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0.4f;
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(0.1f, shadow.color.a, 0.001f);
        }

        [Test]
        public void OuterShadows_FollowReparentOrderAndTargetDestruction()
        {
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(_canvas.transform, false);
            BoxShadowRenderer.Apply(card, new Dictionary<string, string>
            {
                ["box-shadow"] = "0 4px 8px #0004, 0 12px 20px #0002"
            });
            var back = _canvas.transform.GetChild(0).gameObject;
            var front = _canvas.transform.GetChild(1).gameObject;
            var group = new GameObject("Group", typeof(RectTransform));
            group.transform.SetParent(_canvas.transform, false);

            card.transform.SetParent(group.transform, false);
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(group.transform, back.transform.parent);
            Assert.AreEqual(group.transform, front.transform.parent);
            Assert.AreEqual(back.transform, group.transform.GetChild(0));
            Assert.AreEqual(front.transform, group.transform.GetChild(1));
            Assert.AreEqual(card.transform, group.transform.GetChild(2));

            card.transform.SetSiblingIndex(0);
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(back.transform, group.transform.GetChild(0));
            Assert.AreEqual(front.transform, group.transform.GetChild(1));
            Assert.AreEqual(card.transform, group.transform.GetChild(2));

            Object.DestroyImmediate(card);
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(back == null);
            Assert.IsTrue(front == null);
        }

        [Test]
        public void RoundedBorder_UsesShaderStrokeAndDisablesOldOutline()
        {
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Outline));
            card.transform.SetParent(_canvas.transform, false);
            card.GetComponent<Image>().color = Color.clear;
            var outline = card.GetComponent<Outline>();
            outline.effectDistance = new Vector2(3, 3);
            outline.effectColor = Color.red;

            BoxShadowRenderer.Apply(card, new Dictionary<string, string>
            {
                ["border-radius"] = "18px",
                ["border"] = "3px solid red",
                ["box-shadow"] = "0 6px 12px #0004"
            });

            Assert.IsFalse(outline.enabled);
            Material material = card.GetComponent<Image>().material;
            Assert.AreEqual(3f, material.GetFloat("_BorderWidth"), 0.001f);
            Assert.AreEqual(Color.red, material.GetColor("_BorderColor"));
            Assert.AreEqual(1f, material.GetFloat("_HideFill"), 0.001f);
            Assert.AreEqual(1f, card.GetComponent<Image>().color.a, 0.001f);
        }

        [Test]
        public void MaskedBackgroundAndShadow_UpdateShaderRectAfterResize()
        {
            var maskObject = new GameObject("Mask", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Mask));
            maskObject.transform.SetParent(_canvas.transform, false);
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(maskObject.transform, false);
            var rect = (RectTransform)card.transform;
            rect.sizeDelta = new Vector2(200, 100);
            BoxShadowRenderer.Apply(card, new Dictionary<string, string>
            {
                ["border-radius"] = "10%",
                ["box-shadow"] = "0 4px 12px #0004"
            });
            Canvas.ForceUpdateCanvases();

            var image = card.GetComponent<Image>();
            var shadow = maskObject.transform.GetChild(0).GetComponent<BoxShadowGraphic>();
            Assert.AreNotSame(image.material, image.materialForRendering);
            Assert.AreNotSame(shadow.material, shadow.materialForRendering);

            rect.sizeDelta = new Vector2(300, 160);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(300f, image.materialForRendering.GetVector("_BoxRect").z
                - image.materialForRendering.GetVector("_BoxRect").x, 0.001f);
            Assert.AreEqual(16f, image.materialForRendering.GetVector("_CornerRadii").x, 0.001f);
            Assert.AreEqual(300f, shadow.materialForRendering.GetVector("_BoxRect").z
                - shadow.materialForRendering.GetVector("_BoxRect").x, 0.001f);
        }

        [Test]
        public void NestedPrefabBackground_GetsRoundClipAndInsetLayer()
        {
            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(_canvas.transform, false);
            var wrapper = new GameObject("Wrapper", typeof(RectTransform));
            wrapper.transform.SetParent(card.transform, false);
            var background = new GameObject("Background", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(wrapper.transform, false);

            BoxShadowRenderer.Apply(card, new Dictionary<string, string>
            {
                ["border-radius"] = "12px",
                ["box-shadow"] = "inset 0 0 8px #0004"
            });

            Assert.IsNotNull(background.GetComponent<RoundedImageEffect>());
            Assert.IsNotNull(background.transform.GetChild(0).GetComponent<BoxShadowGraphic>());
        }

        [Test]
        public void PercentOpacity_AffectsBackgroundAndShadow()
        {
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(_canvas.transform, false);
            var styles = new Dictionary<string, string>
            {
                ["box-shadow"] = "0 4px 8px rgba(0,0,0,.5)",
                ["opacity"] = "50%"
            };
            VisualStyleApplier.ApplyOpacity(card, styles);
            BoxShadowRenderer.Apply(card, styles);

            Assert.AreEqual(0.5f, card.GetComponent<Image>().color.a, 0.001f);
            Assert.AreEqual(0.25f, _canvas.transform.GetChild(0)
                .GetComponent<BoxShadowGraphic>().color.a, 0.001f);
        }

        [Test]
        public void BorderRadius_RespondsToResizingAndClampsAdjacentCorners()
        {
            var css = CssCornerRadius.FromStyles(new Dictionary<string, string>
            {
                ["border-radius"] = "50% 20px 10px 0"
            });
            Vector4 radii = BorderRadiusParser.Resolve(css, new Rect(0, 0, 200, 100));
            Assert.AreEqual(50f, radii.x, 0.001f);
            Assert.AreEqual(20f, radii.y, 0.001f);

            Vector4 smaller = BorderRadiusParser.Resolve(css, new Rect(0, 0, 40, 20));
            Assert.LessOrEqual(smaller.x + smaller.y, 40.001f);
            Assert.LessOrEqual(smaller.x + smaller.w, 20.001f);
        }

        [Test]
        public void RoundedBackground_PreservesSpriteUvAndCarriesLocalPosition()
        {
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(_canvas.transform, false);
            BoxShadowRenderer.Apply(card, new Dictionary<string, string> { ["border-radius"] = "12px" });
            var effect = card.GetComponent<RoundedImageEffect>();
            Assert.IsNotNull(effect);

            using (var vertices = new VertexHelper())
            {
                UIVertex input = UIVertex.simpleVert;
                input.position = new Vector3(-25, 18, 0);
                input.uv0 = new Vector2(0.3f, 0.7f);
                vertices.AddVert(input);
                effect.ModifyMesh(vertices);
                UIVertex output = new UIVertex();
                vertices.PopulateUIVertex(ref output, 0);
                Assert.AreEqual(input.uv0, output.uv0);
                Assert.AreEqual(-25f, output.uv1.x, 0.001f);
                Assert.AreEqual(18f, output.uv1.y, 0.001f);
            }
        }

        [Test]
        public void Converter_CreatesShadowFromInlineHtmlStyle()
        {
            var canvasRect = _canvas.GetComponent<RectTransform>();
            Assert.IsNotNull(canvasRect);
            canvasRect.sizeDelta = new Vector2(800, 600);
            // 批处理测试没有图形设备；实例化窗口对象即可调用转换逻辑。
            var window = ScriptableObject.CreateInstance<HtmlToUguiConverter>();
            try
            {
                window.HtmlContent = "<html><body style='width:800px;height:600px'>" +
                    "<div id='card' data-u-left='100' data-u-top='100' data-u-width='240' data-u-height='120' " +
                    "style='background-color:#fff;border-radius:16px;box-shadow:0 8px 24px rgba(0,0,0,.2)'></div>" +
                    "</body></html>";
                MethodInfo convert = typeof(HtmlToUguiConverter).GetMethod("ConvertHtmlToUgui",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(convert);
                convert.Invoke(window, null);

                Transform htmlRoot = _canvas.transform.Find("HTML_Content");
                Assert.IsNotNull(htmlRoot);
                Transform body = htmlRoot.GetChild(0);
                Transform card = body.Find("card");
                Assert.IsNotNull(card);
                Assert.IsNotNull(body.Find("card_OuterShadow_0"));
                Assert.IsNotNull(card.GetComponent<RoundedImageEffect>());
            }
            finally
            {
                Object.DestroyImmediate(window);
                var eventSystem = Object.FindObjectOfType<EventSystem>();
                if (eventSystem != null) Object.DestroyImmediate(eventSystem.gameObject);
            }
        }
    }
}

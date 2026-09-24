using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Xxhq.Htmltougui.Editor.Tests
{
    /// <summary>
    /// 锁定 box-shadow 的 CSS 语法边界，确保渲染层收到稳定且已归一化的参数。
    /// </summary>
    public class BoxShadowParserTests
    {
        [Test]
        public void TryParse_MultipleLayersAndRgbaCommas_PreservesLayerOrder()
        {
            List<BoxShadowSpec> shadows;

            bool result = BoxShadowParser.TryParse(
                "1px 2px 3px #000, inset -4px 5px 0 2px rgba(10, 20, 30, 0.25)",
                Color.red,
                out shadows);

            Assert.That(result, Is.True);
            Assert.That(shadows, Has.Count.EqualTo(2));
            AssertShadow(shadows[0], 1f, 2f, 3f, 0f, Color.black, false);
            AssertShadow(shadows[1], -4f, 5f, 0f, 2f, new Color(10f / 255f, 20f / 255f, 30f / 255f, 0.25f), true);
        }

        [Test]
        public void TryParse_ColorFirstAndCalcLength_ParsesOptionalTokensInAnyOrder()
        {
            List<BoxShadowSpec> shadows;

            bool result = BoxShadowParser.TryParse(
                "blue calc(2px + 3px) -6px 4px -1px inset",
                Color.red,
                out shadows);

            Assert.That(result, Is.True);
            Assert.That(shadows, Has.Count.EqualTo(1));
            AssertShadow(shadows[0], 5f, -6f, 4f, -1f, Color.blue, true);
        }

        [Test]
        public void TryParse_CurrentColorAndImplicitColor_UseComputedStyleColor()
        {
            var computedColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            List<BoxShadowSpec> explicitShadows;
            List<BoxShadowSpec> implicitShadows;

            bool explicitResult = BoxShadowParser.TryParse("currentColor 2px -3px", computedColor, out explicitShadows);
            bool implicitResult = BoxShadowParser.TryParse("-2px 3px", computedColor, out implicitShadows);

            Assert.That(explicitResult, Is.True);
            Assert.That(implicitResult, Is.True);
            AssertColor(explicitShadows[0].Color, computedColor);
            AssertColor(implicitShadows[0].Color, computedColor);
            Assert.That(explicitShadows[0].X, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(explicitShadows[0].Y, Is.EqualTo(-3f).Within(0.0001f));
            Assert.That(implicitShadows[0].X, Is.EqualTo(-2f).Within(0.0001f));
            Assert.That(implicitShadows[0].Y, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void TryParse_None_ReturnsSuccessfulEmptyList()
        {
            List<BoxShadowSpec> shadows;

            bool result = BoxShadowParser.TryParse(" none ", Color.black, out shadows);

            Assert.That(result, Is.True);
            Assert.That(shadows, Is.Empty);
        }

        [TestCase("1px")]
        [TestCase("1px 2px -3px")]
        [TestCase("1px 2px 3px 4px 5px")]
        [TestCase("1px 2px red blue")]
        [TestCase("1px 2px, none")]
        [TestCase("none 1px 2px")]
        [TestCase("1px 2px rgba(0, 0, 0, 0.5")]
        [TestCase("calc(foo) 1px")]
        [TestCase("calc(2px +) 1px")]
        [TestCase("calc(2..3px) 1px")]
        [TestCase("garbage 1px 2px")]
        [TestCase("")]
        public void TryParse_InvalidValue_ReturnsFalseAndNoPartialResults(string value)
        {
            List<BoxShadowSpec> shadows;

            bool result = BoxShadowParser.TryParse(value, Color.black, out shadows);

            Assert.That(result, Is.False);
            Assert.That(shadows, Is.Empty);
        }

        private static void AssertShadow(
            BoxShadowSpec actual,
            float x,
            float y,
            float blur,
            float spread,
            Color color,
            bool inset)
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(y).Within(0.0001f));
            Assert.That(actual.Blur, Is.EqualTo(blur).Within(0.0001f));
            Assert.That(actual.Spread, Is.EqualTo(spread).Within(0.0001f));
            AssertColor(actual.Color, color);
            Assert.That(actual.Inset, Is.EqualTo(inset));
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }
    }
}

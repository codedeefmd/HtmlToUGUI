using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xxhq.Htmltougui
{
    /// <summary>
    /// 保留 CSS 圆角原值供运行时按当前 RectTransform 尺寸重新计算。
    /// </summary>
    [Serializable]
    public struct CssCornerRadius
    {
        public string Shorthand;
        public string TopLeft;
        public string TopRight;
        public string BottomRight;
        public string BottomLeft;

        public bool IsDefined => !string.IsNullOrWhiteSpace(Shorthand)
            || !string.IsNullOrWhiteSpace(TopLeft) || !string.IsNullOrWhiteSpace(TopRight)
            || !string.IsNullOrWhiteSpace(BottomRight) || !string.IsNullOrWhiteSpace(BottomLeft);

        public static CssCornerRadius FromStyles(Dictionary<string, string> styles)
        {
            if (styles == null) return default;
            return new CssCornerRadius
            {
                Shorthand = Get(styles, "border-radius"),
                TopLeft = Get(styles, "border-top-left-radius"),
                TopRight = Get(styles, "border-top-right-radius"),
                BottomRight = Get(styles, "border-bottom-right-radius"),
                BottomLeft = Get(styles, "border-bottom-left-radius")
            };
        }

        private static string Get(Dictionary<string, string> styles, string key)
        {
            return styles.TryGetValue(key, out string value) ? value : null;
        }
    }

    /// <summary>
    /// 将常见的 CSS 圆角简写转换为 UGUI 矩形的四个圆形角半径。
    /// 组件在尺寸变化时重新调用，百分比因而始终相对于当前元素尺寸计算。
    /// </summary>
    public static class BorderRadiusParser
    {
        /// <summary>
        /// 返回左上、右上、右下、左下四角的半径。椭圆圆角的斜杠后半段暂按圆形圆角处理。
        /// </summary>
        public static Vector4 Resolve(Dictionary<string, string> styles, Rect rect)
        {
            return Resolve(CssCornerRadius.FromStyles(styles), rect);
        }

        public static Vector4 Resolve(CssCornerRadius css, Rect rect)
        {
            if (!css.IsDefined) return Vector4.zero;

            Vector4 radii = Vector4.zero;
            if (!string.IsNullOrWhiteSpace(css.Shorthand))
                radii = ParseShorthand(css.Shorthand, rect);

            ApplyCorner(css.TopLeft, rect, ref radii, 0);
            ApplyCorner(css.TopRight, rect, ref radii, 1);
            ApplyCorner(css.BottomRight, rect, ref radii, 2);
            ApplyCorner(css.BottomLeft, rect, ref radii, 3);

            // 相邻两角之和不能大于边长；整体等比缩小保留 CSS 的圆角比例。
            float width = Mathf.Max(0, rect.width);
            float height = Mathf.Max(0, rect.height);
            float scale = 1f;
            if (radii.x + radii.y > width) scale = Mathf.Min(scale, width / (radii.x + radii.y));
            if (radii.w + radii.z > width) scale = Mathf.Min(scale, width / (radii.w + radii.z));
            if (radii.x + radii.w > height) scale = Mathf.Min(scale, height / (radii.x + radii.w));
            if (radii.y + radii.z > height) scale = Mathf.Min(scale, height / (radii.y + radii.z));
            return radii * scale;
        }

        private static void ApplyCorner(string value, Rect rect, ref Vector4 radii, int index)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            float radius = ParseLength(FirstRadiusToken(value), rect);
            radii[index] = radius;
        }

        private static Vector4 ParseShorthand(string value, Rect rect)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "none") return Vector4.zero;
            string horizontal = value.Split('/')[0];
            string[] tokens = horizontal.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 1 || tokens.Length > 4) return Vector4.zero;

            float first = ParseLength(tokens[0], rect);
            float second = tokens.Length > 1 ? ParseLength(tokens[1], rect) : first;
            float third = tokens.Length > 2 ? ParseLength(tokens[2], rect) : first;
            float fourth = tokens.Length > 3 ? ParseLength(tokens[3], rect) : second;
            return new Vector4(first, second, third, fourth);
        }

        private static string FirstRadiusToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "0";
            string[] tokens = value.Split(new[] { ' ', '\t', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0 ? tokens[0] : "0";
        }

        private static float ParseLength(string value, Rect rect)
        {
            // CSS 百分比圆角可为椭圆；当前单半径表示取矩形短边计算圆形近似值。
            float shortSide = Mathf.Min(Mathf.Max(0, rect.width), Mathf.Max(0, rect.height));
            return Mathf.Max(0, UnitParser.Parse(value, shortSide));
        }
    }
}

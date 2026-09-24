using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Xxhq.Htmltougui
{
    /// <summary>
    /// 一层 CSS box-shadow 的标准化参数。
    /// 数值统一转换为像素；Y 保留 CSS 坐标方向，由渲染层负责转换为 Unity 坐标。
    /// </summary>
    [Serializable]
    public struct BoxShadowSpec
    {
        public float X;
        public float Y;
        public float Blur;
        public float Spread;
        public Color Color;
        public bool Inset;

        public BoxShadowSpec(float x, float y, float blur, float spread, Color color, bool inset)
        {
            X = x;
            Y = y;
            Blur = blur;
            Spread = spread;
            Color = color;
            Inset = inset;
        }
    }

    /// <summary>
    /// CSS box-shadow 解析器。
    /// 负责语法拆分和参数归一化，不处理绘制顺序、坐标翻转或裁剪范围。
    /// </summary>
    public static class BoxShadowParser
    {
        private static readonly Regex s_LengthRegex = new Regex(
            @"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:px|em|rem)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 解析完整的 box-shadow 属性值。
        /// </summary>
        /// <param name="value">CSS box-shadow 属性值，可包含多层阴影。</param>
        /// <param name="currentColor">计算样式中的 color，用于缺省颜色或 currentColor。</param>
        /// <param name="shadows">按 CSS 声明顺序返回的阴影列表；none 返回空列表。</param>
        /// <returns>整个属性值是否合法；任意一层无效都会返回 false。</returns>
        public static bool TryParse(string value, Color currentColor, out List<BoxShadowSpec> shadows)
        {
            shadows = new List<BoxShadowSpec>();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
                return true;

            List<string> layers;
            if (!TrySplitTopLevel(trimmed, ',', out layers) || layers.Count == 0)
                return false;

            for (int i = 0; i < layers.Count; i++)
            {
                BoxShadowSpec shadow;
                if (!TryParseLayer(layers[i], currentColor, out shadow))
                {
                    shadows.Clear();
                    return false;
                }
                shadows.Add(shadow);
            }

            return true;
        }

        private static bool TryParseLayer(string layer, Color currentColor, out BoxShadowSpec shadow)
        {
            shadow = default(BoxShadowSpec);
            List<string> tokens;
            if (!TrySplitWhitespace(layer, out tokens) || tokens.Count == 0)
                return false;

            var lengths = new List<float>(4);
            Color shadowColor = currentColor;
            bool hasColor = false;
            bool inset = false;

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (string.Equals(token, "inset", StringComparison.OrdinalIgnoreCase))
                {
                    if (inset)
                        return false;
                    inset = true;
                    continue;
                }

                if (string.Equals(token, "currentcolor", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasColor)
                        return false;
                    shadowColor = currentColor;
                    hasColor = true;
                    continue;
                }

                // none 只允许作为整个属性值出现，不能与其他阴影层或长度混用。
                if (string.Equals(token, "none", StringComparison.OrdinalIgnoreCase))
                    return false;

                Color parsedColor;
                if (ColorParser.TryParseColor(token, out parsedColor))
                {
                    if (hasColor)
                        return false;
                    shadowColor = parsedColor;
                    hasColor = true;
                    continue;
                }

                float length;
                if (!TryParseLength(token, out length) || lengths.Count == 4)
                    return false;
                lengths.Add(length);
            }

            // CSS 至少要求水平和垂直偏移，模糊与扩散半径可省略。
            if (lengths.Count < 2)
                return false;

            float blur = lengths.Count >= 3 ? lengths[2] : 0f;
            if (blur < 0f)
                return false;

            shadow = new BoxShadowSpec(
                lengths[0],
                lengths[1],
                blur,
                lengths.Count >= 4 ? lengths[3] : 0f,
                shadowColor,
                inset);
            return true;
        }

        private static bool TryParseLength(string token, out float value)
        {
            value = 0f;
            string trimmed = token.Trim();
            if (s_LengthRegex.IsMatch(trimmed))
            {
                value = UnitParser.Parse(trimmed);
                return true;
            }

            if (!trimmed.StartsWith("calc(", StringComparison.OrdinalIgnoreCase) ||
                !trimmed.EndsWith(")", StringComparison.Ordinal) ||
                !HasBalancedParentheses(trimmed) ||
                trimmed.IndexOf('%') >= 0)
                return false;

            string expression = trimmed.Substring(5, trimmed.Length - 6);
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            // UnitParser 对无法计算的文本会回退为 0，因此先校验操作数、单位和运算符顺序。
            if (!IsValidCalcExpression(expression))
                return false;

            value = UnitParser.Parse(trimmed);
            return true;
        }

        private static bool IsValidCalcExpression(string expression)
        {
            int depth = 0;
            bool expectValue = true;
            bool sawValue = false;

            for (int i = 0; i < expression.Length;)
            {
                char c = expression[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '(')
                {
                    if (!expectValue)
                        return false;
                    depth++;
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    if (expectValue || depth == 0)
                        return false;
                    depth--;
                    expectValue = false;
                    i++;
                    continue;
                }

                if (c == '+' || c == '-' || c == '*' || c == '/')
                {
                    // 正负号可以作为一元运算符；乘除号必须位于两个操作数之间。
                    if (expectValue && c != '+' && c != '-')
                        return false;
                    expectValue = true;
                    i++;
                    continue;
                }

                if (!expectValue)
                    return false;

                int numberStart = i;
                bool hasDigit = false;
                bool hasDot = false;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                {
                    if (expression[i] == '.')
                    {
                        if (hasDot)
                            return false;
                        hasDot = true;
                    }
                    else
                    {
                        hasDigit = true;
                    }
                    i++;
                }

                if (!hasDigit || i == numberStart)
                    return false;

                int unitStart = i;
                while (i < expression.Length && char.IsLetter(expression[i]))
                    i++;
                string unit = expression.Substring(unitStart, i - unitStart);
                if (unit.Length > 0 &&
                    !string.Equals(unit, "px", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(unit, "em", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(unit, "rem", StringComparison.OrdinalIgnoreCase))
                    return false;

                sawValue = true;
                expectValue = false;
            }

            return sawValue && !expectValue && depth == 0;
        }

        /// <summary>
        /// 按顶层分隔符拆分，函数括号内的 rgba/calc 逗号不会被误认为阴影层边界。
        /// </summary>
        private static bool TrySplitTopLevel(string value, char separator, out List<string> parts)
        {
            parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '(')
                    depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0)
                        return false;
                }
                else if (c == separator && depth == 0)
                {
                    string part = value.Substring(start, i - start).Trim();
                    if (part.Length == 0)
                        return false;
                    parts.Add(part);
                    start = i + 1;
                }
            }

            if (depth != 0)
                return false;

            string last = value.Substring(start).Trim();
            if (last.Length == 0)
                return false;
            parts.Add(last);
            return true;
        }

        private static bool TrySplitWhitespace(string value, out List<string> tokens)
        {
            tokens = new List<string>();
            var current = new StringBuilder();
            int depth = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '(')
                {
                    depth++;
                    current.Append(c);
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0)
                        return false;
                    current.Append(c);
                }
                else if (char.IsWhiteSpace(c) && depth == 0)
                {
                    AddToken(current, tokens);
                }
                else
                {
                    current.Append(c);
                }
            }

            if (depth != 0)
                return false;
            AddToken(current, tokens);
            return true;
        }

        private static void AddToken(StringBuilder current, List<string> tokens)
        {
            if (current.Length == 0)
                return;
            tokens.Add(current.ToString());
            current.Length = 0;
        }

        private static bool HasBalancedParentheses(string value)
        {
            int depth = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '(')
                    depth++;
                else if (value[i] == ')' && --depth < 0)
                    return false;
            }
            return depth == 0;
        }
    }
}

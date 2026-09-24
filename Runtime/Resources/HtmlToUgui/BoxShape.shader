Shader "UI/HtmlToUgui/BoxShape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // 0 绘制背景，1 绘制外阴影，2 绘制内阴影。几何尺寸由 UGUI Graphic 提供。
        _Mode ("Box Mode", Float) = 0
        _BoxRect ("Box Rect", Vector) = (0, 0, 100, 100)
        _CornerRadii ("Corner Radii TL TR BR BL", Vector) = (0, 0, 0, 0)
        _ShadowOffset ("Shadow Offset", Vector) = (0, 0, 0, 0)
        _Blur ("Blur", Float) = 0
        _Spread ("Spread", Float) = 0
        _BorderWidth ("Border Width", Float) = 0
        _BorderColor ("Border Color", Color) = (0, 0, 0, 0)
        _HideFill ("Hide Background Fill", Float) = 0
        _BaseGraphicAlpha ("Initial Graphic Alpha", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _BoxRect;
            float4 _CornerRadii;
            float4 _ShadowOffset;
            float _Mode;
            float _Blur;
            float _Spread;
            float _BorderWidth;
            fixed4 _BorderColor;
            float _HideFill;
            float _BaseGraphicAlpha;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                // 本地坐标由 Graphic 的顶点数据携带，Canvas 合批或平移都不会改变它。
                output.localPosition = input.localPosition;
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            // 先处理四角的圆形区域，其余位置使用普通矩形距离。
            // 不能按中心象限选角：合法的单角半径可能大于矩形短边的一半。
            float RoundedRectDistance(float2 position)
            {
                float left = _BoxRect.x;
                float bottom = _BoxRect.y;
                float right = _BoxRect.z;
                float top = _BoxRect.w;
                float tl = max(_CornerRadii.x, 0);
                float tr = max(_CornerRadii.y, 0);
                float br = max(_CornerRadii.z, 0);
                float bl = max(_CornerRadii.w, 0);

                if (tl > 0 && position.x < left + tl && position.y > top - tl)
                    return length(position - float2(left + tl, top - tl)) - tl;
                if (tr > 0 && position.x > right - tr && position.y > top - tr)
                    return length(position - float2(right - tr, top - tr)) - tr;
                if (br > 0 && position.x > right - br && position.y < bottom + br)
                    return length(position - float2(right - br, bottom + br)) - br;
                if (bl > 0 && position.x < left + bl && position.y < bottom + bl)
                    return length(position - float2(left + bl, bottom + bl)) - bl;

                float2 center = (_BoxRect.xy + _BoxRect.zw) * 0.5;
                float2 halfSize = (_BoxRect.zw - _BoxRect.xy) * 0.5;
                float2 q = abs(position - center) - halfSize;
                return length(max(q, 0)) + min(max(q.x, q.y), 0);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float baseDistance = RoundedRectDistance(input.localPosition);
                float edge = max(fwidth(baseDistance), 0.001);
                fixed4 result;

                if (_Mode < 0.5)
                {
                    result = tex2D(_MainTex, input.texcoord) * input.color;
                    result.a *= 1 - _HideFill;
                    // 圆角边框在盒内绘制，避免 Outline 的盒外网格被裁掉。
                    float borderBlend = _BorderWidth > 0
                        ? smoothstep(-_BorderWidth - edge, -_BorderWidth + edge, baseDistance) : 0;
                    fixed4 border = _BorderColor;
                    border.a = saturate(border.a * input.color.a / max(_BaseGraphicAlpha, 0.001));
                    result = lerp(result, border, borderBlend);
                    result.a *= 1 - smoothstep(-edge, edge, baseDistance);
                }
                else
                {
                    float shiftedDistance = RoundedRectDistance(input.localPosition - _ShadowOffset.xy);
                    float softness = max(_Blur * 0.5, edge);
                    result = input.color;

                    if (_Mode < 1.5)
                    {
                        // CSS 外阴影绘制在边框盒外；内部挖空，避免盖住透明背景。
                        float shadowAlpha = 1 - smoothstep(-softness, softness, shiftedDistance - _Spread);
                        float outside = smoothstep(-edge, edge, baseDistance);
                        result.a *= shadowAlpha * outside;
                    }
                    else
                    {
                        // 内阴影只保留盒内、但位于偏移后内轮廓外的区域。
                        float inside = 1 - smoothstep(-edge, edge, baseDistance);
                        float shadowAlpha = smoothstep(-softness, softness, shiftedDistance + _Spread);
                        result.a *= shadowAlpha * inside;
                    }
                }

                #ifdef UNITY_UI_CLIP_RECT
                    result.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDCG
        }
    }
}

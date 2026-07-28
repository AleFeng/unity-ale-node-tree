Shader "NodeTree/NodeLineFlow"
{
    Properties
    {
        _MainTex    ("Main Texture", 2D)          = "white" {}
        _MainTexSpeed ("Main Tex UV Speed (X,Y)", Vector) = (0, 0.2, 0, 0)
        
        _FlowTex    ("Flow Texture", 2D)          = "white" {}
        _FlowColor  ("Flow Color (HDR)", Color)   = (0, 1, 1, 1)
        _FlowSpeed  ("Flow Speed", Float)         = 1.0
        
        _EdgeFade   ("Edge Fade Width", Range(0.01, 1)) = 0.3
        _Glow       ("Glow Intensity", Float)     = 1.5
        _Alpha      ("Global Alpha", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "NodeLineFlow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_FlowTex);
            SAMPLER(sampler_FlowTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _FlowTex_ST;
                float4 _FlowColor;
                float  _FlowSpeed;
                float  _EdgeFade;
                float  _Glow;
                float  _Alpha;
                float2 _MainTexSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  fogFactor   : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float uvX = IN.uv.x;
                float uvY = IN.uv.y;

                // 边缘渐变 alpha（两侧 smoothstep 相乘）
                float edgeAlpha = smoothstep(0.0, _EdgeFade, uvX)
                                * smoothstep(0.0, _EdgeFade, 1.0 - uvX);

                // 流动纹理采样（UV.y 随时间滚动），采样结果作为偏移量扰动 mainUV
                float2 flowUV = float2(uvX, uvY - _Time.y * _FlowSpeed);
                flowUV = TRANSFORM_TEX(float4(flowUV, 0, 0), _FlowTex).xy;
                half4 flowTex = SAMPLE_TEXTURE2D(_FlowTex, sampler_FlowTex, flowUV);

                // 用流动纹理的 rg 通道偏移主纹理 UV（映射到 [-0.5, 0.5]）
                float2 mainUV = TRANSFORM_TEX(IN.uv, _MainTex);
                mainUV += -_Time.y * _MainTexSpeed;
                mainUV += (flowTex.rg - 0.5) * _FlowSpeed * 0.1;
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, mainUV);

                // 最终颜色：主纹理颜色 * HDR 颜色 * 发光强度
                half3 col = mainTex.rgb * _FlowColor.rgb * _Glow;

                // 综合 alpha
                half alpha = edgeAlpha * mainTex.a * _FlowColor.a * _Alpha;

                half4 finalColor = half4(col, alpha);

                // Fog
                finalColor.rgb = MixFog(finalColor.rgb, IN.fogFactor);

                return finalColor;
            }
            ENDHLSL
        }
    }

    // Built-in 回退（非 URP 项目中仍可编译，使用 Unlit 透明）
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

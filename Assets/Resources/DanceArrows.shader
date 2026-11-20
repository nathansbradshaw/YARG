Shader "YARG/DanceArrows"
{
    Properties
    {
        _Tint ("Note color", Color) = (1,1,1,1)
        _Alpha ("Opacity", Float) = 1
        _Glow ("Hit glow", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/Art/Shaders/highways.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Alpha;
                half _Glow;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float tint : TEXCOORD0;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = YargTransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                output.color = input.color;
                output.tint = input.uv.x;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half3 rgb = lerp(input.color.rgb, input.color.rgb * _Tint.rgb, input.tint);
                return half4(SRGBToLinear(rgb) * (1 + _Glow), input.color.a * _Alpha);
            }
            ENDHLSL
        }
    }
}

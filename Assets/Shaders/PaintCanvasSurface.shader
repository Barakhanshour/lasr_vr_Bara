Shader "Custom/PaintCanvasSurface"
{
    Properties
    {
        _BaseMap ("Canvas Base Texture", 2D) = "white" {}
        _BaseColor ("Canvas Base Color", Color) = (1,1,1,1)

        _PaintColorMap ("Paint Color Map", 2D) = "black" {}
        _PaintThicknessMap ("Paint Thickness Map", 2D) = "black" {}
        _PaintWetnessMap ("Paint Wetness Map", 2D) = "black" {}

        _ThicknessVisibility ("Thickness Visibility", Float) = 5
        _ReliefStrength ("Paint Relief", Range(0,5)) = 1.25
        _DryPaintSmoothness ("Dry Paint Smoothness", Range(0,1)) = 0.25
        _WetPaintSmoothness ("Wet Paint Smoothness", Range(0,1)) = 0.92
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_PaintColorMap);
            SAMPLER(sampler_PaintColorMap);

            TEXTURE2D(_PaintThicknessMap);
            SAMPLER(sampler_PaintThicknessMap);

            TEXTURE2D(_PaintWetnessMap);
            SAMPLER(sampler_PaintWetnessMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _ThicknessVisibility;
                float _ReliefStrength;
                float _DryPaintSmoothness;
                float _WetPaintSmoothness;
                float4 _PaintTexelSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 tangentWS : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(
                        input.normalOS,
                        input.tangentOS
                    );

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                float4 baseSample =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        uv
                    ) *
                    _BaseColor;

                float4 paintSample =
                    SAMPLE_TEXTURE2D(
                        _PaintColorMap,
                        sampler_PaintColorMap,
                        uv
                    );

                float thickness =
                    SAMPLE_TEXTURE2D(
                        _PaintThicknessMap,
                        sampler_PaintThicknessMap,
                        uv
                    ).r;

                float wetness =
                    SAMPLE_TEXTURE2D(
                        _PaintWetnessMap,
                        sampler_PaintWetnessMap,
                        uv
                    ).r;

                float paintMask =
                    saturate(
                        thickness *
                        _ThicknessVisibility
                    );

                float thicknessRight =
                    SAMPLE_TEXTURE2D(
                        _PaintThicknessMap,
                        sampler_PaintThicknessMap,
                        uv + float2(_PaintTexelSize.x, 0)
                    ).r;

                float thicknessUp =
                    SAMPLE_TEXTURE2D(
                        _PaintThicknessMap,
                        sampler_PaintThicknessMap,
                        uv + float2(0, _PaintTexelSize.y)
                    ).r;

                float2 gradient =
                    float2(
                        thicknessRight - thickness,
                        thicknessUp - thickness
                    );

                float3 normalTS =
                    normalize(
                        float3(
                            -gradient.x * _ReliefStrength,
                            -gradient.y * _ReliefStrength,
                            1.0
                        )
                    );

                float3 normalWS =
                    normalize(
                        input.tangentWS * normalTS.x +
                        input.bitangentWS * normalTS.y +
                        input.normalWS * normalTS.z
                    );

                float3 surfaceColor =
                    lerp(
                        baseSample.rgb,
                        paintSample.rgb,
                        paintMask
                    );

                Light mainLight =
                    GetMainLight();

                float ndotl =
                    saturate(
                        dot(
                            normalWS,
                            mainLight.direction
                        )
                    );

                float3 diffuse =
                    surfaceColor *
                    (
                        0.18 +
                        ndotl *
                        mainLight.color
                    );

                float3 viewDirection =
                    normalize(
                        GetCameraPositionWS() -
                        input.positionWS
                    );

                float3 halfDirection =
                    normalize(
                        mainLight.direction +
                        viewDirection
                    );

                float smoothness =
                    lerp(
                        _DryPaintSmoothness,
                        _WetPaintSmoothness,
                        wetness
                    );

                float specularPower =
                    lerp(
                        12.0,
                        128.0,
                        smoothness
                    );

                float specular =
                    pow(
                        saturate(
                            dot(
                                normalWS,
                                halfDirection
                            )
                        ),
                        specularPower
                    ) *
                    smoothness *
                    paintMask;

                float3 finalColor =
                    diffuse +
                    mainLight.color *
                    specular;

                return half4(finalColor, 1.0);
            }

            ENDHLSL
        }
    }
}

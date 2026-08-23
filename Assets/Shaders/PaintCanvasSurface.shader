Shader "Custom/PaintCanvasSurface"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)

        [HideInInspector] _PaintColorMap ("Paint Color", 2D) = "black" {}
        [HideInInspector] _PaintThicknessMap ("Paint Thickness", 2D) = "black" {}
        [HideInInspector] _PaintWetnessMap ("Paint Wetness", 2D) = "black" {}
        [HideInInspector] _PaintAgeMap ("Paint Age", 2D) = "black" {}

        _PigmentOpticalExtinctionPerMeter ("Pigment Optical Extinction (1/m)", Float) = 45000
        _ReliefStrength ("Relief Strength", Range(0,5)) = 1.0
        _DryPaintSmoothness ("Dry Paint Smoothness", Range(0,1)) = 0.25
        _WetPaintSmoothness ("Wet Paint Smoothness", Range(0,1)) = 0.92

        [HideInInspector] _DryingDelaySeconds ("Drying Delay Seconds", Float) = 60
        [HideInInspector] _MaxDryColorShiftFraction ("Max Dry Color Shift", Range(0,0.1)) = 0.08
        [HideInInspector] _PaintTexelSize ("Paint Texel Size", Vector) = (0.001,0.001,1024,1024)
        [HideInInspector] _PaintMetersPerTexel ("Paint Meters Per Texel", Vector) = (0.004,0.004,0,0)
        [HideInInspector] _PaintLocalCenter ("Paint Local Center", Vector) = (0,0,0,1)
        [HideInInspector] _PaintLocalAxisU ("Paint Local Axis U", Vector) = (1,0,0,0)
        [HideInInspector] _PaintLocalAxisV ("Paint Local Axis V", Vector) = (0,0,1,0)
        [HideInInspector] _PaintLocalHalfSize ("Paint Local Half Size", Vector) = (5,5,0,0)

        [Toggle] _DebugMapping ("Debug Paint Mapping", Float) = 0
        [Toggle] _DebugDrying ("Debug Paint Drying", Float) = 0
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
            TEXTURE2D(_PaintAgeMap);
            SAMPLER(sampler_PaintAgeMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _PigmentOpticalExtinctionPerMeter;
                float _ReliefStrength;
                float _DryPaintSmoothness;
                float _WetPaintSmoothness;
                float4 _PaintTexelSize;
                float4 _PaintMetersPerTexel;
                float4 _PaintLocalCenter;
                float4 _PaintLocalAxisU;
                float4 _PaintLocalAxisV;
                float4 _PaintLocalHalfSize;
                float _DryingDelaySeconds;
                float _MaxDryColorShiftFraction;
                float _DebugMapping;
                float _DebugDrying;
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
                float2 baseUV : TEXCOORD4;
                float3 positionOS : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;
                output.baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float2 ComputePaintUV(float3 positionOS)
            {
                float3 relativePosition = positionOS - _PaintLocalCenter.xyz;
                float halfWidth = max(1e-6, _PaintLocalHalfSize.x);
                float halfHeight = max(1e-6, _PaintLocalHalfSize.y);
                float u = dot(relativePosition, _PaintLocalAxisU.xyz);
                float v = dot(relativePosition, _PaintLocalAxisV.xyz);
                return float2(u / (halfWidth * 2.0) + 0.5, v / (halfHeight * 2.0) + 0.5);
            }

            float3 ApplyMappingDiagnostic(float3 color, float2 paintUV)
            {
                if (_DebugMapping < 0.5)
                    return color;

                float distanceToBorder = min(min(paintUV.x, 1.0 - paintUV.x), min(paintUV.y, 1.0 - paintUV.y));
                float border = 1.0 - smoothstep(0.006, 0.018, distanceToBorder);
                float centerU = 1.0 - smoothstep(0.002, 0.006, abs(paintUV.x - 0.5));
                float centerV = 1.0 - smoothstep(0.002, 0.006, abs(paintUV.y - 0.5));
                float qU = min(abs(paintUV.x - 0.25), abs(paintUV.x - 0.75));
                float qV = min(abs(paintUV.y - 0.25), abs(paintUV.y - 0.75));
                float quarterLines = max(1.0 - smoothstep(0.0015, 0.0045, qU), 1.0 - smoothstep(0.0015, 0.0045, qV));

                color = lerp(color, float3(1,1,0), border * 0.88);
                color = lerp(color, float3(1,0.05,0.05), centerU * 0.90);
                color = lerp(color, float3(0.05,0.25,1), centerV * 0.90);
                color = lerp(color, float3(0.85,0.85,0.85), quarterLines * 0.38);
                return color;
            }

            float3 ApplyDryingDiagnostic(float3 color, float paintMask, float wetness, float ageSeconds)
            {
                if (_DebugDrying < 0.5 || paintMask <= 0.01)
                    return color;

                float delaySeconds = max(0.001, _DryingDelaySeconds);
                if (ageSeconds < delaySeconds)
                {
                    // With Phase 3D, magenta before the delay is a hard failure.
                    if (wetness < 0.999)
                        return float3(1,0,1);

                    float age01 = saturate(ageSeconds / delaySeconds);
                    return lerp(float3(0,0.85,1), float3(0,1,0.25), age01);
                }

                float dryness = saturate(1.0 - wetness);
                return lerp(float3(1,0.95,0.05), float3(1,0.15,0.02), saturate(dryness * 5.0));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 paintUV = saturate(ComputePaintUV(input.positionOS));
                float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV) * _BaseColor;
                float3 paintColor = saturate(SAMPLE_TEXTURE2D(_PaintColorMap, sampler_PaintColorMap, paintUV).rgb);
                float thickness = max(0.0, SAMPLE_TEXTURE2D(_PaintThicknessMap, sampler_PaintThicknessMap, paintUV).r);
                float wetness = saturate(SAMPLE_TEXTURE2D(_PaintWetnessMap, sampler_PaintWetnessMap, paintUV).r);
                float ageSeconds = max(0.0, SAMPLE_TEXTURE2D(_PaintAgeMap, sampler_PaintAgeMap, paintUV).r);

                // Beer-Lambert optical coverage from PHYSICAL film thickness in meters.
                // Visibility depends only on conserved pigment-film thickness, never on wetness.
                // Therefore drying cannot erase a painted mark.
                float paintMask = saturate(1.0 - exp(-max(1.0, _PigmentOpticalExtinctionPerMeter) * thickness));

                float2 du = float2(_PaintTexelSize.x, 0);
                float2 dv = float2(0, _PaintTexelSize.y);
                float hL = SAMPLE_TEXTURE2D(_PaintThicknessMap, sampler_PaintThicknessMap, saturate(paintUV - du)).r;
                float hR = SAMPLE_TEXTURE2D(_PaintThicknessMap, sampler_PaintThicknessMap, saturate(paintUV + du)).r;
                float hD = SAMPLE_TEXTURE2D(_PaintThicknessMap, sampler_PaintThicknessMap, saturate(paintUV - dv)).r;
                float hU = SAMPLE_TEXTURE2D(_PaintThicknessMap, sampler_PaintThicknessMap, saturate(paintUV + dv)).r;

                float dx = max(1e-6, _PaintMetersPerTexel.x);
                float dy = max(1e-6, _PaintMetersPerTexel.y);
                float2 physicalSlope = float2((hR - hL) / (2.0 * dx), (hU - hD) / (2.0 * dy));

                // Real film slopes can be numerically steep at a one-pixel edge.
                // Bound only the VISUAL normal perturbation; the physics keeps full thickness.
                float2 visualSlope = clamp(physicalSlope, -0.35, 0.35) * _ReliefStrength;
                float3 normalTS = normalize(float3(-visualSlope.x, -visualSlope.y, 1.0));
                float3 normalWS = normalize(
                    input.tangentWS * normalTS.x +
                    input.bitangentWS * normalTS.y +
                    input.normalWS * normalTS.z
                );

                float dryness = saturate(1.0 - wetness);
                float dryColorFactor = 1.0 - saturate(_MaxDryColorShiftFraction) * dryness;
                float3 displayedPaintColor = paintColor * dryColorFactor;

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float lightAttenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                // Keep the wood naturally lit, but preserve paint pigment albedo.
                // Directional-light intensity must not bleach yellow/green/red paint to white
                // or darken it toward black. The paint receives only a very small neutral
                // directional shade plus a controlled wet specular highlight.
                float3 ambientGI = max(float3(0,0,0), SampleSH(normalWS));
                float3 baseLighting = ambientGI + mainLight.color * ndotl * lightAttenuation;
                float3 litBaseColor = baseSample.rgb * baseLighting;

                // float neutralPaintShade = lerp(0.94, 1.00, ndotl * lightAttenuation);
                // float3 litPaintColor = displayedPaintColor * neutralPaintShade;

                float3 litPaintColor = displayedPaintColor;


                float3 viewDirection = normalize(GetCameraPositionWS() - input.positionWS);
                float3 halfDirection = normalize(mainLight.direction + viewDirection);
                float smoothness = lerp(_DryPaintSmoothness, _WetPaintSmoothness, wetness);
                float specularPower = lerp(18.0, 72.0, smoothness);

                float highlight =
                    pow(saturate(dot(normalWS, halfDirection)), specularPower)
                    * smoothness
                    * paintMask;

                float3 paintWithHighlight =
                    saturate(litPaintColor * (1.0 + highlight * 0.22));
                // float specularPower = lerp(12.0, 64.0, smoothness);
                // float specular = pow(saturate(dot(normalWS, halfDirection)), specularPower)
                    // * smoothness * paintMask * lightAttenuation * 0.45;
                // float3 paintWithHighlight = saturate(litPaintColor + mainLight.color * specular);
                float3 finalColor = lerp(litBaseColor, paintWithHighlight, paintMask);
                finalColor = ApplyMappingDiagnostic(finalColor, paintUV);
                finalColor = ApplyDryingDiagnostic(finalColor, paintMask, wetness, ageSeconds);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}

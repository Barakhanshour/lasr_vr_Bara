Shader "Custom/GpuPaintParticleSplat_Stage04"
{
    Properties
    {
        _ParticleScale ("Particle Scale", Float) = 1.0
        _InsideAlpha ("Inside Alpha", Range(0,1)) = 0.055
        _AirborneAlpha ("Airborne Alpha", Range(0,1)) = 0.94
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "GpuPaintParticles"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct PaintParticle
            {
                float4 positionState;
                float4 velocityRadius;
                float4 colorSeed;
            };

            StructuredBuffer<PaintParticle> _Particles;

            CBUFFER_START(UnityPerMaterial)
                float _ParticleScale;
                float _InsideAlpha;
                float _AirborneAlpha;
                float4x4 _BucketLocalToWorld;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 circleUV : TEXCOORD0;
                float4 color : COLOR0;
                nointerpolation float state : TEXCOORD1;
            };

            float2 GetQuadVertex(uint vertexID)
            {
                if (vertexID == 0) return float2(-1, -1);
                if (vertexID == 1) return float2(-1,  1);
                if (vertexID == 2) return float2( 1, -1);
                if (vertexID == 3) return float2( 1, -1);
                if (vertexID == 4) return float2(-1,  1);
                return float2(1, 1);
            }

            Varyings Vert(
                uint vertexID : SV_VertexID,
                uint instanceID : SV_InstanceID
            )
            {
                Varyings output = (Varyings)0;

                PaintParticle particle =
                    _Particles[instanceID];

                float state =
                    particle.positionState.w;

                float3 centerWorld;

                if (state > 0.5 && state < 1.5)
                {
                    centerWorld =
                        mul(
                            _BucketLocalToWorld,
                            float4(
                                particle.positionState.xyz,
                                1
                            )
                        ).xyz;
                }
                else
                {
                    centerWorld =
                        particle.positionState.xyz;
                }

                float3 viewDirection =
                    normalize(
                        GetCameraPositionWS() -
                        centerWorld
                    );

                float3 axisRight =
                    normalize(
                        cross(
                            float3(0, 1, 0),
                            viewDirection
                        )
                    );

                if (dot(axisRight, axisRight) < 0.001)
                {
                    axisRight =
                        normalize(
                            cross(
                                float3(1, 0, 0),
                                viewDirection
                            )
                        );
                }

                float3 axisUp =
                    normalize(
                        cross(
                            viewDirection,
                            axisRight
                        )
                    );

                float widthMultiplier = 1.0;
                float heightMultiplier = 1.0;

                if (state >= 1.5 && state < 2.5)
                {
                    float3 velocity =
                        particle.velocityRadius.xyz;

                    float speed =
                        length(velocity);

                    float3 projectedVelocity =
                        velocity -
                        viewDirection *
                        dot(
                            velocity,
                            viewDirection
                        );

                    float projectedLength =
                        length(projectedVelocity);

                    if (projectedLength > 0.001)
                    {
                        axisUp =
                            projectedVelocity /
                            projectedLength;

                        axisRight =
                            normalize(
                                cross(
                                    axisUp,
                                    viewDirection
                                )
                            );

                        heightMultiplier =
                            1.0 +
                            saturate(speed * 0.18) *
                            2.8;

                        widthMultiplier =
                            lerp(
                                1.0,
                                0.72,
                                saturate(speed * 0.15)
                            );
                    }
                }

                float2 quad =
                    GetQuadVertex(vertexID);

                float radius =
                    max(
                        0.0001,
                        particle.velocityRadius.w *
                        _ParticleScale
                    );

                float3 worldPosition =
                    centerWorld +
                    axisRight *
                    quad.x *
                    radius *
                    widthMultiplier +
                    axisUp *
                    quad.y *
                    radius *
                    heightMultiplier;

                output.positionCS =
                    TransformWorldToHClip(
                        worldPosition
                    );

                output.circleUV = quad;
                output.state = state;

                float alpha =
                    state < 1.5
                        ? _InsideAlpha
                        : _AirborneAlpha;

                output.color =
                    float4(
                        particle.colorSeed.rgb,
                        alpha
                    );

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
           
                if (input.state >= 2.5)
                    clip(-1);

                float distanceSquared =
                    dot(
                        input.circleUV,
                        input.circleUV
                    );

                clip(1.0 - distanceSquared);

                float softEdge =
                    saturate(
                        (
                            1.0 -
                            distanceSquared
                        ) *
                        5.0
                    );

                return half4(
                    input.color.rgb,
                    input.color.a *
                    softEdge
                );
            }

            ENDHLSL
        }
    }
}

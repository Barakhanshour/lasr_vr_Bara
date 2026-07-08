Shader "Custom/GpuPaintParticleSplat"
{
    Properties
    {
        _ParticleScale("Particle Scale", Float) = 1
        _InsideAlpha("Inside Alpha", Float) = 0.045
        _OutsideAlpha("Outside Alpha", Float) = 0.92
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct PaintParticle
            {
                float4 positionState;
                float4 velocityRadius;
                float4 colorSeed;
            };

            StructuredBuffer<PaintParticle> _Particles;
            float4x4 _BucketLocalToWorld;
            float _ParticleScale;
            float _InsideAlpha;
            float _OutsideAlpha;

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 color : TEXCOORD1;
                float state : TEXCOORD2;
            };

            float2 Corner(uint vertexID)
            {
                uint id = vertexID % 6;
                if (id == 0) return float2(-1,-1);
                if (id == 1) return float2( 1,-1);
                if (id == 2) return float2( 1, 1);
                if (id == 3) return float2(-1,-1);
                if (id == 4) return float2( 1, 1);
                return float2(-1,1);
            }

            Varyings vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings o;
                PaintParticle p = _Particles[instanceID];
                float state = p.positionState.w;
                float3 center = p.positionState.xyz;
                if (state < 1.5) center = mul(_BucketLocalToWorld, float4(center,1)).xyz;
                float2 corner = Corner(vertexID);
                float radius = p.velocityRadius.w * _ParticleScale;
                if (state < 0.5) radius = 0;

                float3 camRight = normalize(float3(UNITY_MATRIX_I_V[0][0], UNITY_MATRIX_I_V[1][0], UNITY_MATRIX_I_V[2][0]));
                float3 camUp    = normalize(float3(UNITY_MATRIX_I_V[0][1], UNITY_MATRIX_I_V[1][1], UNITY_MATRIX_I_V[2][1]));
                float3 wp = center + camRight * corner.x * radius + camUp * corner.y * radius;
                o.positionHCS = TransformWorldToHClip(wp);
                o.uv = corner * 0.5 + 0.5;
                o.color = p.colorSeed.rgb;
                o.state = state;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (i.state < 0.5) discard;
                float2 c = i.uv * 2 - 1;
                float d = dot(c,c);
                if (d > 1) discard;
                float soft = smoothstep(1.0, 0.05, d);
                float alpha = i.state < 1.5 ? _InsideAlpha : _OutsideAlpha;
                float highlight = pow(saturate(1-d), 10) * 0.35;
                return half4(i.color + highlight, soft * alpha);
            }
            ENDHLSL
        }
    }
}

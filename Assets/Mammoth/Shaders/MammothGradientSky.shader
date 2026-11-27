Shader "Mammoth/GradientSky"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.27, 0.53, 0.88, 1)
        _Mid ("Mid sky", Color) = (0.52, 0.75, 0.96, 1)
        _Horizon ("Horizon", Color) = (0.84, 0.91, 0.97, 1)
        _Ground ("Below horizon", Color) = (0.80, 0.87, 0.95, 1)
        _SunColor ("Sun", Color) = (1, 0.96, 0.86, 1)
        _MidHeight ("Mid height", Range(0.05, 0.9)) = 0.3
        _SunGlow ("Sun glow", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Top, _Mid, _Horizon, _Ground, _SunColor;
            float _MidHeight, _SunGlow;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 c;
                if (h < 0)
                {
                    c = lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 6));
                }
                else
                {
                    c = lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0, 1, saturate(h / _MidHeight)));
                    c = lerp(c, _Top.rgb, smoothstep(0, 1, saturate((h - _MidHeight) / (1 - _MidHeight))));
                }
                float s = saturate(dot(d, normalize(_WorldSpaceLightPos0.xyz)));
                c += _SunColor.rgb * (pow(s, 8) * _SunGlow * 0.5 + pow(s, 700) * 1.5);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}

Shader "Custom/Standard_WithZones"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Albedo", 2D) = "white" {}
        
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _Glossiness("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}

        _BumpScale("Normal Scale", Float) = 1.0
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}

        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}

        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}

        // Zone Properties
        [Header(Zone Settings)]
        [Enum(Off,0,Local,1,Remote,2)] _ZoneMode("Zone Mode", Int) = 0
        _P("Voronoi Distance P", Range(1,2)) = 2
        _ZoneRadius("Zone Radius", Range(0.1, 5.0)) = 0.9
        _EnableZoneClipping("Enable Zone Clipping", Range(0, 1)) = 1
        _Length("Zone User Count", Int) = 0
        _BaseRegion("Base Region", Int) = 0
        _WhichRegion("Which Region", Int) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _MetallicGlossMap;
        sampler2D _OcclusionMap;
        sampler2D _EmissionMap;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 origWorldPos;
        };

        half _Glossiness;
        half _Metallic;
        half _BumpScale;
        half _OcclusionStrength;
        fixed4 _Color;
        half3 _EmissionColor;

        // Zone variables
        int _ZoneMode;
        float _P;
        float _ZoneRadius;
        float _EnableZoneClipping;
        int _Length;
        float4 _Users[10];
        int _WhichRegion;
        int _BaseRegion;

        // Per-client-only rendering placement shift (xz), fed by
        // DistanceMaintainMode from how far the LOCAL viewer has moved since
        // 거리유지모드 turned on. Applied to Remote-zone geometry only (see vert
        // below), and NEVER affects _Users or calculateZoneClip -- so it moves
        // WHERE this house's revealed cutout renders on this one screen,
        // without changing WHAT is revealed (that is still decided purely by
        // that house's own real avatar position in _Users) or anything
        // networked/visible on another client.
        float4 _LocalOffset;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.origWorldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

            if (_ZoneMode == 2) // RemoteZone only -- never shift my own house's rendering
            {
                v.vertex.xyz += mul((float3x3)unity_WorldToObject, float3(_LocalOffset.x, 0, _LocalOffset.z));
            }
        }

        bool isInsideCircle(float3 vertexPoint, int userNum)
        {
            float4 userPos = _Users[userNum];
            // pow(x, 2) with a possibly-negative x can return NaN on some GPUs
            // (pow is often exp2(log2(x)*y), and log2 of a negative is undefined) --
            // use plain squaring instead, which is also cheaper.
            float dx = vertexPoint.x - userPos.x;
            float dz = vertexPoint.z - userPos.z;
            float dist = sqrt(dx * dx + dz * dz);
            return dist <= _ZoneRadius;
        }

        // Pure Lp-norm Voronoi over the true (un-shifted) _Users positions --
        // exactly the same computation ZoneMode==2 always used, extracted so
        // LocalZone below can reuse it too.
        int closestZoneTrue(float3 worldPos)
        {
            float minDist = 1e8;
            int closest = 0;
            for (int i = 0; i < _Length; i++)
            {
                float dist = pow(
                    pow(abs(worldPos.x - _Users[i].x), _P) +
                    pow(abs(worldPos.z - _Users[i].z), _P),
                    1.0 / _P
                );
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = i;
                }
            }
            return closest;
        }

        // True iff house k's own real, un-shifted avatar position actually
        // reveals content at TRUE world position worldPos -- exactly
        // ZoneMode==2's own reveal test (content selection must never depend
        // on the local viewer's own placement shift). Extracted so LocalZone
        // below can ask the identical question -- "will Remote actually
        // render something here" -- instead of approximating it with a plain
        // circle test, which gets it wrong wherever house k's true circle
        // partially overlaps ANOTHER house (including mine): only part of
        // that circle is really house k's own content to begin with, and a
        // plain radius check doesn't know that.
        bool remoteReveals(float3 worldPos, int k)
        {
            if (!isInsideCircle(worldPos, k))
                return false;

            for (int j = 0; j < _Length; j++)
            {
                if (j != k && isInsideCircle(worldPos, j))
                {
                    if (closestZoneTrue(worldPos) != k) return false;
                }
            }
            return true;
        }

        bool calculateZoneClip(float3 worldPos)
        {
            if (_ZoneMode == 0 || _EnableZoneClipping < 0.5)
                return false;

            if (_ZoneMode == 1) // LocalZone
            {
                // My own floor yields here iff SOME other house's true,
                // un-shifted content is actually what's being displayed at
                // this spot right now -- i.e. remoteReveals() is true for the
                // TRUE position this display position maps back to (undo the
                // same _LocalOffset shift vert() applied when placing that
                // content). This keeps Local's yield mask and what Remote
                // actually renders identical by construction, everywhere,
                // instead of the two independently approximating "who owns
                // this circle" and occasionally disagreeing.
                float3 truePos = worldPos - float3(_LocalOffset.x, 0, _LocalOffset.z);
                for (int k = 0; k < _Length; k++)
                {
                    if (k == _BaseRegion) continue;
                    if (remoteReveals(truePos, k)) return true;
                }
                return false;
            }
            else if (_ZoneMode == 2) // RemoteZone -- unchanged behavior, now via the shared helper above
            {
                return !remoteReveals(worldPos, _WhichRegion);
            }

            return false;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Zone clipping check -- origWorldPos (pre-shift) so which part of
            // the house is revealed never depends on the local placement shift.
            if (calculateZoneClip(IN.origWorldPos))
            {
                clip(-1);
                return;
            }

            // Standard shader material properties
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            
            // Metallic and smoothness
            half4 metallicGloss = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = metallicGloss.r * _Metallic;
            o.Smoothness = metallicGloss.a * _Glossiness;
            
            // Normal map
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            
            // Occlusion
            half occ = tex2D(_OcclusionMap, IN.uv_MainTex).g;
            o.Occlusion = lerp(1.0, occ, _OcclusionStrength);
            
            // Emission
            o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor;
            
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
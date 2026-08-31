Shader "Custom/RemoteZone"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metalness", Range(0, 1)) = 0
        [HDR]_Emission ("Emission", Color) = (0,0,0)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}

        // voronoi
        _P("P", Range(1,2)) = 2
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows keepalpha

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half3 _Emission;

        // voronoi region
        float _P;
        int _Length;
        float4 _Users[10];
        fixed4 _Colors[10];
        int _WhichRegion;
        int _BaseRegion;

        // zone 1 and count
        uniform float4 _EachZone[1024];
        int _EachZoneCount;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        bool isInsideCircle(float3 vertexPoint, int userNum)
        {
    
            float4 userPos = _Users[userNum];
    
            // Calculate distance between vertex point and circle center
            float distance = sqrt(
                        pow(vertexPoint.x - userPos.x, 2) +
                        pow(vertexPoint.z - userPos.z, 2)
                   );
    
            // Check if distance is less than or equal to radius (0.9 meters)
            return distance <= 0.9f;
        }

        


        void surf(Input IN, inout SurfaceOutputStandard o)
        {
    
            float minDist = 1e8; 
            int minI = 0; 
            bool isInAnyCircle = false;
    

            for (int i = 0; i < _Length; i++)
            {
                float dist = pow(pow(abs(IN.worldPos.x - _Users[i].x), _P) + pow(abs(IN.worldPos.z - _Users[i].z), _P), 1 / _P);

                if (dist < minDist)
                {
                    minDist = dist;
                    minI = i;
                }
        
                // Check if point is in any circle except base region
                if (i != _BaseRegion && isInsideCircle(IN.worldPos, i))
                {
                    isInAnyCircle = true;
                }
            }
    
    
            // if not inside circle clip
            if (!isInAnyCircle)
            {
                clip(-1);
            }
            else
            {
        
                if (minI == _WhichRegion)
                {
                    
                    fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
                    o.Albedo = c.rgb;
                    o.Metallic = _Metallic;
                    o.Smoothness = _Glossiness;
                    o.Emission = _Emission;
                    o.Alpha = c.a;
                }
                else
                {
                    //여기서 문제생기는거 같은데
                    clip(-1);
                    /*
                    fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
                    o.Albedo = c.rgb;
                    o.Metallic = _Metallic;
                    o.Smoothness = _Glossiness;
                    o.Emission = _Emission;
                    o.Alpha = c.a;
                    */
                }
                
            }
        }

        ENDCG
    }



}

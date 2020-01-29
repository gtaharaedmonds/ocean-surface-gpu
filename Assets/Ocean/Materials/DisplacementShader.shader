/*
	Author: Gus Tahara-Edmonds
	Date: Summer 2019
	Purpose: Shader for my ocean. Has a main color and a seperate color for simple reflections and foam crests, as well as some basic lighting settings. 
	The vertex displacement is done here, in the shader, based on the texture passed from the generator program. This is ultra fast 
	as the texture does not need to be sent back to the CPU. 
	Note: There is some Fresnel code I found online
*/

Shader "Custom/Displacement" {
	Properties{
		_MainTex("Main", 2D) = "white" {}					//main texture of the water
		_SeaColor("SeaColor", Color) = (1,1,1,1)			//main color of the water
		_SkyColor("SkyColor", Color) = (1,1,1,1)			//sky color which reflects on the water
		_Resolution("Resolution", int) = 256				//resolution of the displacement textures to be sampled from
		_FoamScale("Foam Strength", float) = 0.5				//resolution of the displacement textures to be sampled from

		//basic lighting settings
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0
	}
		SubShader{
			Tags { "RenderType" = "Opaque" }
			LOD 300

			CGPROGRAM
			#pragma surface surf Standard fullforwardshadows vertex:disp nolightmap
			#pragma target 3.0

			float3 _SeaColor, _SkyColor;

			sampler2D _MainTex;
			sampler2D _DispTex;
			sampler2D _NormalMap;
			sampler2D _FoldingMap;
			sampler2D _FresnelLookUp;

			int _Resolution;
			half _FoamScale;
			half _Glossiness;
			half _Metallic;
			fixed4 _Color;

			struct appdata {
				float4 vertex : POSITION;
				float4 tangent : TANGENT;
				float3 normal : NORMAL;
				float2 texcoord : TEXCOORD0;
			};

			//function for modulus that can handle negative numbers
			int negMod(float a, float n) {
				float m = a - n * int(a / n);

				if (m < 0) {
					m = n + m;
				}

				return m;
			}

			//displaces vertices based on texture map
			void disp(inout appdata v) {
				float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				float2 worldCoord = float2(negMod(worldPos.x, _Resolution), negMod(worldPos.z, _Resolution));
				worldCoord /= _Resolution;

				float3 displacement = tex2Dlod(_DispTex, float4(worldCoord, 0, 0)).xyz;
				displacement = float3(displacement.x, displacement.y, displacement.z);
				v.vertex.xyz += displacement;

				v.normal = tex2Dlod(_NormalMap, float4(worldCoord, 0, 0)).xyz;
			}

			struct Input {
				float2 uv_MainTex;
				float3 worldNormal;
				float3 worldPos;
				float3 worldRefl;
			};

			//fresnel calculations for simple reflection appearance
			float Fresnel(float3 V, float3 N) {
				float costhetai = abs(dot(V, N));
				return tex2D(_FresnelLookUp, float2(costhetai, 0.0)).a;
			}

			void surf(Input IN, inout SurfaceOutputStandard o) {
				float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
				float3 N = IN.worldNormal;
				float fresnel = Fresnel(V, N);

				//uses the jacobian from main script to add white foam
				float3 color = lerp(_SeaColor, _SkyColor, fresnel);

				float2 worldCoord = IN.worldPos.xz / _Resolution - int2(IN.worldPos.xz / _Resolution);

				if (worldCoord.x < 0)
					worldCoord.x += 1;

				if (worldCoord.y < 0)
					worldCoord.y += 1;

				float jacobian = tex2D(_FoldingMap, float4(worldCoord, 0, 0));

				jacobian = smoothstep(0, 1, jacobian);
				o.Albedo = lerp(color, float3(1, 1, 1), jacobian * _FoamScale);
				o.Alpha = 1.0;

				o.Metallic = _Metallic;
				o.Smoothness = _Glossiness;
			}
			ENDCG
		}
		
		FallBack "Diffuse"
}
Shader "Minecraft/Blocks" {

	Properties {
		_MainTex ("Block Texture Atlas", 2D) = "white" {}
		_TextureScale ("Texture Scale", Float) = 0.25
		_AOStrength ("Ambient Occlusion Strength", Range(0, 1)) = 0.5
	}

	SubShader {
		
		Tags {"Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutout"}
		LOD 100
		Lighting Off

		Pass {
		
			CGPROGRAM
				#pragma vertex vertFunction
				#pragma fragment fragFunction
				#pragma target 2.0

				#include "UnityCG.cginc"

				struct appdata {
					float4 vertex : POSITION;
					float3 normal : NORMAL;
					float4 color : COLOR;
				};

				struct v2f {
					float4 vertex : SV_POSITION;
					float3 localPos : TEXCOORD0;
					float3 normal : TEXCOORD1;
					float4 color : COLOR;
				};

				sampler2D _MainTex;
				float4 _MainTex_ST;
				float _TextureScale;
				float _AOStrength;
				float GlobalLightLevel;
				float minGlobalLightLevel;
				float maxGlobalLightLevel;

				// Block type is stored in color.r
				// Light level is stored in color.g (0-15)
				// 0 = Air, 1 = Dirt, 2 = Grass, 3 = Stone, 4 = Sand, 5 = Water, 6 = Deepslate

				float2 GetTileOffset(float blockType, float faceIndex) {
					// Stone
					if (blockType == 3) {
						return float2(0.25, 0.5);
					}
					// Dirt
					else if (blockType == 1) {
						return float2(0.25, 0.75);
					}
					// Grass
					else if (blockType == 2) {
						if (faceIndex == 0.0) // Top face
							return float2(0.0, 0.75);
						if (faceIndex == 1.0) // Bottom face
							return float2(0.25, 0.75);
						return float2(0.0, 0.5); // Side faces
					}
					// Deepslate
					else if (blockType == 6) {
						return float2(0.5, 0.75);
					}
					// Sand
					else if (blockType == 4) {
						return float2(0.75, 0.75);
					}
					// Default
					return float2(0.0, 0.0);
				}

				v2f vertFunction (appdata v) {
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.localPos = v.vertex.xyz;
					o.normal = UnityObjectToWorldNormal(v.normal);
					o.color = v.color;
					return o;
				}

				fixed4 fragFunction (v2f i) : SV_Target {
					// Get the dominant normal component
					float3 normal = normalize(i.normal);
					float3 absNormal = abs(normal);
					
					// Get the fractional part of the local position for repeating textures
					float3 fracPos = frac(i.localPos);
					
					// Determine face index based on normal
					float faceIndex;
					if (absNormal.x > absNormal.y && absNormal.x > absNormal.z) {
						faceIndex = normal.x > 0 ? 3.0 : 2.0; // Right : Left
					} else if (absNormal.y > absNormal.z) {
						faceIndex = normal.y > 0 ? 0.0 : 1.0; // Top : Bottom
					} else {
						faceIndex = normal.z > 0 ? 4.0 : 5.0; // Front : Back
					}

					// Get base UV coordinates
					float2 uv;
					if (absNormal.x > absNormal.y && absNormal.x > absNormal.z) {
						uv = fracPos.zy * _TextureScale;
					} else if (absNormal.y > absNormal.z) {
						uv = fracPos.xz * _TextureScale;
					} else {
						uv = fracPos.xy * _TextureScale;
					}

					// Add the tile offset based on block type and face
					uv += GetTileOffset(i.color.r, faceIndex);

					// Sample the texture
					fixed4 col = tex2D(_MainTex, uv);

					// Apply lighting and ambient occlusion
					float shade = (maxGlobalLightLevel - minGlobalLightLevel) * GlobalLightLevel + minGlobalLightLevel;
					
					// Use the block light directly - it's already in 0.1-1.0 range
					float blockLight = i.color.g;
					
					// Combine global and block lighting
					shade = min(shade, blockLight);
					
					// Apply ambient occlusion (higher AO value = darker)
					float ao = 1.0 - (i.color.a * _AOStrength);
					col.rgb *= ao;

					clip(col.a - 1);
					// Apply lighting (higher shade = brighter)
					col.rgb = lerp(float4(0, 0, 0, 1), col, shade - 0.07).rgb;

					return col;
				}

				ENDCG
		}
	}
}
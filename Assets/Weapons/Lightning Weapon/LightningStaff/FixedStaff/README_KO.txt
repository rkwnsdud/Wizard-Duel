번개 지팡이 수정본

변경: 표시한 수정체 옆 연결체 제거 및 절단면 2곳 메움.
수정체에 Crystal_Cyan_Emission 재질을 별도로 배정했습니다.
원래 수정체 무늬는 유지하고 일정한 청색 발광을 더했습니다.
원본 모델의 크기/원점 유지. Unity 프리팹/스크립트는 포함되지 않습니다.

FBX + LightningStaff_Fixed.fbm 폴더를 함께 보관하세요.
텍스처: Texture_0 = Base Color, Texture_1 = glTF Metallic/Roughness (G Roughness, B Metallic), Texture_2 = Normal, Texture_3 = 기존 Emission.
Texture_1은 Unity의 Metallic/Smoothness 채널 규격과 다릅니다. 그대로 Metallic 슬롯에 넣지 마세요.

Unity 적용 시:
기존 금속/봉 재질은 첫 번째 슬롯 Staff_Original에 연결할 수 있습니다.
두 번째 슬롯 Crystal_Cyan_Emission은 수정체 전용입니다.
URP/Lit 기준 Base Map은 Texture_0, Metallic 0, Smoothness 약 0.66.
수정체 Emission에는 기존의 얼룩진 Texture_3을 연결하지 마세요.
이번 수정체는 텍스처 없이 균일한 발광색을 사용합니다.
제작 시 선형 RGB (0.035, 0.30, 0.62), 강도 1.2를 사용했습니다.
Unity의 톤매핑/노출에 따라 보이는 밝기는 달라지므로 현 프로젝트에서 최종 조절하세요.
GLB는 발광 재질을 포함하며 FBX의 재질 변환 결과는 사용하는 임포터에 따라 달라질 수 있습니다.

검증: Blender에서 앞/뒤/사선 렌더 확인, FBX/GLB 재임포트 성공.
Unity 실행 검증은 수행하지 않았습니다.

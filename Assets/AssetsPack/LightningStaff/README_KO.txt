번개 지팡이 — Unity / Blender

실제 3D 모델: LightningStaff.obj + LightningStaff.mtl
768 삼각형, 28개 파츠. 텍스처 없이 4개 재질. 높이 약 1.87m.
Y축이 위쪽이며 (0,0,0)은 손잡이 기준점입니다.

Unity URP 사용법 (추천)
1. 이 LightningStaff 폴더 전체를 Unity 프로젝트 Assets 안에 복사합니다.
2. 스크립트 컴파일이 끝나면 메뉴 Tools > Lightning Staff > Create URP Prefab 실행.
3. Generated/LightningStaff.prefab을 씬이나 플레이어 손 타깃 아래에 배치합니다.
4. 손 타깃 자식으로 붙이면 Local Position = 0, Local Rotation = 0으로 시작하고
   캐릭터 손 방향에 맞춰 회전을 조정합니다. 실제 크기: Scale = 1.
   머리 위/아래로 긴 형태이므로 캐릭터 비율에 맞춰 전체 크기를 줄일 수 있습니다.
5. SpellOrigin 자식은 발사/효과 기준점입니다.

생성 도구는 OBJ를 직접 읽어 평면 노멀, 4개 URP 재질과 프리팹을 만듭니다.
파란 크리스탈은 Emission이 설정되며 화면 밖으로 번지는 빛은 Bloom 설정에 따라 달라집니다.
콜라이더, 물리, 공격 동작, 번개 파티클은 포함하지 않았습니다.
메뉴를 다시 실행하면 Generated 안의 동일 이름 결과가 업데이트됩니다.
기본 Unity OBJ import로도 모델을 사용할 수 있지만 URP 재질은 위 메뉴 사용을 권장합니다.

Blender 원본 / FBX 만들기
CreateBlenderModel.py와 mesh_data.json을 같은 폴더에 둡니다.
Blender > Scripting > Open에서 CreateBlenderModel.py 열기 > Run Script.
현재 씬을 비우므로 새 Blender 파일에서 실행하세요.
옆에 LightningStaff.blend와 LightningStaff.fbx를 저장합니다.
이 패키지에는 실행 환경 제한으로 .blend/.fbx 완성 파일 대신 생성 스크립트가 들어 있습니다.

Preview.png는 실제 OBJ와 같은 메시를 렌더링한 미리보기입니다.
검증: 모든 삼각형 인덱스, 퇴화 면, 파츠별 닫힌 메시 및 개수 확인.
Unity/Blender 실행 환경이 없어 실제 앱에서 가져오기 실행은 확인하지 못했습니다.

# M1 Monster Sprite v004 Production Drop

이 폴더는 외부 또는 별도 에셋 제작 파이프라인의 **검수 원본**만 받는다. Unity에서 바로 사용하는 PNG를 이 폴더에 넣지 않는다.

## 작업 순서

1. `m1_monster_sprite_v004_manifest.json`에서 역할·상태·방향·프레임 수를 확인한다.
2. 원본은 `<role>/<direction>/<state>/frame_00.png` 형식으로 납품한다.
3. 각 상태의 `review_contact_sheet.png`와 `manifest.json`에 프롬프트, 원본 해시, 검수자, 검수 결과를 기록한다.
4. 아트 승인 후에만 128px Unity 스트립을 `Assets/_Project/Art/M1Production/Characters/Animation/MotionsV004/`에 내보낸다.

## 절대 금지

- v003 파일을 덮어쓰기
- 여러 방향·모션·캐릭터를 한 이미지에 합성하기
- 크로마키 배경, UI, 그림자, 텔레그래프, 투사체를 캐릭터 본체 원본에 포함하기
- 미승인 원본을 Unity 씬·프리팹·빌드에 연결하기

자세한 미술·피벗·캔버스 계약은 `Docs/Design/M1_SPRITE_PRODUCTION_BRIEF_V004_KO.md`가 기준이다.

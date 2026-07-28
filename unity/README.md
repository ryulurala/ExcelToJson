# 🎮 Unity Auto Excel Converter

유니티 백그라운드 갱신 툴입니다.

엑셀 파일을 수정하고 저장하는 Excel to Json을 실행합니다.

## 📂 권장 폴더 구조

빌드 용량 최적화 및 유니티의 불필요한 에셋 스캔을 막기 위해, **변환기 실행 파일과 엑셀 원본은 `Assets` 폴더 바깥에 위치**시키는 것을 권장합니다.

```text
Workspace/
├── UnityProject/
│   ├── Assets/
│   │   └── Data/       # (자동 생성됨) 변환된 JSON 파일이 들어오는 곳
│   │   ├── Scripts/
│   │   │   └── Editor/    # 👈 AutoExcelConverter.cs 스크립트 위치
├── Excels/                       # 👈 기획자 엑셀 원본 파일을 넣는 곳
└── Tools/                        # 👈 변환기 실행 파일 폴더 (exe / macos)
```
